// メインの画面の部品を作る小さい道具(見出し、注意書き、スライダーの行、選択の行、チェック)。
// 色は DynamicResource で読み、テーマの切り替えに付いてくる(TaskDeck 規約 §1.3)。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PadSight.App.Main;

public static class Ui
{
    public static TextBlock Section(string text) => AboutPage.Section(text);

    public static TextBlock Text(string text = "")
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    public static TextBlock Weak(string text = "")
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 4) };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Weak");
        return t;
    }

    public static Button Button(string text, Action onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4) };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static Grid Row(string label, UIElement control, double labelWidth = 180)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Text(label));
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>スライダーと数の表示。範囲の外は入らない(画面設計書 §4.6)。</summary>
    public static Grid Slider(string label, double min, double max, double step, double value, string format, Action<double> onChange)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5, TickFrequency = step, IsSnapToTickEnabled = true, Width = 220, VerticalAlignment = VerticalAlignment.Center };
        var t = Text(value.ToString(format, CultureInfo.InvariantCulture));
        t.Margin = new Thickness(8, 0, 0, 0);
        t.MinWidth = 60;
        s.ValueChanged += (_, e) =>
        {
            double v = Math.Round(e.NewValue / step) * step;
            t.Text = v.ToString(format, CultureInfo.InvariantCulture);
            onChange(v);
        };
        return Row(label, Horizontal(s, t));
    }

    public static ComboBox Combo((string Value, string Label)[] items, string selected, Action<string> onChange)
    {
        var c = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (v, l) in items)
            c.Items.Add(new ComboBoxItem { Content = l, Tag = v });
        c.SelectedIndex = Math.Max(0, Array.FindIndex(items, i => i.Value == selected));
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: string v }) onChange(v); };
        return c;
    }

    public static CheckBox Check(string label, bool value, Action<bool> onChange)
    {
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 3, 12, 3) };
        c.Checked += (_, _) => onChange(true);
        c.Unchecked += (_, _) => onChange(false);
        return c;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16),
    };
}
