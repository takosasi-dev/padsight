// スティック診断の計算(仕様書 §9.9・§9.18・FR-56)。手を離したときのずれ、外周の形、トリガーの範囲。
// 集めた値を受けて結果を返すだけ。ずれには PadLab の DriftAnalyzer の目安(0.03・0.08)、外周には CircleTest の真円度と左右・上下の差を足した。
namespace PadSight.Core;

/// <param name="Noise">平均の位置からの距離の標準偏差(揺れ。PadLab の DeviceSession.Noise と同じ求め方)</param>
public sealed record DriftResult(float MeanX, float MeanY, float MeanMagnitude, float MaxMagnitude, double RawMeanX, double RawMeanY, float Noise = 0)
{
    /// <summary>PadLab の DriftThresholds の既定(軽いずれ・大きなずれ)。</summary>
    public const float MinorDistance = 0.03f, NotableDistance = 0.08f;

    /// <summary>0 = 目安の範囲内、1 = 軽いずれ、2 = 大きなずれ(最大の倒し具合で見る)。</summary>
    public int Tier => MaxMagnitude >= NotableDistance ? 2 : MaxMagnitude >= MinorDistance ? 1 : 0;

    /// <summary>判定の文(断定しない)。</summary>
    public string Verdict => Tier switch
    {
        2 => "目安(0.080)より大きなずれがある可能性があります",
        1 => "軽いずれがある可能性があります(目安 0.030〜0.080)",
        _ => "ずれは目安(0.030)より小さい範囲です",
    };

    /// <summary>平均のずれの向き(0〜360 度。右が 0、反時計回り)。</summary>
    public float DirectionDeg => Values.Angle(MeanX, MeanY);
}

/// <param name="BinMax">5 度ずつ 72 の区切りの倒し具合の最大。値の入らない区切りは NaN</param>
/// <param name="Roundness">最小 ÷ 最大(1 に近いほど丸い。PadLab の CircleTest と同じ)</param>
/// <param name="AsymmetryX">(右の端 − 左の端) ÷ 平均。正なら右が大きい</param>
/// <param name="AsymmetryY">(上の端 − 下の端) ÷ 平均。正なら上が大きい</param>
public sealed record OuterResult(float[] BinMax, int EmptyBins, bool Insufficient, float Mean, float Min, float Max, float DeviationPercent,
    float Roundness = 0, float AsymmetryX = 0, float AsymmetryY = 0)
{
    /// <summary>記録とレポートに描く形(72 個、0〜1)。値の入らない区切りは 0。</summary>
    public IReadOnlyList<double> Shape => [.. BinMax.Select(b => float.IsNaN(b) ? 0.0 : Math.Round(b, 4))];
}

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
        var dist = samples.Select(s => (double)MathF.Sqrt((s.X - mx) * (s.X - mx) + (s.Y - my) * (s.Y - my))).ToList();
        double dm = dist.Average();
        float noise = (float)Math.Sqrt(Math.Max(0, dist.Average(d => d * d) - dm * dm));
        return new DriftResult(mx, my, MathF.Min(1f, MathF.Sqrt(mx * mx + my * my)), samples.Max(s => s.Magnitude),
            samples.Average(s => (double)s.RawX), samples.Average(s => (double)s.RawY), noise);
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
        float mean = filled.Average(), min = filled.Min(), max = filled.Max();
        // 軸の両側の 2 つの区切り(例 0 度なら 355〜360 と 0〜5)の大きい方を、その向きの端とする
        float Edge(int bin) => MaxOf(bins[(bin + Bins - 1) % Bins], bins[bin]);
        float Asym(int a, int b) => float.IsNaN(Edge(a)) || float.IsNaN(Edge(b)) ? 0 : (Edge(a) - Edge(b)) / mean;
        return new OuterResult(bins, empty, false, mean, min, max, filled.Average(b => MathF.Abs(1f - b)) * 100f,
            min / max, Asym(0, 36), Asym(18, 54));
    }

    static float MaxOf(float a, float b) => float.IsNaN(a) ? b : float.IsNaN(b) ? a : MathF.Max(a, b);
}
