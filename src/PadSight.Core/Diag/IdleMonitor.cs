// 放置監視(仕様書 FR-59。PadLab の IdleMonitor を移した物)。触っていないパッドに勝手に入った押下・スティックとトリガーの揺れ・切断と再接続を、始めからの時間つきで数える。
// DiagSample はイベントのときしか来ないので、時間は Advance(今の時刻)でも進め、決めた長さで止まる。報告の途切れは SDL からは見えないので数えない。
// 閾値は PadLab と同じ(スティックは始めた時の位置から 0.05、トリガーは 0.05。越えた立ち上がりで 1 件)。メモリだけで持ち、保存しない(INV-8)。
namespace PadSight.Core.Diag;

public enum IdleEventKind { StickDrift, PhantomButton, TriggerWobble, Disconnected, Reconnected }

/// <param name="OffsetNs">始めてからの時間</param>
/// <param name="Detail">スティック・トリガーは「左スティック」など、ボタンは PadButton の名前、切断・再接続は空</param>
/// <param name="Magnitude">揺れの大きさ(0〜1)。ボタンは 1、切断・再接続は 0</param>
public sealed record IdleEvent(ulong OffsetNs, IdleEventKind Kind, string Detail, float Magnitude, PadButton? Button = null);

public sealed record IdleMonitorSettings(double DurationSeconds = 600, float StickThreshold = 0.05f, float TriggerThreshold = 0.05f);

public sealed record IdleSummary(double ElapsedSeconds, bool Running, bool Finished,
    IReadOnlyList<IdleEvent> Events, int DriftEvents, int ButtonEvents, int TriggerEvents, int DisconnectEvents,
    float MaxLeftDeviation, float MaxRightDeviation)
{
    public int TotalEvents => DriftEvents + ButtonEvents + TriggerEvents + DisconnectEvents;
}

public sealed class IdleMonitor(IdleMonitorSettings? settings = null)
{
    /// <summary>一覧に持つ件数の上限(回数はこの先も数える)。揺れが閾値をまたぎ続けると件数が際限なく増えるため。</summary>
    public const int MaxListed = 2000;

    readonly List<IdleEvent> _events = [];
    bool _running, _finished;
    ulong _startNs, _lastNs;
    StickValue _leftCenter, _rightCenter;
    float _maxLeft, _maxRight;
    bool _leftOver, _rightOver, _ltOver, _rtOver;
    uint _prevBits;
    int _drift, _buttons, _triggers, _disconnects;

    public IdleMonitorSettings Settings { get; set; } = settings ?? new IdleMonitorSettings();

    public bool Running => _running;

    /// <param name="nowNs">始めた時刻(これより前のサンプルは見ない)</param>
    /// <param name="buttonsNow">始めた時に押していたボタン(押しっぱなしを勝手な押下と数えない)</param>
    public void Start(ulong nowNs, StickValue leftCenter, StickValue rightCenter, uint buttonsNow = 0)
    {
        Reset();
        _startNs = _lastNs = nowNs;
        _leftCenter = leftCenter;
        _rightCenter = rightCenter;
        _prevBits = buttonsNow;
        _running = true;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _finished = true;
    }

    public void NoteDisconnected(ulong ns) => Note(ns, IdleEventKind.Disconnected);

    public void NoteReconnected(ulong ns) => Note(ns, IdleEventKind.Reconnected);

    void Note(ulong ns, IdleEventKind kind)
    {
        if (!Advance(ns)) return;
        _disconnects++;
        Add(new IdleEvent(Offset(ns), kind, "", 0));
    }

    /// <summary>時間を進める。決めた長さに届いたら止まる。まだ監視しているなら真。</summary>
    public bool Advance(ulong nowNs)
    {
        if (!_running) return false;
        if (nowNs > _lastNs) _lastNs = nowNs;
        if ((_lastNs - _startNs) / 1e9 >= Settings.DurationSeconds)
        {
            _lastNs = _startNs + (ulong)(Settings.DurationSeconds * 1e9);
            _running = false;
            _finished = true;
        }
        return _running;
    }

    public void Feed(DiagSample s)
    {
        if (!_running || s.Ns < _startNs || !Advance(s.Ns)) return;
        ulong offset = Offset(s.Ns);
        CheckStick(offset, "左スティック", Deviation(s.Left, _leftCenter), ref _maxLeft, ref _leftOver);
        CheckStick(offset, "右スティック", Deviation(s.Right, _rightCenter), ref _maxRight, ref _rightOver);
        CheckTrigger(offset, "左トリガー", s.LeftTrigger, ref _ltOver);
        CheckTrigger(offset, "右トリガー", s.RightTrigger, ref _rtOver);
        uint down = s.Buttons & ~_prevBits;
        for (int b = 0; down != 0; b++, down >>= 1)
        {
            if ((down & 1) == 0) continue;
            _buttons++;
            var button = (PadButton)b;
            Add(new IdleEvent(offset, IdleEventKind.PhantomButton, button.ToString(), 1, button));
        }
        _prevBits = s.Buttons;
    }

    // 1 を超えても切らない(真ん中がずれた状態から反対側へ倒すと 1 を超える)
    static float Deviation(StickValue v, StickValue center)
    {
        float dx = v.X - center.X, dy = v.Y - center.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    ulong Offset(ulong ns) => ns > _startNs ? ns - _startNs : 0;

    void CheckStick(ulong offset, string name, float dev, ref float max, ref bool over)
    {
        if (dev > max) max = dev;
        bool now = dev > Settings.StickThreshold;
        if (now && !over)
        {
            _drift++;
            Add(new IdleEvent(offset, IdleEventKind.StickDrift, name, dev));
        }
        over = now;
    }

    void CheckTrigger(ulong offset, string name, float v, ref bool over)
    {
        bool now = v > Settings.TriggerThreshold;
        if (now && !over)
        {
            _triggers++;
            Add(new IdleEvent(offset, IdleEventKind.TriggerWobble, name, v));
        }
        over = now;
    }

    void Add(IdleEvent e)
    {
        if (_events.Count < MaxListed) _events.Add(e);
    }

    public void Reset()
    {
        _events.Clear();
        _running = _finished = false;
        _startNs = _lastNs = 0;
        _maxLeft = _maxRight = 0;
        _leftOver = _rightOver = _ltOver = _rtOver = false;
        _prevBits = 0;
        _drift = _buttons = _triggers = _disconnects = 0;
    }

    /// <summary>今の要約。Events は中の一覧をそのまま見せる(次の Start・Reset で変わる)。</summary>
    public IdleSummary Summary => new(
        (_lastNs - _startNs) / 1e9, _running, _finished, _events.AsReadOnly(),
        _drift, _buttons, _triggers, _disconnects, _maxLeft, _maxRight);
}
