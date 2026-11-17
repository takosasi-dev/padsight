// メインの画面の色のトークン(WhiteBench 共通規約 §6.5 と同じ値。画面設計書 §6)。XAML からは DynamicResource で読む。
// テーマはメインの画面だけに効き、パネルの見た目は変えない(FR-30)。
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PadSight.App;

public static class Theme
{
    // Hover は指を置いたときの塗り、Primary は主なボタンの塗り(白い文字と 4.5:1 以上になる濃さ)。2026-11-17 に足した
    // WarningText は警告の色で文字を書くとき用(明るいテーマの Warning #B58600 は地との差が 3.3:1 しか無いので、文字だけ濃くする。暗いテーマは Warning と同じ)
    static readonly string[] Keys = ["Background", "Surface", "Border", "Text", "SubText", "Accent", "Success", "Warning", "Error", "Hover", "Primary", "Selected", "WarningText"];
    static readonly string[] Dark = ["#1E1F22", "#2B2D31", "#3F4147", "#F2F3F5", "#B5BAC1", "#4F8EF7", "#3BA55D", "#E0A800", "#ED4245", "#383A40", "#2F6FE0", "#33405A", "#E0A800"];
    static readonly string[] Light = ["#F5F6F8", "#FFFFFF", "#D6D8DC", "#1E1F22", "#5C5F66", "#2F6FE0", "#2D8A4B", "#B58600", "#D13438", "#ECEEF1", "#2F6FE0", "#DCE7FB", "#8A6400"];

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
        IsDark = dark;
        foreach (Window w in Application.Current.Windows)
            OverlayCore.Win32Window.SetDarkTitleBar(w, dark);
    }

    /// <summary>今のメインの画面のテーマが暗いか。題名の帯をそろえるのに使う。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>起動時に 1 回呼ぶ。これから開く窓(メインの画面・初回の案内・書き出しの窓)の題名の帯もテーマにそろえる。</summary>
    public static void FollowNewWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => OverlayCore.Win32Window.SetDarkTitleBar((Window)s, IsDark)));

    static bool SystemIsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
