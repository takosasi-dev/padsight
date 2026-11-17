// パネルの中身の基底と、描くときの共通の部品(色、線、矢印の図形)。画面設計書 §3。
// 中身は窓と切り離した UserControl で、図形は作り直さず値だけを変える(D-9・D-26)。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public abstract class PanelView : UserControl
{
    protected PanelView(double width, double height)
    {
        Width = width;
        Height = height;
        Canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        StatusText = new OutlineText
        {
            Foreground = Draw.TextBrush, FontSize = 16, FontFamily = Draw.Ui, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
        };
        var root = new Grid();
        root.Children.Add(Canvas);
        root.Children.Add(StatusText);
        Content = root;
        IsHitTestVisible = false;
    }

    protected Canvas Canvas { get; }
    OutlineText StatusText { get; }

    /// <summary>スナップショットの版が変わったとき、または時間で変わる表示を更新するときに呼ぶ。</summary>
    public abstract void Render(PadSnapshot s, Settings settings, ulong nowNs);

    /// <summary>時間で変わる表示を持っているか(持っていれば毎フレーム Render する)。</summary>
    public virtual bool Animating(PadSnapshot s) => false;

    /// <summary>未接続・つなぎ直し中は中身を 30% にして中央に文字を出す(画面設計書 §3)。</summary>
    protected void ShowStatus(string? text)
    {
        Canvas.Opacity = text is null ? 1 : 0.6; // 仕様は 30% だが、薄すぎて絵が読めなかった(2026-11-17)
        StatusText.Text = text ?? "";
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    protected static string? StatusOf(PadSnapshot s) => s.Status switch
    {
        InputStatus.Reconnecting => "つなぎ直し中…",
        InputStatus.Failed => "入力の部品を準備できませんでした",
        _ => s.Connected ? null : "未接続",
    };
}

/// <summary>黒い縁取りの付いた文字。明るい画面の上でも読めるように、周りに 8 回ずらして黒で描いてから本体を描く。
/// 影の効果(DropShadowEffect)はソフトウェア描画になるので使わない(TaskDeck 規約 §1.3)。</summary>
public sealed class OutlineText : FrameworkElement
{
    static readonly (double X, double Y)[] Offsets = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    string _text = "";
    List<(string Text, Brush Brush)>? _runs;
    Brush _foreground = Draw.TextBrush;
    double _fontSize = 12;
    FontFamily _fontFamily = Draw.Ui;

    public string Text
    {
        get => _runs is null ? _text : string.Concat(_runs.Select(r => r.Text));
        set { if (_runs is null && _text == value) return; _text = value; _runs = null; InvalidateMeasure(); InvalidateVisual(); }
    }

    public Brush Foreground { get => _foreground; set { _foreground = value; InvalidateVisual(); } }
    public double FontSize { get => _fontSize; set { _fontSize = value; InvalidateMeasure(); } }
    public FontFamily FontFamily { get => _fontFamily; set { _fontFamily = value; InvalidateMeasure(); } }
    public TextAlignment TextAlignment { get; set; }
    public TextTrimming TextTrimming { get; set; }

    /// <summary>色の違う文字を並べる(入力履歴のボタン)。</summary>
    public void SetRuns(IEnumerable<(string Text, Brush Brush)> runs)
    {
        _runs = [.. runs];
        InvalidateMeasure();
        InvalidateVisual();
    }

    FormattedText Build(bool halo)
    {
        var ft = new FormattedText(Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), _fontSize,
            halo ? Draw.Halo : _foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment, Trimming = TextTrimming, MaxLineCount = 3 };
        if (!double.IsNaN(Width))
            ft.MaxTextWidth = Math.Max(1, Width - 2);
        if (!halo && _runs is not null)
        {
            int at = 0;
            foreach (var (t, b) in _runs)
            {
                if (t.Length > 0) ft.SetForegroundBrush(b, at, t.Length);
                at += t.Length;
            }
        }
        return ft;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = Build(true);
        return new Size(double.IsNaN(Width) ? ft.WidthIncludingTrailingWhitespace + 2 : Width, ft.Height + 2);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Text.Length == 0)
            return;
        var halo = Build(true);
        foreach (var (x, y) in Offsets)
            dc.DrawText(halo, new Point(1 + x, 1 + y));
        dc.DrawText(Build(false), new Point(1, 1));
    }
}

public static class Draw
{
    public static readonly FontFamily Ui = new("Yu Gothic UI");
    public static readonly FontFamily Mono = new("Consolas");
    // 配色テーマの色(FR-39)。凍らせずに使い回し、テーマを変えたら色だけを書き換える。UI のスレッドだけで触る
    public static readonly SolidColorBrush Line = new(Color.FromArgb(225, 255, 255, 255));
    public static readonly SolidColorBrush Weak = new(Color.FromArgb(185, 255, 255, 255));
    public static readonly SolidColorBrush Faint = new(Color.FromArgb(90, 255, 255, 255));
    public static readonly SolidColorBrush TextBrush = new(Colors.White);
    public static readonly SolidColorBrush Halo = new(Color.FromArgb(200, 0, 0, 0));
    /// <summary>メインの画面の見本・小さい表示の地(不透明)。</summary>
    public static readonly SolidColorBrush Background = new(Colors.Black);
    public const double Thin = 2.0, Thick = 3.0;

    static string _themeKey = "";

    public static PanelPalette Palette { get; private set; } = PanelTheme.For(new Settings());

    /// <summary>設定のテーマを色に入れる。変わったら真。</summary>
    public static bool ApplyTheme(Settings s)
    {
        string key = $"{s.PanelTheme}|{s.CustomBackground}|{s.CustomLine}|{s.CustomText}";
        if (key == _themeKey)
            return false;
        _themeKey = key;
        Palette = PanelTheme.For(s);
        Line.Color = Palette.Line;
        Weak.Color = Palette.Weak;
        Faint.Color = Palette.Faint;
        TextBrush.Color = Palette.Text;
        Halo.Color = Palette.Halo;
        Background.Color = Palette.Background;
        return true;
    }

    /// <summary>押したときの色で塗った中の文字・矢印の色(明るければ黒、暗ければ白)。</summary>
    public static Brush OnPressed(Brush pressed) =>
        pressed is SolidColorBrush b && PanelTheme.Luminance(b.Color) < 0.5 ? Brushes.White : Brushes.Black;

    static readonly Dictionary<string, Brush> PressedCache = [];

    public static Brush Pressed(Settings s)
    {
        if (!PressedCache.TryGetValue(s.PressedColor, out var b))
            PressedCache[s.PressedColor] = b = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.PressedColor)));
        return b;
    }

    public static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    public static T At<T>(this T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    public static OutlineText Text(string text, double size = 12, bool mono = false) => new OutlineText
    {
        Text = text, Foreground = Draw.TextBrush, FontSize = size, FontFamily = mono ? Mono : Ui,
    };

    /// <summary>上向きの矢印(中心 0,0)。方向は RotateTransform で回す。文字ではなく図形で描く(画面設計書 §6)。</summary>
    public static Path Arrow(double size)
    {
        double s = size / 16;
        var geo = Geometry.Parse($"M 0,{-8 * s} L {6 * s},{-1 * s} L {2 * s},{-1 * s} L {2 * s},{8 * s} L {-2 * s},{8 * s} L {-2 * s},{-1 * s} L {-6 * s},{-1 * s} Z");
        geo.Freeze();
        return new Path { Data = geo, Fill = Draw.TextBrush, RenderTransform = new RotateTransform(0) };
    }

    /// <summary>テンキーの方向を、上を 0 とした時計回りの角度にする。5 は null。</summary>
    public static double? AngleOf(int dir) => dir switch
    {
        8 => 0, 9 => 45, 6 => 90, 3 => 135, 2 => 180, 1 => 225, 4 => 270, 7 => 315, _ => null,
    };

    public static void SetPressed(Shape shape, bool pressed, Brush pressedBrush)
    {
        shape.Fill = pressed ? pressedBrush : Brushes.Transparent;
        shape.Stroke = pressed ? pressedBrush : Line;
        shape.StrokeThickness = pressed ? Thick : Thin;
    }
}
