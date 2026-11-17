// パネルの配色テーマの設定(FR-39・AC-47)の読み書きと、範囲の外の値の戻し方を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class ThemeSettingsTests
{
    [Fact]
    public void Parse_Default_IsClassic()
    {
        var s = Settings.Parse("{}", out var reset, out _);
        Assert.Equal("classic", s.PanelTheme);
        Assert.Empty(reset);
    }

    [Fact]
    public void Parse_UnknownTheme_ResetsOnlyThatItem()
    {
        var s = Settings.Parse("""{"overlay":{"panel_theme":"rainbow","label_style":"xbox"}}""", out var reset, out _);
        Assert.Equal("classic", s.PanelTheme);
        Assert.Equal("xbox", s.LabelStyle);
        Assert.Equal(["overlay.panel_theme"], reset);
    }

    [Fact]
    public void Parse_BadCustomColor_ResetsThatColor()
    {
        var s = Settings.Parse("""{"overlay":{"panel_theme":"custom","custom_colors":{"background":"#102030","line":"red","text":"#ABCDEF"}}}""", out var reset, out _);
        Assert.Equal("custom", s.PanelTheme);
        Assert.Equal("#102030", s.CustomBackground);
        Assert.Equal("#FFFFFF", s.CustomLine);
        Assert.Equal("#ABCDEF", s.CustomText);
        Assert.Equal(["overlay.custom_colors.line"], reset);
    }

    [Fact]
    public void StickValue_RawZero_IsNotNegativeZero()
    {
        var v = StickValue.FromRaw(0, 0);
        Assert.Equal("+0.000", v.Y.ToString("+0.000;-0.000", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToJson_ThenParse_KeepsTheme()
    {
        var s = new Settings { PanelTheme = "custom", CustomBackground = "#112233", CustomLine = "#445566", CustomText = "#778899" };
        var back = Settings.Parse(s.ToJson(), out var reset, out _);
        Assert.Empty(reset);
        Assert.Equal(("custom", "#112233", "#445566", "#778899"), (back.PanelTheme, back.CustomBackground, back.CustomLine, back.CustomText));
    }
}
