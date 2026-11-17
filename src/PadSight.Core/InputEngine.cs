// Core のまとめ役。SDL のスレッドから InputEvent を順に受け、状態・選択・入力履歴・軌跡・セッションに渡し、
// 不変のスナップショットを作る(仕様書 §2 の処理の流れ)。このクラスは 1 本のスレッドだけから呼ぶ。
// 時刻はすべて SDL のイベントの時刻(ns)で扱う。
namespace PadSight.Core;

public sealed record EngineConfig(
    string DirectionSource, float StickThreshold, IReadOnlyList<string> HistoryButtons,
    float TriggerThreshold, int IdleMinutes, int TrailMs, string SelectionMode, string? PinnedGuid)
{
    public static EngineConfig From(Settings s) => new(
        s.History.DirectionSource, (float)s.History.StickThreshold, s.HistoryButtonKeys,
        (float)s.TriggerThreshold, s.SessionIdleMinutes, s.TrailMs, s.SelectionMode, s.PinnedGuid);
}

public sealed class InputEngine
{
    const ulong DuplicateWindow = 50 * Values.NsPerMs;
    const ulong CheckpointEvery = 60 * Values.NsPerSecond;

    readonly PadStateMachine _state = new();
    readonly PadSelector _selector = new();
    readonly DirectionResolver _direction = new();
    readonly HistoryBuilder _history = new();
    readonly StickTrail _leftTrail = new(), _rightTrail = new();
    readonly SessionTracker _session;
    readonly List<SessionRecord> _finalized = [];
    EngineConfig _config;
    HashSet<string> _historyKeys;
    long _version, _pressCounter;
    ulong _lastPressNs, _lastCheckpointNs;
    uint? _lastPressPad;
    bool _lastPaired, _duplicate;
    int _pairStreak;
    uint? _historyPad;

    public InputEngine(IClock clock, EngineConfig config, Func<Guid>? newId = null)
    {
        _session = new SessionTracker(clock, newId);
        _config = config;
        _historyKeys = [.. config.HistoryButtons];
        Configure(config);
    }

    public EngineConfig Config => _config;
    public uint? SelectedId => _selector.Selected;
    public IReadOnlyDictionary<uint, PadState> Pads => _state.Pads;
    public bool SessionActive => _session.Active;

    public void Configure(EngineConfig c)
    {
        _config = c;
        _historyKeys = [.. c.HistoryButtons];
        _state.TriggerThreshold = c.TriggerThreshold;
        _session.IdleMinutes = c.IdleMinutes;
        bool selChanged = _selector.Mode != c.SelectionMode || _selector.PinnedGuid != c.PinnedGuid;
        _selector.Mode = c.SelectionMode;
        _selector.PinnedGuid = c.PinnedGuid;
        if (selChanged)
            _selector.Reselect(_state.Pads);
        if (c.SelectionMode != "last_used")
            _duplicate = false;
    }

    public void Apply(InputEvent e)
    {
        var r = _state.Apply(e);
        switch (e)
        {
            case PadAdded:
                _selector.Reselect(_state.Pads);
                break;
            case PadRemoved:
                _selector.Reselect(_state.Pads);
                if (_state.Pads.Count == 0)
                    AddFinalized(_session.Close()); // E-2: 全部外れたらその時点で確定
                break;
        }
        if (r.Pad is null || e is PadAdded or PadRemoved)
        {
            UpdateHistory(e.TimestampNs);
            return;
        }
        var pad = r.Pad;
        if (r.Operation)
        {
            AddFinalized(_session.OnOperation(e.TimestampNs));
            pad.LastOperationNs = e.TimestampNs;
            _selector.OnOperation(pad, e.TimestampNs, _state.Pads);
            pad.HasOperated = true;
        }
        if (r.InputKey is not null)
            _session.OnInput(e.TimestampNs, r.InputKey, pad.Info);
        if (r.ButtonPress)
            OnPress(pad.Info.InstanceId, e.TimestampNs);
        if (pad.Info.InstanceId == _selector.Selected && e is AxisChanged ax)
        {
            if (ax.Axis is PadAxis.LeftX or PadAxis.LeftY) _leftTrail.Add(e.TimestampNs, pad.Left.X, pad.Left.Y);
            else if (ax.Axis is PadAxis.RightX or PadAxis.RightY) _rightTrail.Add(e.TimestampNs, pad.Right.X, pad.Right.Y);
        }
        UpdateHistory(e.TimestampNs);
    }

    /// <summary>250 ミリ秒ごとに呼ぶ。区切り・直近 60 秒・current_session の書き出しの時期を見る。</summary>
    public SessionRecord? Tick(ulong nowNs)
    {
        AddFinalized(_session.Tick(nowNs));
        if (_session.Active && nowNs - _lastCheckpointNs >= CheckpointEvery)
        {
            _lastCheckpointNs = nowNs;
            return _session.Checkpoint();
        }
        return null;
    }

    public SessionRecord? Checkpoint() => _session.Checkpoint();

    /// <summary>入力の取り方を変えてつなぎ直すときに、セッションを確定せずにパッドを外す(E-29)。</summary>
    public void DropPad(uint id)
    {
        _state.Apply(new PadRemoved(0, id));
        _selector.Reselect(_state.Pads);
    }

    /// <summary>ホームで選んだパッドに固定する(FR-4)。</summary>
    public void Pin(string guid) => Configure(_config with { SelectionMode = "pinned", PinnedGuid = guid });

    public void ClearHistory() => _history.Clear();

    /// <summary>手で区切る(FR-28)・終了するときに、今のセッションを確定する。</summary>
    public void CloseSession() => AddFinalized(_session.Close());

    public IReadOnlyList<SessionRecord> TakeFinalized()
    {
        if (_finalized.Count == 0)
            return [];
        var list = _finalized.ToList();
        _finalized.Clear();
        return list;
    }

    public PadSnapshot Snapshot(ulong nowNs, long stopwatch, InputStatus status = InputStatus.Running, string? error = null, int? mappingCount = null)
    {
        PadState? sel = _selector.Selected is uint id && _state.Pads.TryGetValue(id, out var s) ? s : null;
        RawState? raw = sel is { Info.IsMapped: false }
            ? new RawState([.. sel.RawButtons], [.. sel.RawAxes.Select(Values.Axis)], [.. sel.RawHats])
            : null;
        return new PadSnapshot(
            ++_version, sel?.Info, sel is not null,
            sel is null ? new HashSet<PadButton>() : new HashSet<PadButton>(sel.Pressed),
            sel?.Left ?? default, sel?.Right ?? default,
            sel?.LeftTrigger ?? 0, sel?.RightTrigger ?? 0,
            [.. _history.Rows], _session.View(), nowNs, stopwatch)
        {
            Pads = [.. _state.Pads.Values.OrderBy(p => p.Order).Select(p => p.Info)],
            Sticks = _state.Pads.ToDictionary(kv => kv.Key, kv => (kv.Value.Left, kv.Value.Right)),
            Live = _state.Pads.ToDictionary(kv => kv.Key, kv => Live(kv.Value)),
            LeftTrail = _leftTrail.Recent(nowNs, _config.TrailMs),
            RightTrail = _rightTrail.Recent(nowNs, _config.TrailMs),
            Raw = raw,
            Status = status,
            Error = error,
            PressCounter = _pressCounter,
            LastPressNs = _lastPressNs,
            DuplicateWarning = _duplicate,
            MappingCount = mappingCount,
        };
    }

    static PadLive Live(PadState p) => new(p.Info, new HashSet<PadButton>(p.Pressed), p.Left, p.Right, p.LeftTrigger, p.RightTrigger,
        p.Info.IsMapped ? null : new RawState([.. p.RawButtons], [.. p.RawAxes.Select(Values.Axis)], [.. p.RawHats]));

    void UpdateHistory(ulong ns)
    {
        if (_selector.Selected is not uint id || !_state.Pads.TryGetValue(id, out var p))
            return;
        if (_historyPad != id)
        {
            _historyPad = id;
            _direction.Reset();
        }
        int dir;
        List<string> keys;
        if (p.Info.IsMapped)
        {
            dir = _direction.Resolve(p.Pressed, p.Left, _config.DirectionSource, _config.StickThreshold);
            keys = [.. p.Pressed.Select(b => b.ToString()).Where(_historyKeys.Contains)];
            if (p.LeftTriggerDown && _historyKeys.Contains("LeftTrigger")) keys.Add("LeftTrigger");
            if (p.RightTriggerDown && _historyKeys.Contains("RightTrigger")) keys.Add("RightTrigger");
            keys.Sort(StringComparer.Ordinal);
        }
        else
        {
            dir = p.RawHats.Length > 0 ? DirectionResolver.FromHat(p.RawHats[0]) : 5;
            keys = [.. Enumerable.Range(0, p.RawButtons.Length).Where(i => p.RawButtons[i]).Select(Values.RawKey)];
        }
        _history.Update(ns, dir, keys);
    }

    void OnPress(uint pad, ulong ns)
    {
        _pressCounter++;
        // E-4: 別のパッドの押下が 50 ミリ秒以内にそろうことが 5 回続いたら知らせる
        if (_lastPressPad is uint prev && prev != pad && ns - _lastPressNs <= DuplicateWindow && !_lastPaired)
        {
            _lastPaired = true;
            if (++_pairStreak >= 5 && _config.SelectionMode == "last_used")
                _duplicate = true;
        }
        else
        {
            if (!_lastPaired && _lastPressPad is not null)
                _pairStreak = 0;
            _lastPaired = false;
        }
        _lastPressPad = pad;
        _lastPressNs = ns;
    }

    void AddFinalized(SessionRecord? r)
    {
        if (r is not null)
            _finalized.Add(r);
    }
}
