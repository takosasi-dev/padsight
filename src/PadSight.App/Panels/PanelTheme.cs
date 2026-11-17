// パネルの配色テーマ(仕様書 FR-39・画面設計書 §3.1)。テーマごとの色の表と、おすすめの押したときの色を持つ。
// 色は Draw の使い回しのブラシに入れるので、テーマを変えても図形を作り直さずに全部の絵へ効く。
using System.Windows.Media;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed record PanelPalette(Color Background, Color Line, Color Text, Color Weak, Color Faint, Color Halo);

public static class PanelTheme
{
    public static readonly (string Value, string Label)[] Choices =
        [("classic", "クラシック"), ("neon", "ネオン"), ("light", "ライト"), ("contrast", "高コントラスト"), ("custom", "自分で選ぶ")];

    static Color C(string hex, byte alpha = 255)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        return c;
    }

    /// <summary>テーマを選んだときに入れる、おすすめの押したときの色。自分で選ぶは null(今の色のまま)。</summary>
    public static string? RecommendedPressed(string theme) => theme switch
    {
        "classic" => "#FFC83D",
        "neon" => "#FF3EA5",
        "light" => "#1F6FEB",
        "contrast" => "#FFFF00",
        _ => null,
    };

    public static PanelPalette For(Settings s) => s.PanelTheme switch
    {
        "neon" => new(C("#0B0820"), C("#4FE3FF", 235), C("#E8FBFF"), C("#9AD8E8", 220), C("#4FE3FF", 80), C("#000000", 200)),
        "light" => new(C("#F4F6FA"), C("#2A2F3A", 230), C("#11151C"), C("#4A5160"), C("#2A2F3A", 70), C("#FFFFFF", 220)),
        "contrast" => new(C("#000000"), C("#FFFFFF"), C("#FFFFFF"), C("#FFFFFF", 230), C("#FFFFFF", 150), C("#000000")),
        "custom" => Custom(s),
        _ => new(C("#000000"), C("#FFFFFF", 225), C("#FFFFFF"), C("#FFFFFF", 185), C("#FFFFFF", 90), C("#000000", 200)),
    };

    static PanelPalette Custom(Settings s)
    {
        var text = C(s.CustomText);
        // 縁取りは、文字が明るければ黒、暗ければ白
        var halo = Luminance(text) > 0.5 ? C("#000000", 200) : C("#FFFFFF", 220);
        return new(C(s.CustomBackground), C(s.CustomLine, 235), text, C(s.CustomText, 191), C(s.CustomLine, 90), halo);
    }

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
