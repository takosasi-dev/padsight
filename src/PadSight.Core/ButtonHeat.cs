// ボタン別の通算の回数を、絵の塗りの濃さ(0〜1)にする(仕様書 FR-47)。一番多いボタンが 1、0 回は 0、ほかは回数に比例。
// 鍵は PadTotals と同じ文字列(PadButton の名前・LeftTrigger・RightTrigger・B1〜)。描くのは App の PadView。
namespace PadSight.Core;

public static class ButtonHeat
{
    /// <summary>塗るときの最低の濃さ。1 回でも押したボタンは、これより薄くしない。
    /// 一番多いボタンの 1% しか押していないボタンも「押したことはある」と見分けられるように 15% にした。
    /// 15% 以上は回数にそのまま比例する。</summary>
    public const double MinVisible = 0.15;

    /// <summary>鍵ごとの濃さ。一番多い物を 1、0 回(と負の値)は 0、ほかは 回数 ÷ 一番多い回数。空なら空。</summary>
    public static Dictionary<string, double> Intensities(IReadOnlyDictionary<string, long> counts)
    {
        long max = counts.Count == 0 ? 0 : counts.Values.Max();
        return counts.ToDictionary(kv => kv.Key, kv => max <= 0 || kv.Value <= 0 ? 0.0 : (double)kv.Value / max);
    }

    /// <summary>濃さを塗りの不透明度にする。0 は 0(塗らない)、0 より大きければ MinVisible 以上。</summary>
    public static double Opacity(double intensity) => intensity <= 0 ? 0 : Math.Max(MinVisible, Math.Min(1, intensity));

    /// <summary>一番多く押した鍵と回数。同じ回数なら鍵の名前順(統計の「多いボタン」と同じ並び)。1 回も無ければ null。</summary>
    public static (string Key, long Count)? Top(IReadOnlyDictionary<string, long> counts)
    {
        var top = counts.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).FirstOrDefault();
        return top.Key is null ? null : (top.Key, top.Value);
    }
}
