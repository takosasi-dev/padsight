// 報告の間隔の参考値(仕様書 §9.20・FR-54。PadLab の PollingRateMeter を DiagSample の時刻で受ける形に移した)。直近 1 秒の時刻の間隔から出す。
// DiagSample は値が変わったイベントごとにしか来ないので、同じ報告から来た分(50 µs 以内)は 1 つにまとめ、動きが止まった間の長い間隔に引っ張られないよう Hz は間隔の中央値から出す。
// 出すのは SDL が受け取った間隔で、パッドの実際の報告の間隔と同じとは限らない。メモリだけに持ち、保存しない(INV-8)。
namespace PadSight.Core.Diag;

/// <param name="Hz">1000 / 間隔の中央値。測れていなければ 0</param>
/// <param name="JitterMs">間隔のばらつき(標準偏差)</param>
/// <param name="Samples">窓の中の時刻の数(同じ報告はまとめた後)</param>
public sealed record PollingStats(double Hz, double MeanIntervalMs, double MedianIntervalMs, double MaxIntervalMs, double JitterMs, int Samples)
{
    /// <summary>参考値として出せるだけの間隔があるか(ボタンを 1 回押しただけの間隔を「Hz」と見せないように)。</summary>
    public bool Ready => Samples > PollingRate.MinIntervals;
}

public sealed class PollingRate(ulong windowNs = 1_000_000_000)
{
    /// <summary>これより近い時刻は同じ報告から来たイベントとみなす(8000 Hz のパッドの間隔 125 µs より短く取る)。</summary>
    public const ulong SameReportNs = 50_000;

    /// <summary>参考値を出すのに要る間隔の数。</summary>
    public const int MinIntervals = 20;

    readonly Queue<ulong> _stamps = new();
    ulong _last;
    PollingStats? _stats;

    public void Feed(ulong ns)
    {
        if (_stamps.Count > 0)
        {
            if (ns < _last)
                _stamps.Clear(); // 時刻が戻った(デモとの切り替えなど)ので数え直す
            else if (ns - _last < SameReportNs)
                return;
        }
        _stamps.Enqueue(ns);
        _last = ns;
        while (ns - _stamps.Peek() > windowNs)
            _stamps.Dequeue();
        _stats = null;
    }

    public void Reset()
    {
        _stamps.Clear();
        _stats = null;
    }

    /// <summary>計算は Feed の後の最初の 1 回だけ(描画のたびに呼んでよい)。</summary>
    public PollingStats Stats => _stats ??= Compute([.. _stamps]);

    static PollingStats Compute(ulong[] t)
    {
        if (t.Length < 2)
            return new PollingStats(0, 0, 0, 0, 0, t.Length);
        int n = t.Length - 1;
        var d = new double[n];
        double sum = 0, max = 0;
        for (int i = 0; i < n; i++)
        {
            d[i] = (t[i + 1] - t[i]) / 1e6;
            sum += d[i];
            max = Math.Max(max, d[i]);
        }
        double mean = sum / n, var = 0;
        foreach (var x in d)
            var += (x - mean) * (x - mean);
        Array.Sort(d);
        double median = n % 2 == 1 ? d[n / 2] : (d[n / 2 - 1] + d[n / 2]) / 2;
        return new PollingStats(median > 0 ? 1000.0 / median : 0, mean, median, max, Math.Sqrt(var / n), t.Length);
    }
}
