// スティック診断の計算(仕様書 §9.9・§9.18)。手を離したときのずれ、外周の形、トリガーの範囲。
// 集めた値を受けて結果を返すだけ。良い・悪いを言うのはトリガーの範囲だけ(FR-46)。結果は保存しない(FR-23)。
namespace PadSight.Core;

public sealed record DriftResult(float MeanX, float MeanY, float MeanMagnitude, float MaxMagnitude, double RawMeanX, double RawMeanY);

/// <param name="BinMax">5 度ずつ 72 の区切りの倒し具合の最大。値の入らない区切りは NaN</param>
public sealed record OuterResult(float[] BinMax, int EmptyBins, bool Insufficient, float Mean, float Min, float Max, float DeviationPercent);

/// <summary>最小と最大は 0〜1。Verdict は「問題なし」「戻りきっていません」「奥まで届いていません」「引き方が足りません」。</summary>
public sealed record TriggerRangeResult(float Min, float Max, string Verdict);

public static class StickDiagnostics
{
    public const int Bins = 72;

    /// <summary>描画のたびのトリガーの値(0〜1)から最小と最大と判定を出す。判定はこの順に見る(§9.18)。</summary>
    public static TriggerRangeResult TriggerRange(IReadOnlyList<float> samples)
    {
        float min = samples.Count == 0 ? 0f : samples.Min(), max = samples.Count == 0 ? 0f : samples.Max();
        string verdict = max - min < 0.5f ? "引き方が足りません"
            : min > 0.02f ? "戻りきっていません"
            : max < 0.98f ? "奥まで届いていません"
            : "問題なし";
        return new TriggerRangeResult(min, max, verdict);
    }

    public static DriftResult Drift(IReadOnlyList<StickValue> samples)
    {
        if (samples.Count == 0)
            return new DriftResult(0, 0, 0, 0, 0, 0);
        float mx = samples.Average(s => s.X), my = samples.Average(s => s.Y);
        return new DriftResult(mx, my, MathF.Min(1f, MathF.Sqrt(mx * mx + my * my)), samples.Max(s => s.Magnitude),
            samples.Average(s => (double)s.RawX), samples.Average(s => (double)s.RawY));
    }

    public static OuterResult Outer(IReadOnlyList<StickValue> samples)
    {
        var bins = Enumerable.Repeat(float.NaN, Bins).ToArray();
        foreach (var s in samples)
        {
            float mag = s.Magnitude;
            if (mag <= 0f)
                continue;
            int i = (int)(s.AngleDeg / 5f) % Bins;
            if (float.IsNaN(bins[i]) || mag > bins[i])
                bins[i] = mag;
        }
        var filled = bins.Where(b => !float.IsNaN(b)).ToList();
        int empty = Bins - filled.Count;
        if (empty >= 8)
            return new OuterResult(bins, empty, true, 0, 0, 0, 0);
        return new OuterResult(bins, empty, false, filled.Average(), filled.Min(), filled.Max(),
            filled.Average(b => MathF.Abs(1f - b)) * 100f);
    }
}
