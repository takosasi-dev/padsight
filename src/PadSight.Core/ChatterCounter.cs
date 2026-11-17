// ボタンの検査(仕様書 FR-45・§9.18)。パッドとボタンごとに、押した回数と、離してから 15 ミリ秒未満でまた押された回数を数える。
// 時刻は SDL のイベントの時刻(ns)。数はメモリだけに持ち、保存も記録もしない(INV-8)。InputEngine と同じ 1 本のスレッドから呼ぶ。
namespace PadSight.Core;

public readonly record struct ChatterCount(int Presses, int Short);

public sealed class ChatterCounter
{
    public const ulong ShortNs = 15 * Values.NsPerMs;

    sealed class Entry { public int Presses, Short; public bool Down; public ulong? ReleasedNs; }

    readonly Dictionary<(uint Pad, PadButton Button), Entry> _entries = [];
    IReadOnlyDictionary<uint, IReadOnlyDictionary<PadButton, ChatterCount>>? _copy;

    /// <summary>押した・離したを渡す。同じ向きが続いた物は数えない。</summary>
    public void Apply(uint pad, PadButton button, bool down, ulong ns)
    {
        if (!_entries.TryGetValue((pad, button), out var e))
            _entries[(pad, button)] = e = new Entry();
        if (e.Down == down)
            return;
        e.Down = down;
        if (!down)
        {
            e.ReleasedNs = ns;
        }
        else
        {
            e.Presses++;
            if (e.ReleasedNs is ulong r && ns - r < ShortNs) // 時刻が戻ったときは ulong で大きくなり、数えない
                e.Short++;
        }
        _copy = null;
    }

    public void Reset()
    {
        _entries.Clear();
        _copy = null;
    }

    /// <summary>画面に渡す写し。変わっていなければ前の写しを返す(写しは変えない)。</summary>
    public IReadOnlyDictionary<uint, IReadOnlyDictionary<PadButton, ChatterCount>> Snapshot() =>
        _copy ??= _entries.Where(kv => kv.Value.Presses > 0)
            .GroupBy(kv => kv.Key.Pad)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<PadButton, ChatterCount>)g.ToDictionary(
                kv => kv.Key.Button, kv => new ChatterCount(kv.Value.Presses, kv.Value.Short)));
}
