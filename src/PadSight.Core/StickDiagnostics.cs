// スティック診断の計算(仕様書 §9.9)。手を離したときのずれと、外周の形。
// 集めた値を受けて結果を返すだけで、良い・悪いの判定はしない。結果は保存しない(FR-23)。
namespace PadSight.Core;

public sealed record DriftResult(float MeanX, float MeanY, float MeanMagnitude, float MaxMagnitude, double RawMeanX, double RawMeanY);

/// <param name="BinMax">5 度ずつ 72 の区切りの倒し具合の最大。値の入らない区切りは NaN</param>
public sealed record OuterResult(float[] BinMax, int EmptyBins, bool Insufficient, float Mean, float Min, float Max, float DeviationPercent);

public static class StickDiagnostics
{
    public const int Bins = 72;

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
