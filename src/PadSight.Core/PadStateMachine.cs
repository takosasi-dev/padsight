// イベントを順に受け、パッドごとの今の状態(押しているボタン、軸、割り当ての無いパッドの生の値)を持つ(仕様書 §2)。
// あわせて、その出来事が「操作」か、「入力の回数」に数える物か(§9.6.1)を返す。
namespace PadSight.Core;

public sealed class PadState(PadInfo info, int order)
{
    public PadInfo Info { get; } = info;
    public int Order { get; } = order;
    public HashSet<PadButton> Pressed { get; } = [];
    public short[] Axes { get; } = new short[6];
    public bool[] RawButtons { get; } = new bool[info.RawButtons];
    public short[] RawAxes { get; } = new short[info.RawAxes];
    public byte[] RawHats { get; } = new byte[info.RawHats];
    public ulong LastOperationNs { get; set; }
    public bool HasOperated { get; set; }
    public bool LeftTriggerDown { get; set; }
    public bool RightTriggerDown { get; set; }
    public bool LeftStickOut { get; set; }
    public bool RightStickOut { get; set; }

    public StickValue Left => StickValue.FromRaw(Axes[(int)PadAxis.LeftX], Axes[(int)PadAxis.LeftY]);
    public StickValue Right => StickValue.FromRaw(Axes[(int)PadAxis.RightX], Axes[(int)PadAxis.RightY]);
    public float LeftTrigger => Values.Trigger(Axes[(int)PadAxis.LeftTrigger]);
    public float RightTrigger => Values.Trigger(Axes[(int)PadAxis.RightTrigger]);

    // スナップショットに渡す写し。PadStateMachine.Apply がこのパッドを変えるたびに捨てる(渡した写しは変えない)
    IReadOnlySet<PadButton>? _pressedCopy;
    PadLive? _live;

    /// <summary>押しているボタンの写し。ボタンが変わるまで同じ物を返す。</summary>
    internal IReadOnlySet<PadButton> PressedCopy() => _pressedCopy ??= new HashSet<PadButton>(Pressed);

    /// <summary>このパッドの今の状態の写し。変わるまで同じ物を返す。</summary>
    internal PadLive Live() => _live ??= new(Info, PressedCopy(), Left, Right, LeftTrigger, RightTrigger,
        Info.IsMapped ? null : new RawState([.. RawButtons], [.. RawAxes.Select(Values.Axis)], [.. RawHats]));

    internal void Changed(bool buttons)
    {
        _live = null;
        if (buttons)
            _pressedCopy = null;
    }
}

/// <param name="Operation">セッションを続ける操作か</param>
/// <param name="InputKey">入力の回数に数える鍵(数えないなら null)</param>
/// <param name="ButtonPress">ボタンの押下か(FR-7 の数え方)</param>
public readonly record struct ApplyResult(PadState? Pad, bool Operation, string? InputKey, bool ButtonPress);

public sealed class PadStateMachine
{
    const float StickOperation = 0.25f;
    readonly Dictionary<uint, PadState> _pads = [];
    int _order;
    // スナップショットに渡す写し。変わったときだけ作り直す
    IReadOnlyList<PadInfo>? _infos;
    IReadOnlyDictionary<uint, PadLive>? _live;
    IReadOnlyDictionary<uint, (StickValue Left, StickValue Right)>? _sticks;

    public IReadOnlyDictionary<uint, PadState> Pads => _pads;

    public float TriggerThreshold { get; set; } = 0.5f;

    /// <summary>つながっているパッドの情報(つないだ順)。つなぐ・外すまで同じ物を返す。</summary>
    public IReadOnlyList<PadInfo> Infos() => _infos ??= [.. _pads.Values.OrderBy(p => p.Order).Select(p => p.Info)];

    /// <summary>全部のパッドの今の状態。鍵は InstanceId。どれかが変わるまで同じ物を返す。</summary>
    public IReadOnlyDictionary<uint, PadLive> Live()
    {
        if (_live is null)
        {
            var d = new Dictionary<uint, PadLive>(_pads.Count);
            foreach (var (id, p) in _pads)
                d[id] = p.Live();
            _live = d;
        }
        return _live;
    }

    /// <summary>全部のパッドの左右のスティック。鍵は InstanceId。軸が変わるまで同じ物を返す。</summary>
    public IReadOnlyDictionary<uint, (StickValue Left, StickValue Right)> Sticks()
    {
        if (_sticks is null)
        {
            var d = new Dictionary<uint, (StickValue, StickValue)>(_pads.Count);
            foreach (var (id, p) in _pads)
                d[id] = (p.Left, p.Right);
            _sticks = d;
        }
        return _sticks;
    }

    public ApplyResult Apply(InputEvent e)
    {
        if (e is PadAdded or PadRemoved)
            (_infos, _live, _sticks) = (null, null, null);
        switch (e)
        {
            case PadAdded a:
                var st = new PadState(a.Info, _order++);
                _pads[a.InstanceId] = st;
                return new(st, false, null, false);
            case PadRemoved:
                _pads.Remove(e.InstanceId, out var removed);
                return new(removed, false, null, false);
        }
        if (!_pads.TryGetValue(e.InstanceId, out var p))
            return default;
        p.Changed(e is ButtonChanged);
        _live = null;
        if (e is AxisChanged)
            _sticks = null;
        switch (e)
        {
            case ButtonChanged b:
                bool wasDown = p.Pressed.Contains(b.Button);
                if (b.Down) p.Pressed.Add(b.Button); else p.Pressed.Remove(b.Button);
                return b.Down && !wasDown ? new(p, true, b.Button.ToString(), true) : new(p, false, null, false);
            case AxisChanged ax:
                p.Axes[(int)ax.Axis] = ax.Raw;
                if (ax.Axis is PadAxis.LeftTrigger or PadAxis.RightTrigger)
                {
                    bool left = ax.Axis == PadAxis.LeftTrigger;
                    bool down = Values.Trigger(ax.Raw) >= TriggerThreshold;
                    bool was = left ? p.LeftTriggerDown : p.RightTriggerDown;
                    if (left) p.LeftTriggerDown = down; else p.RightTriggerDown = down;
                    return down && !was ? new(p, true, Values.TriggerKey(ax.Axis), false) : new(p, false, null, false);
                }
                else
                {
                    bool left = ax.Axis is PadAxis.LeftX or PadAxis.LeftY;
                    bool outNow = (left ? p.Left : p.Right).Magnitude >= StickOperation;
                    bool was = left ? p.LeftStickOut : p.RightStickOut;
                    if (left) p.LeftStickOut = outNow; else p.RightStickOut = outNow;
                    return new(p, outNow && !was, null, false);
                }
            case RawButtonChanged rb when rb.Index < p.RawButtons.Length:
                bool rWas = p.RawButtons[rb.Index];
                p.RawButtons[rb.Index] = rb.Down;
                return rb.Down && !rWas ? new(p, true, Values.RawKey(rb.Index), true) : new(p, false, null, false);
            case RawAxisChanged ra when ra.Index < p.RawAxes.Length:
                p.RawAxes[ra.Index] = ra.Raw;
                return new(p, false, null, false);
            case RawHatChanged rh when rh.Index < p.RawHats.Length:
                bool changed = p.RawHats[rh.Index] != rh.Value;
                p.RawHats[rh.Index] = rh.Value;
                return new(p, changed && rh.Value != 0, null, false);
        }
        return new(p, false, null, false);
    }
}
