// メインの画面の部品を作る小さい道具(ページ、区切りの箱、見出し、注意書き、ラベルの行、スライダー、選択、チェック、表の列、数の札、空の表示)。
// 色は DynamicResource で読み、テーマの切り替えに付いてくる(TaskDeck 規約 §1.3)。ラベルの幅・余白はここでそろえ、タブごとに決めない。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;

namespace PadSight.App.Main;

public static class Ui
{
    /// <summary>ラベルの列の幅(どのタブでも同じにする)。</summary>
    public const double LabelWidth = 210;

    public static TextBlock Section(string text) => AboutPage.Section(text);

    public static TextBlock SubSection(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SubSection");
        return t;
    }

    public static TextBlock Text(string text = "")
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    public static TextBlock Weak(string text = "")
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Weak");
        return t;
    }

    /// <summary>文字を入れ、空なら行ごと畳む(空の行が隙間に見えないように)。</summary>
    public static void SetText(TextBlock t, string text)
    {
        t.Text = text;
        t.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Button Button(string text, Action onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 2, 8, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>画面の主な操作(画面に 1 つ)。</summary>
    public static Button Primary(string text, Action onClick) => Styled(Button(text, onClick), "Primary");

    /// <summary>終わる・消すなど戻せない操作。</summary>
    public static Button Danger(string text, Action onClick) => Styled(Button(text, onClick), "Danger");

    static Button Styled(Button b, string key)
    {
        b.SetResourceReference(FrameworkElement.StyleProperty, key);
        return b;
    }

    /// <summary>横に並べる。幅が足りなければ次の行へ折り返す(欄の右が切れて見えなくならないように)。高さの違う部品は行の中で上下の真ん中にそろえる。</summary>
    public static WrapPanel Horizontal(params UIElement[] children)
    {
        var s = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var c in children)
        {
            if (c is FrameworkElement f && f.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue)
                f.VerticalAlignment = VerticalAlignment.Center;
            s.Children.Add(c);
        }
        return s;
    }

    /// <summary>左にラベル、右に部品。ラベルは部品の 1 行目の高さの真ん中にそろえ、部品が折り返しても上に残す。</summary>
    public static Grid Row(string label, UIElement control, double labelWidth = LabelWidth)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Text(label);
        l.VerticalAlignment = VerticalAlignment.Top;
        l.Margin = new Thickness(0, 10, 12, 0);
        g.Children.Add(l);
        bool wraps = control is WrapPanel;
        if (control is FrameworkElement f && !wraps)
            f.VerticalAlignment = VerticalAlignment.Center;
        AutomationLabel(control, label);
        // 1 行の部品は高さ 40 の中で真ん中に置く(折り返す WrapPanel の 1 行目と同じ高さ)
        var cell = new Border { Child = control, MinHeight = 40, Padding = wraps ? new Thickness(0) : new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(cell, 1);
        g.Children.Add(cell);
        // 狭いとき(部品の場所が NarrowControl より狭い)は、ラベルを上、部品を下に積む(右が切れないように。§6)
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.SizeChanged += (_, e) =>
        {
            bool stack = e.NewSize.Width < labelWidth + NarrowControl;
            if (stack == (Grid.GetRow(cell) == 1))
                return;
            g.ColumnDefinitions[0].Width = stack ? new GridLength(0) : new GridLength(labelWidth);
            Grid.SetColumnSpan(l, stack ? 2 : 1);
            l.Margin = stack ? new Thickness(0, 6, 0, 0) : new Thickness(0, 10, 12, 0);
            Grid.SetRow(cell, stack ? 1 : 0);
            Grid.SetColumn(cell, stack ? 0 : 1);
            Grid.SetColumnSpan(cell, stack ? 2 : 1);
        };
        return g;
    }

    /// <summary>ラベルの右に部品を置くのに要る幅。これより狭ければラベルを上に積む。</summary>
    const double NarrowControl = 230;

    /// <summary>スライダーと数の表示。範囲の外は入らない(画面設計書 §4.6)。</summary>
    public static Grid Slider(string label, double min, double max, double step, double value, string format, Action<double> onChange, double labelWidth = LabelWidth, double width = 190)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5, TickFrequency = step, IsSnapToTickEnabled = true, Width = width };
        AutomationLabel(s, label);
        var t = Text(value.ToString(format, CultureInfo.InvariantCulture));
        t.Margin = new Thickness(12, 0, 0, 0);
        t.MinWidth = 44;
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        s.ValueChanged += (_, e) =>
        {
            double v = Math.Round(e.NewValue / step) * step;
            t.Text = v.ToString(format, CultureInfo.InvariantCulture);
            onChange(v);
        };
        return Row(label, Horizontal(s, t), labelWidth);
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
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 5, 16, 5) };
        c.Checked += (_, _) => onChange(true);
        c.Unchecked += (_, _) => onChange(false);
        return c;
    }

    /// <summary>読み上げ用の名前(ラベルの行に置いた部品は、横の文字と結び付いていないため)。</summary>
    public static void AutomationLabel(DependencyObject d, string label)
    {
        if (label.Length > 0 && System.Windows.Automation.AutomationProperties.GetName(d).Length == 0)
            System.Windows.Automation.AutomationProperties.SetName(d, label);
    }

    /// <summary>区切りの箱。見出しがあれば箱の中の上に出す。</summary>
    public static Border Card(string? title, params UIElement[] children)
    {
        var body = new StackPanel();
        if (title is not null)
        {
            var h = Section(title);
            h.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(h);
        }
        foreach (var c in children)
            body.Children.Add(c);
        var b = new Border { Child = body };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    /// <summary>タブの中身。縦にスクロールし、横に広げすぎない(広い窓で行が間延びしないように)。</summary>
    public static ScrollViewer Page(params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var c in children)
            stack.Children.Add(c);
        return Scroll(LeftCapped(stack, 980));
    }

    /// <summary>幅いっぱいに広げるが maxWidth で止め、余りは右に残す(広い窓でも左にそろう。MaxWidth だけだと真ん中に寄る)。</summary>
    public static Grid LeftCapped(UIElement content, double maxWidth)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = maxWidth });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(content);
        return g;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20, 20, 20, 4), Focusable = false,
    };

    /// <summary>数の札(上に小さいラベル、下に大きい数)。数の部分を返すので、呼ぶ側で書き換える。</summary>
    public static Border Stat(string label, out TextBlock value)
    {
        var l = Weak(label);
        l.Margin = new Thickness(0);
        value = Text("—");
        value.FontSize = 22;
        value.FontWeight = FontWeights.SemiBold;
        value.TextWrapping = TextWrapping.NoWrap;
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var s = new StackPanel();
        s.Children.Add(l);
        s.Children.Add(value);
        var b = new Border { Child = s, MinWidth = 150 };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        b.Margin = new Thickness(0, 0, 12, 12);
        b.Padding = new Thickness(16, 10, 20, 12);
        return b;
    }

    /// <summary>表の列。数の列は右にそろえ、桁をそろえる(画面設計書 §6)。</summary>
    public static GridViewColumn Column(string header, string path, double width, bool number = false)
    {
        var col = new GridViewColumn { Header = header, Width = width };
        var f = new FrameworkElementFactory(typeof(TextBlock));
        if (!number)
        {
            // 列より長い文字は「…」で切り、指を置くと全部を出す(列の幅は見出しの右端で変えられる)
            f.SetBinding(TextBlock.TextProperty, new Binding(path));
            f.SetBinding(FrameworkElement.ToolTipProperty, new Binding(path));
            f.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            col.CellTemplate = new DataTemplate { VisualTree = f };
            return col;
        }
        f.SetBinding(TextBlock.TextProperty, new Binding(path));
        f.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        f.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
        f.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        col.CellTemplate = new DataTemplate { VisualTree = f };
        var hs = new Style(typeof(GridViewColumnHeader), (Style)Application.Current.FindResource(typeof(GridViewColumnHeader)));
        hs.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
        hs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 10, 18, 7)));
        col.HeaderContainerStyle = hs;
        return col;
    }

    /// <summary>何も無いときの表示(表や絵の場所に、縁の中で弱い文字を出す)。</summary>
    public static Border Empty(TextBlock text)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.TextAlignment = TextAlignment.Center;
        var b = new Border { Child = text, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 22, 16, 22), Margin = new Thickness(0, 2, 0, 6) };
        b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        b.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        return b;
    }

    /// <summary>パネルの色で描いた絵の台(ホームの小さいパッド・見本・統計の絵・スティック診断の図で同じ形)。</summary>
    public static Border Picture(UIElement child) => new()
    {
        Child = child, Background = Panels.Draw.Background, CornerRadius = new CornerRadius(8), Padding = new Thickness(8),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };
}
