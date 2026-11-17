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
        StatusText = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 16, FontFamily = Draw.Ui, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
        };
        var root = new Grid();
        root.Children.Add(Canvas);
        root.Children.Add(StatusText);
        Content = root;
        IsHitTestVisible = false;
    }

    protected Canvas Canvas { get; }
    TextBlock StatusText { get; }

    /// <summary>スナップショットの版が変わったとき、または時間で変わる表示を更新するときに呼ぶ。</summary>
    public abstract void Render(PadSnapshot s, Settings settings, ulong nowNs);

    /// <summary>時間で変わる表示を持っているか(持っていれば毎フレーム Render する)。</summary>
    public virtual bool Animating(PadSnapshot s) => false;

    /// <summary>未接続・つなぎ直し中は中身を 30% にして中央に文字を出す(画面設計書 §3)。</summary>
    protected void ShowStatus(string? text)
    {
        Canvas.Opacity = text is null ? 1 : 0.3;
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

public static class Draw
{
    public static readonly FontFamily Ui = new("Yu Gothic UI");
    public static readonly FontFamily Mono = new("Consolas");
    public static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)));
    public static readonly Brush Weak = Freeze(new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)));
    public static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
    public const double Thin = 2.0, Thick = 3.0;

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

    public static TextBlock Text(string text, double size = 12, bool mono = false) => new()
    {
        Text = text, Foreground = Brushes.White, FontSize = size, FontFamily = mono ? Mono : Ui,
    };

    /// <summary>上向きの矢印(中心 0,0)。方向は RotateTransform で回す。文字ではなく図形で描く(画面設計書 §6)。</summary>
    public static Path Arrow(double size)
    {
        double s = size / 16;
        var geo = Geometry.Parse($"M 0,{-8 * s} L {6 * s},{-1 * s} L {2 * s},{-1 * s} L {2 * s},{8 * s} L {-2 * s},{8 * s} L {-2 * s},{-1 * s} L {-6 * s},{-1 * s} Z");
        geo.Freeze();
        return new Path { Data = geo, Fill = Brushes.White, RenderTransform = new RotateTransform(0) };
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
