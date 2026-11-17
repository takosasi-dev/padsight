// セッションの始まりと終わり、入力の回数、直近 60 秒の数と 60 秒の最大を数える(仕様書 §9.6・D-12)。
// 長さは SDL のイベントの時刻の差で測り、日時は始まった時点の時計から出す(E-17)。
// 保存するのは集計(SessionRecord)だけで、押した順番と時刻は持ち出さない(INV-8)。
namespace PadSight.Core;

public sealed record SessionRecord(
    Guid Id, DateTime StartUtc, DateTime EndUtc, long DurationSeconds,
    string PadKind, string PadName, long Inputs, int PeakPerMinute,
    IReadOnlyDictionary<string, long> Buttons,
    DateTime? LastOperationUtc = null)
{
    /// <summary>1 秒の最大(FR-37)。</summary>
    public int PeakPerSecond { get; init; }

    /// <summary>パッドの GUID ごとの回数(FR-38)。sessions.jsonl には書かず、pads.json に足すために使う。</summary>
    public IReadOnlyDictionary<string, PadTally> PerPad { get; init; } = new Dictionary<string, PadTally>();
}

/// <summary>1 台のパッドの、1 セッション分の回数。</summary>
public sealed record PadTally(string Name, string Kind, long Inputs, IReadOnlyDictionary<string, long> Buttons);

public sealed class SessionTracker
{
    const ulong Window60 = 60 * Values.NsPerSecond;
    const ulong Max24h = 24UL * 3600 * Values.NsPerSecond;

    readonly IClock _clock;
    readonly Func<Guid> _newId;

    bool _active;
    Guid _id;
    ulong _startNs, _lastOpNs;
    DateTime _startUtc;
    long _inputs;
    int _peak, _last60;
    readonly Dictionary<string, long> _buttons = [];
    readonly Dictionary<uint, (PadInfo Info, long Count)> _perPad = [];
    readonly Queue<ulong> _recent = new();
    readonly Queue<ulong> _recent1 = new();
    int _peak1;
    readonly Dictionary<string, (PadInfo Info, long Inputs, Dictionary<string, long> Buttons)> _perGuid = [];
    long? _prevDuration, _prevInputs;
    // 画面に渡す写し。上位 5 つはボタンの回数が変わったときだけ並べ直す
    IReadOnlyList<KeyValuePair<string, long>>? _top5;
    SessionView? _view;

    public SessionTracker(IClock clock, Func<Guid>? newId = null)
    {
        _clock = clock;
        _newId = newId ?? Guid.NewGuid;
    }

    public int IdleMinutes { get; set; } = 10;
    public bool Active => _active;
    public ulong LastOperationNs => _lastOpNs;

    /// <summary>操作(§9.6.1)を受ける。前のセッションが区切りを過ぎていれば確定して返す。</summary>
    public SessionRecord? OnOperation(ulong ns)
    {
        SessionRecord? closed = null;
        if (_active && (Values.Since(ns, _lastOpNs) >= IdleNs || Values.Since(ns, _startNs) >= Max24h))
            closed = Close();
        if (!_active)
        {
            _active = true;
            _id = _newId();
            _startNs = ns;
            _startUtc = ToUtc(ns);
            _inputs = 0;
            _peak = 0;
            _last60 = 0;
            _buttons.Clear();
            _top5 = null;
            _perPad.Clear();
            _recent.Clear();
            _recent1.Clear();
            _peak1 = 0;
            _perGuid.Clear();
        }
        _lastOpNs = Math.Max(_lastOpNs, ns);
        return closed;
    }

    /// <summary>入力の回数に数える物(ボタンの押下とトリガーの押した始まり)を受ける。先に OnOperation を呼ぶ。</summary>
    public void OnInput(ulong ns, string key, PadInfo? pad)
    {
        if (!_active)
            return;
        _inputs++;
        _buttons[key] = _buttons.GetValueOrDefault(key) + 1;
        _top5 = null;
        if (pad is not null)
            _perPad[pad.InstanceId] = (pad, (_perPad.TryGetValue(pad.InstanceId, out var p) ? p.Count : 0) + 1);
        if (pad is not null)
        {
            if (!_perGuid.TryGetValue(pad.Guid, out var g))
                g = (pad, 0, new Dictionary<string, long>());
            g.Buttons[key] = g.Buttons.GetValueOrDefault(key) + 1;
            _perGuid[pad.Guid] = (g.Info, g.Inputs + 1, g.Buttons);
        }
        // FR-37: この入力から 1 秒前までの数の最大
        _recent1.Enqueue(ns);
        while (Values.Since(ns, _recent1.Peek()) >= Values.NsPerSecond)
            _recent1.Dequeue();
        _peak1 = Math.Max(_peak1, _recent1.Count);
        _recent.Enqueue(ns);
        Recount(ns);
    }

    /// <summary>250 ミリ秒ごとに呼ぶ。区切りを過ぎていれば確定して返す。</summary>
    public SessionRecord? Tick(ulong nowNs)
    {
        if (!_active)
            return null;
        if (Values.Since(nowNs, _lastOpNs) >= IdleNs || Values.Since(nowNs, _startNs) >= Max24h)
            return Close();
        Recount(nowNs);
        return null;
    }

    /// <summary>パッドが全部外れた・終了・手で区切る(FR-28)ときに、その時点で確定する。</summary>
    public SessionRecord? Close()
    {
        if (!_active)
            return null;
        _active = false;
        var rec = Build(null);
        _prevDuration = rec.DurationSeconds;
        _prevInputs = rec.Inputs;
        // 操作が 1 回だけ(長さ 0 秒)のセッションは保存しない
        return rec.DurationSeconds > 0 ? rec : null;
    }

    /// <summary>進行中のセッションの写し(current_session.json 用)。</summary>
    public SessionRecord? Checkpoint() => _active ? Build(ToUtc(_lastOpNs)) : null;

    /// <summary>統計のパネルに出す値。どの値も変わっていなければ前と同じ物を返す。</summary>
    public SessionView View()
    {
        _top5 ??= _buttons.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(5).ToList();
        if (_view is { } v && v.Active == _active && v.StartNs == _startNs && v.Inputs == _inputs && v.Last60 == _last60
            && v.Peak == _peak && v.Peak1s == _peak1 && v.PreviousDurationSeconds == _prevDuration && v.PreviousInputs == _prevInputs
            && ReferenceEquals(v.Top5, _top5))
            return v;
        return _view = new(_active, _startNs, _inputs, _last60, _peak, _top5, _prevDuration, _prevInputs) { Peak1s = _peak1 };
    }

    ulong IdleNs => (ulong)IdleMinutes * 60 * Values.NsPerSecond;

    void Recount(ulong nowNs)
    {
        while (_recent.Count > 0 && Values.Since(nowNs, _recent.Peek()) >= Window60)
            _recent.Dequeue();
        _last60 = _recent.Count;
        _peak = Math.Max(_peak, _last60);
    }

    DateTime ToUtc(ulong ns)
    {
        ulong now = _clock.MonotonicNs;
        double deltaMs = now >= ns ? (now - ns) / 1e6 : -((ns - now) / 1e6);
        return _clock.UtcNow.AddMilliseconds(-deltaMs);
    }

    SessionRecord Build(DateTime? lastOpUtc)
    {
        long seconds = (long)((_lastOpNs - _startNs) / Values.NsPerSecond);
        var pad = _perPad.Values.OrderByDescending(p => p.Count).Select(p => p.Info).FirstOrDefault();
        var endUtc = _startUtc.AddTicks((long)((_lastOpNs - _startNs) / 100));
        return new SessionRecord(_id, _startUtc, endUtc, seconds,
            pad?.Kind.ToString() ?? "", pad?.Name ?? "", _inputs, _peak,
            _buttons.Where(kv => kv.Value > 0).ToDictionary(), lastOpUtc)
        {
            PeakPerSecond = _peak1,
            PerPad = _perGuid.ToDictionary(kv => kv.Key, kv => new PadTally(kv.Value.Info.Name, kv.Value.Info.Kind.ToString(), kv.Value.Inputs, new Dictionary<string, long>(kv.Value.Buttons))),
        };
    }
}
