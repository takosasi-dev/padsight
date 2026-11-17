// GrindLog 向けに書き出す窓(画面設計書 §4.8・仕様書 FR-27)。ゲーム(1〜30 文字)と種別(1〜20 文字)を入れる。
// 前後の空白を除いてから確かめ、除いた値を返す。31 文字目・21 文字目からは入らない(MaxLength)。
using System.Windows;
using System.Windows.Controls;

namespace PadSight.App;

public sealed class ExportWindow : Window
{
    readonly TextBox _game, _type;
    readonly TextBlock _error;

    public string Game => _game.Text.Trim();
    public string Kind => _type.Text.Trim();

    public ExportWindow(string? lastGame, string lastType, int count, int excluded)
    {
        Title = "GrindLog 向けに書き出す";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        _game = new TextBox { Text = lastGame ?? "", MaxLength = 30, Margin = new Thickness(0, 4, 0, 12) };
        _type = new TextBox { Text = lastType, MaxLength = 20, Margin = new Thickness(0, 4, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(_game, "ゲーム");
        System.Windows.Automation.AutomationProperties.SetName(_type, "種別");
        stack.Children.Add(Label("ゲーム"));
        stack.Children.Add(_game);
        stack.Children.Add(Label("種別"));
        stack.Children.Add(_type);
        stack.Children.Add(Label(excluded > 0
            ? $"書き出すセッション {count} 件(1 分に満たない {excluded} 件は除きます)"
            : $"書き出すセッション {count} 件"));
        _error = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        stack.Children.Add(_error);
        var note = new TextBlock { Text = "GrindLog では、出所は『手入力』、タグは『PadSight』として入ります", Margin = new Thickness(0, 8, 0, 12) };
        note.SetResourceReference(StyleProperty, "Weak");
        stack.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "書き出す", IsDefault = true, MinWidth = 96 };
        ok.SetResourceReference(StyleProperty, "Primary"); // この窓の主な操作
        ok.Click += (_, _) => Submit();
        var cancel = new Button { Content = "取り消し", IsCancel = true, MinWidth = 96, Margin = new Thickness(0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;
        Loaded += (_, _) => _game.Focus();
    }

    static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    void Submit()
    {
        if (Game.Length == 0) { _error.Text = "ゲームの名前を入れてください"; return; }
        if (Kind.Length == 0) { _error.Text = "種別を入れてください"; return; }
        DialogResult = true;
    }
}
