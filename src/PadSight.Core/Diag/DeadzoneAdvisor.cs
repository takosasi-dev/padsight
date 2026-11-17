// デッドゾーンのおすすめ値(仕様書 FR-56・§9.20)。手を離したときの最大のずれと揺れ(と、あれば放置監視の最大のずれ)から % を出す。PadLab の DeadzoneAdvisor を移した物。
// おすすめ = 最大のずれ + 揺れ × 2 + 余裕 0.02 を % に切り上げ、3〜30% に収める(根拠の文の数は PadLab の 2 桁から 3 桁にした。0.055 → 6% が 0.05 → 6% に見えないように)。
using System.Globalization;

namespace PadSight.Core.Diag;

public sealed record DeadzoneAdvice(int RecommendedPercent, string Rationale, IReadOnlyList<(string Use, string Hint)> Hints);

public static class DeadzoneAdvisor
{
    const float Margin = 0.02f;

    static readonly (string Use, string Hint)[] HintTable =
    [
        ("FPS", "おすすめ値どおりの小さめが、細かい狙いに向きます"),
        ("格闘ゲーム", "おすすめ値より 2〜3% 広めにすると、暴発を防ぎやすくなります"),
        ("レース", "おすすめ値どおり"),
    ];

    public static DeadzoneAdvice Compute(float maxDriftDistance, float noiseLevel, float? idleMaxDeviation)
    {
        float drift = MathF.Max(maxDriftDistance, idleMaxDeviation ?? 0);
        float total = drift + 2 * noiseLevel + Margin;
        // 浮動小数の丸めで 0.09 → 9.0000001 → 10 とならないよう、4 桁で丸めてから切り上げる(PadLab と同じ)
        int percent = Math.Clamp((int)Math.Ceiling(Math.Round(total * 100.0, 4)), 3, 30);
        var inv = CultureInfo.InvariantCulture;
        string rationale = $"最大のずれ {drift.ToString("F3", inv)} + 揺れ {noiseLevel.ToString("F3", inv)}×2 + 余裕 {Margin.ToString("F2", inv)} = {total.ToString("F3", inv)} → {percent}%";
        return new DeadzoneAdvice(percent, rationale, HintTable);
    }
}
