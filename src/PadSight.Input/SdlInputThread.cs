// 専用のスレッド 1 本で、SDL の初期化・イベントの待ち受け・Core の処理・終了までを行う(仕様書 D-8)。
// 画面には不変のスナップショットを渡すだけで、UI のスレッドから SDL を呼ばない。
// UI からの頼み(履歴を消す、設定を変えるなど)は Post で受け、このスレッドで実行する。
using System.Collections.Concurrent;
using System.Diagnostics;
using PadSight.Core;
using SDL3;

namespace PadSight.Input;

public sealed record InputOptions(string Wgi, bool HidapiPlayStation, bool HidapiSwitch);

public sealed class SdlClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public ulong MonotonicNs => SDL.GetTicksNS();
}

public sealed class SdlInputThread
{
    const long MinPublishTicks = 4; // ミリ秒。描くのは画面の 1 フレームに 1 回なので、これより細かく出しても意味が無い
    const ulong TickNs = 250 * Values.NsPerMs;
    const long MappingFileLimit = 1024 * 1024;

    readonly ConcurrentQueue<Action<InputEngine>> _commands = new();
    readonly Dictionary<uint, (nint Handle, bool IsGamepad)> _open = [];
    readonly Action<PadSnapshot> _publish;
    readonly Action<IReadOnlyList<SessionRecord>> _finalized;
    readonly Action<SessionRecord> _checkpoint;
    readonly Action<string> _log;
    readonly string? _mappingFile;
    readonly AutoResetEvent _wake = new(false);
    readonly InputEngine _engine;
    Thread? _thread;
    volatile bool _stop;
    InputOptions _options;
    InputOptions? _reconnectTo;
    int? _mappingCount;
    // FR-40: デモは本物とは別の InputEngine で動かし、確定した集計は捨てる。時計は SDL に頼らない(初期化に失敗していても流せる)
    int _demoRequest = -1; // -1 頼み無し、0 止める、1 流す
    DemoPlayer? _demo;
    InputEngine? _demoEngine;
    EngineConfig? _demoConfigFrom;
    static readonly StopwatchClock DemoClock = new();

    sealed class StopwatchClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public ulong MonotonicNs => (ulong)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
    }

    public SdlInputThread(EngineConfig config, InputOptions options, string? mappingFile,
        Action<PadSnapshot> publish, Action<IReadOnlyList<SessionRecord>> finalized, Action<SessionRecord> checkpoint, Action<string> log)
    {
        _engine = new InputEngine(new SdlClock(), config);
        _options = options;
        _mappingFile = mappingFile;
        _publish = publish;
        _finalized = finalized;
        _checkpoint = checkpoint;
        _log = log;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SDL input" };
        _thread.Start();
    }

    /// <summary>SDL のスレッドで実行する頼みを足す。</summary>
    public void Post(Action<InputEngine> command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    /// <summary>デモの入力を流す・止める(FR-40)。</summary>
    public void SetDemo(bool on)
    {
        Volatile.Write(ref _demoRequest, on ? 1 : 0);
        _wake.Set();
    }

    /// <summary>入力の取り方を変えて、SDL を初期化し直す(FR-8)。</summary>
    public void Reconnect(InputOptions options)
    {
        _reconnectTo = options;
        _wake.Set();
    }

    /// <summary>今のセッションを確定し、SDL を止める。確定した集計は finalized で渡す。</summary>
    public void Stop()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));
    }

    void Run()
    {
        bool ok = Init();
        long lastPublish = 0;
        ulong lastTick = 0;
        bool dirty = true;
        while (!_stop)
        {
            if (_reconnectTo is { } next)
            {
                _reconnectTo = null;
                Shutdown(keepSession: true);
                _publish(_engine.Snapshot(0, Stopwatch.GetTimestamp(), InputStatus.Reconnecting));
                _options = next;
                ok = Init();
                dirty = true;
            }
            while (_commands.TryDequeue(out var cmd))
            {
                cmd(_engine);
                dirty = true;
            }
            switch (Interlocked.Exchange(ref _demoRequest, -1))
            {
                case 1 when _demo is null:
                    ulong start = DemoClock.MonotonicNs;
                    _demo = new DemoPlayer(start);
                    _demoEngine = new InputEngine(DemoClock, DemoConfig());
                    _demoEngine.Apply(_demo.Added());
                    break;
                case 0:
                    _demo = null;
                    _demoEngine = null;
                    dirty = true; // 本物の状態を出し直す
                    break;
            }
            if (!ok)
            {
                FlushFinalized();
                if (_demo is not null) StepDemo();
                _wake.WaitOne(_demo is null ? 250 : DemoStepMs);
                continue;
            }
            long sinceMs = (Stopwatch.GetTimestamp() - lastPublish) * 1000 / Stopwatch.Frequency;
            int timeout = dirty ? (int)Math.Max(0, MinPublishTicks - sinceMs) : 250;
            if (_demo is not null)
                timeout = Math.Min(timeout, DemoStepMs);
            if (SDL.WaitEventTimeout(out var e, timeout))
            {
                Handle(e);
                while (SDL.PollEvent(out e))
                    Handle(e);
                dirty = true;
            }
            ulong now = SDL.GetTicksNS();
            if (now - lastTick >= TickNs)
            {
                lastTick = now;
                if (_engine.Tick(now) is { } cp)
                    _checkpoint(cp);
                dirty = true;
            }
            FlushFinalized();
            sinceMs = (Stopwatch.GetTimestamp() - lastPublish) * 1000 / Stopwatch.Frequency;
            if (_demo is not null)
            {
                if (sinceMs >= DemoStepMs)
                {
                    lastPublish = Stopwatch.GetTimestamp();
                    StepDemo();
                }
                dirty = false;
            }
            else if (dirty && sinceMs >= MinPublishTicks)
            {
                lastPublish = Stopwatch.GetTimestamp();
                _publish(_engine.Snapshot(now, lastPublish, InputStatus.Running, null, _mappingCount));
                dirty = false;
            }
        }
        _engine.CloseSession();
        FlushFinalized();
        if (ok)
            Shutdown(keepSession: false);
    }

    bool Init()
    {
        bool inited;
        string err;
        try
        {
            SdlHints.Apply(_options.Wgi, _options.HidapiPlayStation, _options.HidapiSwitch);
            inited = SDL.Init(SDL.InitFlags.Gamepad);
            err = inited ? "" : (SDL.GetError() ?? "").Split('\n')[0];
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            inited = false; // E-19: SDL3.dll が無い・読めない
            err = ex.Message.Split('\n')[0];
        }
        if (!inited)
        {
            _log("SDL の初期化に失敗: " + err);
            _publish(_engine.Snapshot(0, Stopwatch.GetTimestamp(), InputStatus.Failed, err));
            return false;
        }
        _log($"SDL {SdlHints.SdlVersion()} を初期化した。ヒント: " +
            string.Join(", ", SdlHints.AllNames.Select(n => $"{n}={SDL.GetHint(n) ?? "(未設定)"}")));
        _mappingCount = null;
        if (_mappingFile is not null && File.Exists(_mappingFile))
        {
            // 外から来るファイルなので、1 MB を超える物は SDL に渡さない(NFR §4)
            _mappingCount = new FileInfo(_mappingFile).Length > MappingFileLimit ? -1 : SDL.AddGamepadMappingsFromFile(_mappingFile);
            _log(_mappingCount >= 0 ? $"gamecontrollerdb.txt から {_mappingCount} 件を読んだ" : "gamecontrollerdb.txt を読めなかった");
        }
        return true;
    }

    void Shutdown(bool keepSession)
    {
        foreach (var (id, o) in _open)
        {
            if (o.IsGamepad) SDL.CloseGamepad(o.Handle); else SDL.CloseJoystick(o.Handle);
            if (keepSession) _engine.DropPad(id); // E-29: つなぎ直しではセッションと履歴を続ける
        }
        _open.Clear();
        SDL.Quit();
    }

    const int DemoStepMs = 16;

    // デモでは固定のパッドを出したいので、選び方は「最後に操作」にする
    EngineConfig DemoConfig() => (_demoConfigFrom = _engine.Config) with { SelectionMode = "last_used", PinnedGuid = null };

    void StepDemo()
    {
        if (!ReferenceEquals(_demoConfigFrom, _engine.Config))
            _demoEngine!.Configure(DemoConfig()); // 履歴の行やボタンの設定を変えたら、デモにも効かせる
        long sw = Stopwatch.GetTimestamp();
        ulong now = (ulong)(sw * (1e9 / Stopwatch.Frequency));
        foreach (var ev in _demo!.Advance(now))
            _demoEngine!.Apply(ev);
        _demoEngine!.Tick(now);
        _demoEngine.TakeFinalized(); // FR-40: デモの集計は保存しない
        _publish(_demoEngine.Snapshot(now, sw, InputStatus.Running, null, _mappingCount));
    }

    void FlushFinalized()
    {
        var list = _engine.TakeFinalized();
        if (list.Count > 0)
            _finalized(list);
    }

    void Handle(SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.GamepadAdded:
                AddGamepad(e.GDevice.Which, e.GDevice.Timestamp);
                break;
            case SDL.EventType.JoystickAdded:
                AddJoystick(e.JDevice.Which, e.JDevice.Timestamp);
                break;
            case SDL.EventType.GamepadRemoved:
                Remove(e.GDevice.Which, e.GDevice.Timestamp, gamepad: true);
                break;
            case SDL.EventType.JoystickRemoved:
                Remove(e.JDevice.Which, e.JDevice.Timestamp, gamepad: false);
                break;
            case SDL.EventType.GamepadButtonDown or SDL.EventType.GamepadButtonUp:
                if (e.GButton.Button <= DeviceCatalog.MaxMappedButton)
                    _engine.Apply(new ButtonChanged(e.GButton.Timestamp, e.GButton.Which, (PadButton)e.GButton.Button, e.GButton.Down));
                break;
            case SDL.EventType.GamepadAxisMotion:
                if (e.GAxis.Axis <= (byte)SDL.GamepadAxis.RightTrigger)
                    _engine.Apply(new AxisChanged(e.GAxis.Timestamp, e.GAxis.Which, (PadAxis)e.GAxis.Axis, e.GAxis.Value));
                break;
            case SDL.EventType.JoystickButtonDown or SDL.EventType.JoystickButtonUp when IsRawJoystick(e.JButton.Which):
                _engine.Apply(new RawButtonChanged(e.JButton.Timestamp, e.JButton.Which, e.JButton.Button, e.JButton.Down));
                break;
            case SDL.EventType.JoystickAxisMotion when IsRawJoystick(e.JAxis.Which):
                _engine.Apply(new RawAxisChanged(e.JAxis.Timestamp, e.JAxis.Which, e.JAxis.Axis, e.JAxis.Value));
                break;
            case SDL.EventType.JoystickHatMotion when IsRawJoystick(e.JHat.Which):
                _engine.Apply(new RawHatChanged(e.JHat.Timestamp, e.JHat.Which, e.JHat.Hat, e.JHat.Value));
                break;
            // センサーとタッチパッドのイベントは受けても捨てる(FR-2)
        }
    }

    bool IsRawJoystick(uint id) => _open.TryGetValue(id, out var o) && !o.IsGamepad;

    void AddGamepad(uint id, ulong ts)
    {
        if (_open.TryGetValue(id, out var o))
        {
            if (o.IsGamepad)
                return;
            Remove(id, ts, gamepad: false); // 割り当てが後から付いたジョイスティック
        }
        nint h = SDL.OpenGamepad(id);
        if (h == 0)
        {
            _log("パッドを開けなかった: " + (SDL.GetError() ?? "").Split('\n')[0]);
            return;
        }
        _open[id] = (h, true);
        var info = DeviceCatalog.Gamepad(id, h);
        _log($"パッドがつながった: {info.TypeName} {Short(info.Guid)}");
        _engine.Apply(new PadAdded(ts, id, info));
    }

    void AddJoystick(uint id, ulong ts)
    {
        if (SDL.IsGamepad(id) || _open.ContainsKey(id))
            return; // ゲームパッドは GamepadAdded で開く
        nint h = SDL.OpenJoystick(id);
        if (h == 0)
        {
            _log("ジョイスティックを開けなかった: " + (SDL.GetError() ?? "").Split('\n')[0]);
            return;
        }
        _open[id] = (h, false);
        var info = DeviceCatalog.Joystick(id, h);
        _log($"割り当ての無いジョイスティックがつながった: {Short(info.Guid)}");
        _engine.Apply(new PadAdded(ts, id, info));
    }

    void Remove(uint id, ulong ts, bool gamepad)
    {
        if (!_open.TryGetValue(id, out var o) || o.IsGamepad != gamepad)
            return;
        if (o.IsGamepad) SDL.CloseGamepad(o.Handle); else SDL.CloseJoystick(o.Handle);
        _open.Remove(id);
        _log("パッドが外れた: " + (o.IsGamepad ? "gamepad" : "joystick"));
        _engine.Apply(new PadRemoved(ts, id));
    }

    static string Short(string guid) => guid.Length > 8 ? guid[..8] : guid;
}
