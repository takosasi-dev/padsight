// 専用のスレッド 1 本で、SDL の初期化・イベントの待ち受け・Core の処理・終了までを行う(仕様書 D-8)。
// 画面には不変のスナップショットを渡すだけで、UI のスレッドから SDL を呼ばない。
// UI からの頼み(履歴を消す、設定を変えるなど)は Post で受け、このスレッドで実行する。
using System.Collections.Concurrent;
using System.Diagnostics;
using PadSight.Core;
using SDL3;

namespace PadSight.Input;

public sealed record InputOptions(string Wgi, bool HidapiPlayStation, bool HidapiSwitch);

/// <summary>SDL の時計を TimeBase で続けて数えた物(E-29)。SDL のスレッドからだけ使う。</summary>
public sealed class SdlClock(TimeBase time) : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public ulong MonotonicNs => time.Map(SDL.GetTicksNS());
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
    readonly TimeBase _time = new();
    Thread? _thread;
    volatile bool _stop;
    volatile bool _sdlReady; // 真の間だけ、Post で SDL の待ちを起こす
    InputOptions _options;
    string? _initError;
    InputOptions? _reconnectTo;
    int? _mappingCount;
    // FR-40: デモは本物とは別の InputEngine で動かし、確定した集計は捨てる。時計は SDL に頼らない(初期化に失敗していても流せる)
    int _demoRequest = -1; // -1 頼み無し、0 止める、1 流す
    DemoPlayer? _demo;
    InputEngine? _demoEngine;
    EngineConfig? _demoConfigFrom;
    static readonly StopwatchClock DemoClock = new();
    // §9.20: 診断のタブが測っている間だけ、イベント 1 件ごとの状態を積む。UI が描画のたびに取り出す。上限を超えたら古い物から捨てる
    const int DiagCap = 20_000;
    readonly ConcurrentQueue<DiagSample> _diag = new();
    int _diagCount;
    Action<DiagSample>? _diagTap;
    // SDL の関数を呼ぶ頼み(振動テスト)。SDL が動いているときだけ、このスレッドで実行する
    readonly ConcurrentQueue<Action> _sdlCommands = new();
    // §9.20 の電池。5 秒ごとに問い合わせ、変わったときだけ写しを作り直す
    const ulong PowerEveryNs = 5 * Values.NsPerSecond;
    ulong _lastPowerNs;
    IReadOnlyDictionary<uint, PadPower> _power = new Dictionary<uint, PadPower>();

    sealed class StopwatchClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public ulong MonotonicNs => (ulong)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
    }

    public SdlInputThread(EngineConfig config, InputOptions options, string? mappingFile,
        Action<PadSnapshot> publish, Action<IReadOnlyList<SessionRecord>> finalized, Action<SessionRecord> checkpoint, Action<string> log)
    {
        _engine = new InputEngine(new SdlClock(_time), config);
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
        Wake();
    }

    /// <summary>SDL のイベントを待っている間でも、すぐに頼みを見るように起こす(入力が無いと最大 250 ミリ秒待っていた)。</summary>
    void Wake()
    {
        _wake.Set();
        if (_sdlReady)
        {
            // 自分のキューに積むだけで、パッドには何も送らない
            var e = new SDL.Event { Type = (uint)SDL.EventType.User };
            SDL.PushEvent(ref e);
        }
    }

    /// <summary>診断用の細かい値を積み始める・やめる(§9.20)。やめたら積んである物も捨てる。</summary>
    public void SetDiagTap(bool on) => Post(engine =>
    {
        _diagTap = on ? EnqueueDiag : null;
        engine.DiagTap = _diagTap;
        if (_demoEngine is not null)
            _demoEngine.DiagTap = _diagTap;
        if (!on)
            while (_diag.TryDequeue(out _))
                Interlocked.Decrement(ref _diagCount);
    });

    void EnqueueDiag(DiagSample s)
    {
        _diag.Enqueue(s);
        if (Interlocked.Increment(ref _diagCount) > DiagCap && _diag.TryDequeue(out _))
            Interlocked.Decrement(ref _diagCount);
    }

    /// <summary>積んである診断用の細かい値を、古い順に into へ移す(UI のスレッドから呼ぶ)。</summary>
    public void DrainDiag(List<DiagSample> into)
    {
        while (_diag.TryDequeue(out var s))
        {
            Interlocked.Decrement(ref _diagCount);
            into.Add(s);
        }
    }

    /// <summary>振動テスト(FR-58・D-28)。利用者がボタンを押したときだけ呼ぶ。triggers なら low・high は左右のトリガー。</summary>
    public void Vibrate(uint padId, ushort low, ushort high, bool triggers)
    {
        _sdlCommands.Enqueue(() =>
        {
            if (!_open.TryGetValue(padId, out var o) || !o.IsGamepad)
                return;
            bool sent = triggers ? PadVibration.Triggers(o.Handle, low, high) : PadVibration.Motors(o.Handle, low, high);
            _log(sent ? "振動テストを送った" : "振動テストを送れなかった: " + (SDL.GetError() ?? "").Split('\n')[0]);
        });
        Wake();
    }

    /// <summary>デモの入力を流す・止める(FR-40)。</summary>
    public void SetDemo(bool on)
    {
        Volatile.Write(ref _demoRequest, on ? 1 : 0);
        Wake();
    }

    /// <summary>入力の取り方を変えて、SDL を初期化し直す(FR-8)。</summary>
    public void Reconnect(InputOptions options)
    {
        _reconnectTo = options;
        Wake();
    }

    /// <summary>今のセッションを確定し、SDL を止める。確定した集計は finalized で渡す。</summary>
    public void Stop()
    {
        _stop = true;
        Wake();
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
                if (ok)
                    Shutdown(keepSession: true);
                else // SDL が動いていないので止める物が無い(SDL を呼ぶと、DLL が無いときに落ちる)
                    foreach (var id in _engine.Pads.Keys.ToList())
                        _engine.DropPad(id);
                _publish(_engine.Snapshot(_time.Map(0), Stopwatch.GetTimestamp(), InputStatus.Reconnecting));
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
                    _demoEngine = new InputEngine(DemoClock, DemoConfig()) { DiagTap = _diagTap }; // デモでも診断を試せるように
                    _demoEngine.Apply(_demo.Added());
                    break;
                case 0:
                    _demo = null;
                    _demoEngine = null;
                    dirty = true; // 本物の状態を出し直す
                    break;
            }
            if (ok)
                while (_sdlCommands.TryDequeue(out var sdlCommand))
                    sdlCommand();
            else
                _sdlCommands.Clear(); // SDL が動いていないので送れない(呼ぶと DLL が無いときに落ちる)
            if (!ok)
            {
                FlushFinalized();
                if (_demo is not null) StepDemo();
                else if (dirty)
                {
                    // デモを止めた後などに、最後のデモの絵が残らないように失敗の状態を出し直す
                    _publish(_engine.Snapshot(_time.Map(0), Stopwatch.GetTimestamp(), InputStatus.Failed, _initError));
                    dirty = false;
                }
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
            ulong now = _time.Map(SDL.GetTicksNS());
            if (now - lastTick >= TickNs)
            {
                lastTick = now;
                if (_engine.Tick(now) is { } cp)
                    _checkpoint(cp);
                dirty = true;
                if (Values.Since(now, _lastPowerNs) >= PowerEveryNs)
                {
                    _lastPowerNs = now;
                    UpdatePower();
                }
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
                _publish(_engine.Snapshot(now, lastPublish, InputStatus.Running, null, _mappingCount) with { Power = _power });
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
            _initError = err;
            _log("SDL の初期化に失敗: " + err);
            _publish(_engine.Snapshot(_time.Map(0), Stopwatch.GetTimestamp(), InputStatus.Failed, err));
            return false;
        }
        _log($"SDL {SdlHints.SdlVersion()} を初期化した。ヒント: " +
            string.Join(", ", SdlHints.AllNames.Select(n => $"{n}={SDL.GetHint(n) ?? "(未設定)"}")));
        _sdlReady = true;
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
        _sdlReady = false;
        _time.BeforeRestart(SDL.GetTicksNS()); // 次の SDL_Init で時計が 0 に戻るので、ここまでの時刻を足しておく
        foreach (var (id, o) in _open)
        {
            if (o.IsGamepad) SDL.CloseGamepad(o.Handle); else SDL.CloseJoystick(o.Handle);
            if (keepSession) _engine.DropPad(id); // E-29: つなぎ直しではセッションと履歴を続ける
        }
        _open.Clear();
        SDL.Quit();
    }

    /// <summary>つながっているパッドの電池を問い合わせ、変わっていれば写しを作り直す(§9.20)。</summary>
    void UpdatePower()
    {
        var next = new Dictionary<uint, PadPower>();
        foreach (var (id, o) in _open)
            if (o.IsGamepad)
                next[id] = DeviceCatalog.Power(o.Handle);
        if (next.Count != _power.Count || next.Any(kv => !_power.TryGetValue(kv.Key, out var p) || p != kv.Value))
            _power = next;
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
                AddGamepad(e.GDevice.Which, _time.Map(e.GDevice.Timestamp));
                break;
            case SDL.EventType.JoystickAdded:
                AddJoystick(e.JDevice.Which, _time.Map(e.JDevice.Timestamp));
                break;
            case SDL.EventType.GamepadRemoved:
                Remove(e.GDevice.Which, _time.Map(e.GDevice.Timestamp), gamepad: true);
                break;
            case SDL.EventType.JoystickRemoved:
                Remove(e.JDevice.Which, _time.Map(e.JDevice.Timestamp), gamepad: false);
                break;
            case SDL.EventType.GamepadButtonDown or SDL.EventType.GamepadButtonUp:
                if (e.GButton.Button <= DeviceCatalog.MaxMappedButton)
                    _engine.Apply(new ButtonChanged(_time.Map(e.GButton.Timestamp), e.GButton.Which, (PadButton)e.GButton.Button, e.GButton.Down));
                break;
            case SDL.EventType.GamepadAxisMotion:
                if (e.GAxis.Axis <= (byte)SDL.GamepadAxis.RightTrigger)
                    _engine.Apply(new AxisChanged(_time.Map(e.GAxis.Timestamp), e.GAxis.Which, (PadAxis)e.GAxis.Axis, e.GAxis.Value));
                break;
            case SDL.EventType.JoystickButtonDown or SDL.EventType.JoystickButtonUp when IsRawJoystick(e.JButton.Which):
                _engine.Apply(new RawButtonChanged(_time.Map(e.JButton.Timestamp), e.JButton.Which, e.JButton.Button, e.JButton.Down));
                break;
            case SDL.EventType.JoystickAxisMotion when IsRawJoystick(e.JAxis.Which):
                _engine.Apply(new RawAxisChanged(_time.Map(e.JAxis.Timestamp), e.JAxis.Which, e.JAxis.Axis, e.JAxis.Value));
                break;
            case SDL.EventType.JoystickHatMotion when IsRawJoystick(e.JHat.Which):
                _engine.Apply(new RawHatChanged(_time.Map(e.JHat.Timestamp), e.JHat.Which, e.JHat.Hat, e.JHat.Value));
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
