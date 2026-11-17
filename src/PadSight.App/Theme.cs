// メインの画面の色のトークン(WhiteBench 共通規約 §6.5 と同じ値。画面設計書 §6)。XAML からは DynamicResource で読む。
// テーマはメインの画面だけに効き、パネルの見た目は変えない(FR-30)。
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PadSight.App;

public static class Theme
{
    static readonly string[] Keys = ["Background", "Surface", "Border", "Text", "SubText", "Accent", "Success", "Warning", "Error"];
    static readonly string[] Dark = ["#1E1F22", "#2B2D31", "#3F4147", "#F2F3F5", "#B5BAC1", "#4F8EF7", "#3BA55D", "#E0A800", "#ED4245"];
    static readonly string[] Light = ["#F5F6F8", "#FFFFFF", "#D6D8DC", "#1E1F22", "#5C5F66", "#2F6FE0", "#2D8A4B", "#B58600", "#D13438"];

    public static void Apply(string theme)
    {
        bool dark = theme == "dark" || (theme == "system" && SystemIsDark());
        var values = dark ? Dark : Light;
        var res = Application.Current.Resources;
        for (int i = 0; i < Keys.Length; i++)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
            b.Freeze();
            res[Keys[i] + "Brush"] = b;
        }
    }

    static bool SystemIsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
