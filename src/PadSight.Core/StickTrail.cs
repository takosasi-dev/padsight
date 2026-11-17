// スティックの軌跡を、固定の長さ(512 点)の輪のバッファで持つ(仕様書 FR-22・D-9)。
// 取り出すときは、直近 trail_ms の点だけを古い順に返す。
namespace PadSight.Core;

public sealed class StickTrail
{
    public const int Capacity = 512;
    readonly TrailPoint[] _buf = new TrailPoint[Capacity];
    int _next, _count;

    public void Add(ulong ns, float x, float y)
    {
        _buf[_next] = new TrailPoint(ns, x, y);
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
            _count++;
    }

    public IReadOnlyList<TrailPoint> Recent(ulong nowNs, int trailMs)
    {
        if (trailMs <= 0 || _count == 0)
            return [];
        ulong span = (ulong)trailMs * Values.NsPerMs;
        // 先に数えてから、ちょうどの長さの配列に写す(List を伸ばしながら作ると、捨てる配列が何本も出る)。
        // 時刻が戻った点もあるので、範囲は連続とみなさず 1 点ずつ見る
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            if (InWindow(_buf[(_next - _count + i + Capacity) % Capacity].Ns, nowNs, span))
                n++;
        }
        if (n == 0)
            return [];
        var arr = new TrailPoint[n];
        for (int i = 0, j = 0; i < _count; i++)
        {
            var p = _buf[(_next - _count + i + Capacity) % Capacity];
            if (InWindow(p.Ns, nowNs, span))
                arr[j++] = p;
        }
        return arr;
    }

    static bool InWindow(ulong ns, ulong nowNs, ulong span) => nowNs < ns || nowNs - ns <= span;
}
