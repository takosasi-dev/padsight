// トリガーの時系列(仕様書 FR-57。PadLab の TriggerTracker を移した物)。1 本のトリガーの値を時刻つきで輪のバッファに持ち、最大・今の値を出す。
// 描くときは、終わりを「今」にした窓を決まった数の列に分け、列ごとの最大を返す(値の変わらない間はイベントが来ないので、前の値を持ち越す)。
// メモリだけで持ち、保存しない(INV-8)。
namespace PadSight.Core.Diag;

public sealed class TriggerTrace(bool right, int capacity = 8192)
{
    readonly (ulong Ns, float Value)[] _ring = new (ulong, float)[capacity];
    int _start, _count;

    public float Max { get; private set; }
    public float Current { get; private set; }

    /// <summary>積んでいる値の数(History と違って写しを作らない)。</summary>
    public int Count => _count;

    /// <summary>古い順の値。値が前と同じイベントは積まない(ほかの軸の動きで来たイベント)。</summary>
    public IReadOnlyList<(ulong Ns, float Value)> History
    {
        get
        {
            var list = new (ulong, float)[_count];
            for (int i = 0; i < _count; i++)
                list[i] = _ring[(_start + i) % _ring.Length];
            return list;
        }
    }

    public void Feed(DiagSample s) => Add(s.Ns, right ? s.RightTrigger : s.LeftTrigger);

    public void Add(ulong ns, float v)
    {
        Current = v;
        if (v > Max) Max = v;
        if (_count > 0)
        {
            var last = _ring[(_start + _count - 1) % _ring.Length];
            if (last.Value == v)
                return;
            ns = Math.Max(ns, last.Ns); // 時刻は戻さない(描く側が古い順を前提にする)
        }
        if (_count == _ring.Length)
        {
            _start = (_start + 1) % _ring.Length;
            _count--;
        }
        _ring[(_start + _count) % _ring.Length] = (ns, v);
        _count++;
    }

    public void Reset()
    {
        _start = _count = 0;
        Max = Current = 0;
    }

    /// <summary>
    /// 終わりを nowNs にした windowNs の窓を into.Length の列に分け、列ごとの値(列の中の最大。中にイベントが無ければ前の値を持ち越す)を入れる。
    /// 最初の値より前の列は NaN。
    /// </summary>
    public void Resample(ulong nowNs, ulong windowNs, float[] into)
    {
        int n = into.Length;
        if (n == 0) return;
        long start = (long)nowNs - (long)windowNs;
        double step = (double)windowNs / n;
        float held = float.NaN;
        int k = 0;
        // 窓より前の最後の値を持ち越しの始まりにする
        while (k < _count && (long)_ring[(_start + k) % _ring.Length].Ns < start)
        {
            held = _ring[(_start + k) % _ring.Length].Value;
            k++;
        }
        for (int col = 0; col < n; col++)
        {
            long colEnd = col == n - 1 ? long.MaxValue : start + (long)((col + 1) * step); // 今より新しい時刻の値も最後の列に入れる
            float v = held;
            while (k < _count && (long)_ring[(_start + k) % _ring.Length].Ns < colEnd)
            {
                float x = _ring[(_start + k) % _ring.Length].Value;
                v = float.IsNaN(v) ? x : MathF.Max(v, x);
                held = x;
                k++;
            }
            into[col] = v;
        }
    }
}
