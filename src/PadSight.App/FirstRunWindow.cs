// S6 初回の案内(画面設計書 §4.10・仕様書 FR-31)。settings.json が無い起動のときだけ出す。
// 「すること」「しないこと」「遊び方の注意」を出し、「はじめる」でパネルを既定の位置に出す。
using System.Windows;
using System.Windows.Controls;

namespace PadSight.App;

public sealed class FirstRunWindow : Window
{
    public event Action? Started;

    public FirstRunWindow()
    {
        Title = "PadSight へようこそ";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        var s = App.Current.Settings;
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(AboutPage.Section("すること"));
        foreach (var t in Texts.Does) stack.Children.Add(AboutPage.Bullet(t));
        stack.Children.Add(AboutPage.Section("しないこと"));
        foreach (var t in Texts.DoesNot) stack.Children.Add(AboutPage.Bullet(t));
        stack.Children.Add(AboutPage.Section("遊び方の注意"));
        foreach (var t in Texts.HowToPlay(s.HotkeyToggle, s.HotkeyArrange)) stack.Children.Add(AboutPage.Bullet(t));
        var weak = new TextBlock { Text = Texts.AntiCheat, Margin = new Thickness(0, 12, 0, 12), Style = (Style)FindResource("Weak") };
        stack.Children.Add(weak);
        var start = new Button { Content = "はじめる", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        start.Click += (_, _) => { Started?.Invoke(); Close(); };
        stack.Children.Add(start);
        Content = stack;
    }
}

/// <summary>初回の案内と「このソフトについて」で共通に使う、文字の部品。</summary>
public static class AboutPage
{
    public static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Section");
        return t;
    }

    public static TextBlock Bullet(string text)
    {
        var t = new TextBlock { Text = "・" + text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    public static StackPanel DoesAndDoesNot()
    {
        var s = new StackPanel();
        s.Children.Add(Section("すること"));
        foreach (var t in Texts.Does) s.Children.Add(Bullet(t));
        s.Children.Add(Section("しないこと"));
        foreach (var t in Texts.DoesNot) s.Children.Add(Bullet(t));
        return s;
    }
}
