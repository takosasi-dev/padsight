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
        var list = new List<TrailPoint>();
        for (int i = 0; i < _count; i++)
        {
            var p = _buf[(_next - _count + i + Capacity) % Capacity];
            if (nowNs < p.Ns || nowNs - p.Ns <= span)
                list.Add(p);
        }
        return list;
    }
}
