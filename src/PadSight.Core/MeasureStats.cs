// 計測のタブの計算(仕様書 FR-53・§9.19)。連打テストは押下の時刻の列から、反応速度テストは各回のミリ秒から結果を出す。
// 押下の拾い方(スナップショットの間に何回押されたか)は画面の側。ここは受け取った値を数えるだけで、結果は保存しない。
namespace PadSight.Core;

/// <param name="PerSecond">回数 ÷ テストの長さ(秒)</param>
/// <param name="Max1s">どこでもよい 1 秒の間([t, t+1 秒))に入った押下の数の最大</param>
/// <param name="MeanIntervalMs">押した間隔の平均。押下が 2 回未満なら null</param>
/// <param name="StdevIntervalMs">押した間隔の標準偏差(母集団の式、n で割る)。押下が 2 回未満なら null</param>
public sealed record RapidResult(int Count, double PerSecond, int Max1s, double? MeanIntervalMs, double? StdevIntervalMs);

public sealed record ReactionResult(double AverageMs, double BestMs);

public static class RapidStats
{
    public static RapidResult From(IReadOnlyList<ulong> pressNs, double seconds)
    {
        var t = pressNs.OrderBy(x => x).ToArray();
        int max = 0;
        for (int i = 0, j = 0; j < t.Length; j++)
        {
            while (t[j] - t[i] >= Values.NsPerSecond) i++;
            max = Math.Max(max, j - i + 1);
        }
        double? mean = null, sd = null;
        if (t.Length >= 2)
        {
            var gaps = new double[t.Length - 1];
            for (int k = 1; k < t.Length; k++) gaps[k - 1] = (t[k] - t[k - 1]) / (double)Values.NsPerMs;
            double m = gaps.Average();
            mean = m;
            sd = Math.Sqrt(gaps.Sum(g => (g - m) * (g - m)) / gaps.Length);
        }
        return new RapidResult(t.Length, seconds > 0 ? t.Length / seconds : 0, max, mean, sd);
    }

    /// <summary>
    /// 2 つのスナップショットの間に k 回押されたとき、分かるのは最後の押下の時刻だけなので、前に拾った押下の時刻(無ければテストの始まり)から
    /// 最後の押下までを k 等分して並べる(最後は本当の時刻)。k が 1 なら本当の時刻 1 つ。間隔の平均とばらつきは、この分だけ近似になる。
    /// </summary>
    public static IEnumerable<ulong> Spread(ulong previousNs, ulong lastNs, long k)
    {
        if (lastNs < previousNs) previousNs = lastNs;
        for (long i = 1; i <= k; i++)
            yield return i == k ? lastNs : previousNs + (ulong)((double)(lastNs - previousNs) * i / k);
    }
}

public static class ReactionStats
{
    /// <summary>各回のミリ秒から平均と最良(一番短い物)。1 回も無ければ null。</summary>
    public static ReactionResult? From(IReadOnlyList<double> ms) =>
        ms.Count == 0 ? null : new ReactionResult(ms.Average(), ms.Min());
}
