// O1 パッド表示(画面設計書 §4.1・仕様書 FR-15〜FR-18)。スキンごとの配置を表で持ち、図形で描く(D-16)。
// 押しているボタンは「押したときの色」で塗って線を太くし、スティックの丸は値 ×(土台の半径 × 0.6)だけ動かす。
// メーカーのロゴや画像は使わない(INV-12)。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class PadView : PanelView
{
    enum K { Circle, Rect, Trigger, Stick, Touchpad, Lever, Deco }

    /// <summary>Circle・Rect・Touchpad・Deco は左上と幅・高さ。Stick・Lever は中心と半径(W)。Trigger は文字の左上と棒の長さ(W)・太さ(H)。</summary>
    sealed record El(K Kind, string Key, double X, double Y, double W, double H = 0);

    static readonly El[] Paddles =
    [
        new(K.Rect, "Paddle1", 120, 222, 24, 12), new(K.Rect, "Paddle2", 150, 222, 24, 12),
        new(K.Rect, "Paddle3", 180, 222, 24, 12), new(K.Rect, "Paddle4", 210, 222, 24, 12),
    ];

    static readonly El[] XboxLayout =
    [
        new(K.Trigger, "LeftTrigger", 16, 8, 100, 10), new(K.Trigger, "RightTrigger", 216, 8, 100, 10),
        new(K.Rect, "LeftShoulder", 20, 30, 80, 18), new(K.Rect, "RightShoulder", 260, 30, 80, 18),
        new(K.Stick, "LeftStick", 70, 95, 28),
        new(K.Rect, "Back", 128, 78, 36, 16), new(K.Circle, "Guide", 166, 70, 30, 30), new(K.Rect, "Start", 198, 78, 36, 16),
        new(K.Circle, "North", 278, 70, 24, 24), new(K.Circle, "West", 252, 96, 24, 24),
        new(K.Circle, "East", 304, 96, 24, 24), new(K.Circle, "South", 278, 122, 24, 24),
        new(K.Rect, "DpadUp", 100, 146, 20, 20), new(K.Rect, "DpadDown", 100, 186, 20, 20),
        new(K.Rect, "DpadLeft", 80, 166, 20, 20), new(K.Rect, "DpadRight", 120, 166, 20, 20),
        new(K.Stick, "RightStick", 230, 170, 28),
        new(K.Rect, "Misc1", 280, 196, 44, 16),
        .. Paddles,
    ];

    static readonly El[] PlayStationLayout =
    [
        new(K.Trigger, "LeftTrigger", 16, 8, 100, 10), new(K.Trigger, "RightTrigger", 216, 8, 100, 10),
        new(K.Rect, "LeftShoulder", 20, 30, 80, 18), new(K.Rect, "RightShoulder", 260, 30, 80, 18),
        new(K.Rect, "DpadUp", 50, 86, 20, 20), new(K.Rect, "DpadDown", 50, 126, 20, 20),
        new(K.Rect, "DpadLeft", 30, 106, 20, 20), new(K.Rect, "DpadRight", 70, 106, 20, 20),
        new(K.Rect, "Back", 92, 60, 44, 16), new(K.Touchpad, "Touchpad", 140, 52, 80, 40), new(K.Rect, "Start", 224, 60, 48, 16),
        new(K.Circle, "North", 288, 66, 24, 24), new(K.Circle, "West", 262, 92, 24, 24),
        new(K.Circle, "East", 314, 92, 24, 24), new(K.Circle, "South", 288, 118, 24, 24),
        new(K.Circle, "Guide", 165, 100, 30, 30),
        new(K.Stick, "LeftStick", 120, 180, 26), new(K.Stick, "RightStick", 240, 180, 26),
        new(K.Rect, "Misc1", 158, 150, 44, 16),
        .. Paddles,
    ];

    static readonly El[] JoyConPairLayout =
    [
        new(K.Deco, "", 18, 28, 152, 206), new(K.Deco, "", 190, 28, 152, 206),
        new(K.Trigger, "LeftTrigger", 24, 6, 100, 10), new(K.Trigger, "RightTrigger", 206, 6, 100, 10),
        new(K.Rect, "LeftShoulder", 28, 36, 70, 16), new(K.Rect, "Back", 128, 40, 28, 16),
        new(K.Stick, "LeftStick", 80, 92, 24),
        new(K.Circle, "DpadUp", 70, 128, 20, 20), new(K.Circle, "DpadLeft", 50, 148, 20, 20),
        new(K.Circle, "DpadRight", 90, 148, 20, 20), new(K.Circle, "DpadDown", 70, 168, 20, 20),
        new(K.Rect, "Misc1", 96, 200, 60, 16),
        new(K.Rect, "RightShoulder", 262, 36, 70, 16), new(K.Rect, "Start", 204, 40, 28, 16),
        new(K.Circle, "North", 270, 66, 24, 24), new(K.Circle, "West", 246, 90, 24, 24),
        new(K.Circle, "East", 294, 90, 24, 24), new(K.Circle, "South", 270, 114, 24, 24),
        new(K.Stick, "RightStick", 284, 176, 24), new(K.Circle, "Guide", 204, 186, 30, 30),
    ];

    static readonly El[] ArcadeLayout =
    [
        new(K.Lever, "", 80, 110, 30),
        new(K.Circle, "West", 160, 64, 30, 30), new(K.Circle, "North", 204, 64, 30, 30),
        new(K.Circle, "RightShoulder", 248, 64, 30, 30), new(K.Circle, "LeftShoulder", 292, 64, 30, 30),
        new(K.Circle, "South", 160, 112, 30, 30), new(K.Circle, "East", 204, 112, 30, 30),
        new(K.Circle, "RightTrigger", 248, 112, 30, 30), new(K.Circle, "LeftTrigger", 292, 112, 30, 30),
        new(K.Rect, "Back", 118, 192, 36, 16), new(K.Circle, "Guide", 165, 184, 30, 30), new(K.Rect, "Start", 206, 192, 36, 16),
    ];

    sealed class Part
    {
        public required El El;
        public Shape? Shape;
        public TextBlock? Label;
        public Path? Arrow;
        public Rectangle? Fill;
        public TranslateTransform? Knob;
    }

    readonly List<Part> _parts = [];
    readonly List<TextBlock> _raw = [];
    string _builtFor = "";
    Skin _lastSkin = Skin.Generic;

    public PadView() : base(360, 240) { }

    public override void Render(PadSnapshot s, Settings settings, ulong nowNs)
    {
        var skin = s.Pad is null ? _lastSkin : Labels.ChooseSkin(s.Pad, settings.Devices.GetValueOrDefault(s.Pad.Guid));
        _lastSkin = skin;
        string key = $"{skin}|{s.Pad?.InstanceId}|{settings.LabelStyle}";
        if (key != _builtFor)
        {
            _builtFor = key;
            Build(skin, s.Pad, settings);
        }
        ShowStatus(StatusOf(s));
        if (skin == Skin.Unmapped)
            RenderRaw(s, settings);
        else
            RenderParts(s, settings);
    }

    void Build(Skin skin, PadInfo? pad, Settings settings)
    {
        Canvas.Children.Clear();
        _parts.Clear();
        _raw.Clear();
        if (skin == Skin.Unmapped)
        {
            BuildRaw(pad);
            return;
        }
        var layout = skin switch
        {
            Skin.PlayStation => PlayStationLayout,
            Skin.JoyConPair => JoyConPairLayout,
            Skin.Arcade => ArcadeLayout,
            _ => XboxLayout,
        };
        foreach (var el in layout)
        {
            // 共有・マイク・キャプチャ、タッチパッド、パドルは、パッドが持っているときだけ描く
            if (el.Key is "Misc1" or "Touchpad" || el.Key.StartsWith("Paddle", StringComparison.Ordinal))
            {
                if (pad is null || !Enum.TryParse<PadButton>(el.Key, out var b) || !pad.Buttons.Contains(b))
                    continue;
            }
            _parts.Add(BuildPart(el, skin, pad, settings));
        }
    }

    Part BuildPart(El el, Skin skin, PadInfo? pad, Settings settings)
    {
        var part = new Part { El = el };
        switch (el.Kind)
        {
            case K.Deco:
                Canvas.Children.Add(new Rectangle { Width = el.W, Height = el.H, RadiusX = 30, RadiusY = 30, Stroke = Draw.Faint, StrokeThickness = Draw.Thin }.At(el.X, el.Y));
                break;
            case K.Trigger:
                Canvas.Children.Add(Draw.Text(Labels.For(el.Key, skin, settings.LabelStyle, pad), 12.5).At(el.X, el.Y - 3));
                Canvas.Children.Add(new Rectangle { Width = el.W, Height = el.H, Stroke = Draw.Line, StrokeThickness = 1 }.At(el.X + 26, el.Y));
                part.Fill = new Rectangle { Width = 0, Height = el.H }.At(el.X + 26, el.Y);
                Canvas.Children.Add(part.Fill);
                break;
            case K.Stick or K.Lever:
                Canvas.Children.Add(new Ellipse { Width = el.W * 2, Height = el.W * 2, Stroke = Draw.Line, StrokeThickness = Draw.Thin }.At(el.X - el.W, el.Y - el.W));
                double kr = el.W * 0.45;
                part.Knob = new TranslateTransform();
                part.Shape = new Ellipse { Width = kr * 2, Height = kr * 2, RenderTransform = part.Knob }.At(el.X - kr, el.Y - kr);
                Canvas.Children.Add(part.Shape);
                break;
            default:
                part.Shape = el.Kind == K.Circle
                    ? new Ellipse { Width = el.W, Height = el.H }
                    : new Rectangle { Width = el.W, Height = el.H, RadiusX = 4, RadiusY = 4 };
                Canvas.Children.Add(part.Shape.At(el.X, el.Y));
                if (el.Key.StartsWith("Dpad", StringComparison.Ordinal))
                {
                    part.Arrow = Draw.Arrow(Math.Min(el.W, el.H) * 0.7);
                    ((RotateTransform)part.Arrow.RenderTransform).Angle = el.Key switch { "DpadRight" => 90, "DpadDown" => 180, "DpadLeft" => 270, _ => 0 };
                    Canvas.Children.Add(part.Arrow.At(el.X + el.W / 2, el.Y + el.H / 2));
                }
                else
                {
                    part.Label = new TextBlock
                    {
                        Text = Labels.For(el.Key, skin, settings.LabelStyle, pad), FontFamily = Draw.Ui,
                        FontSize = el.Key == "Guide" ? 9 : el.H < 20 ? 10.5 : 13, Width = el.W, Height = el.H,
                        TextAlignment = TextAlignment.Center, Padding = new Thickness(0, el.H < 20 ? 1 : (el.H - 16) / 2, 0, 0),
                    };
                    Canvas.Children.Add(part.Label.At(el.X, el.Y));
                }
                break;
        }
        return part;
    }

    void RenderParts(PadSnapshot s, Settings settings)
    {
        var pressedBrush = Draw.Pressed(settings);
        float th = (float)settings.TriggerThreshold;
        foreach (var p in _parts)
        {
            var el = p.El;
            switch (el.Kind)
            {
                case K.Trigger:
                    float v = el.Key == "LeftTrigger" ? s.LeftTrigger : s.RightTrigger;
                    p.Fill!.Width = el.W * v;
                    p.Fill.Fill = v >= th ? pressedBrush : Draw.Line;
                    break;
                case K.Stick:
                    var st = el.Key == "LeftStick" ? s.Left : s.Right;
                    p.Knob!.X = st.X * el.W * 0.6;
                    p.Knob.Y = -st.Y * el.W * 0.6;
                    Draw.SetPressed(p.Shape!, s.Pressed.Contains(el.Key == "LeftStick" ? PadButton.LeftStick : PadButton.RightStick), pressedBrush);
                    break;
                case K.Lever:
                    var (dx, dy) = LeverOffset(s);
                    p.Knob!.X = dx * el.W * 0.6;
                    p.Knob.Y = -dy * el.W * 0.6;
                    p.Shape!.Fill = Draw.Line;
                    break;
                case K.Deco:
                    break;
                default:
                    bool down = el.Key switch
                    {
                        "LeftTrigger" => s.LeftTrigger >= th,
                        "RightTrigger" => s.RightTrigger >= th,
                        _ => Enum.TryParse<PadButton>(el.Key, out var b) && s.Pressed.Contains(b),
                    };
                    Draw.SetPressed(p.Shape!, down, pressedBrush);
                    if (p.Label is not null) p.Label.Foreground = down ? Brushes.Black : Brushes.White;
                    if (p.Arrow is not null) p.Arrow.Fill = down ? Brushes.Black : Brushes.White;
                    break;
            }
        }
    }

    /// <summary>レバーは十字キー(押されていなければ左スティック)の方向へ動く(§9.5.3)。</summary>
    static (float X, float Y) LeverOffset(PadSnapshot s)
    {
        int h = (s.Pressed.Contains(PadButton.DpadRight) ? 1 : 0) - (s.Pressed.Contains(PadButton.DpadLeft) ? 1 : 0);
        int v = (s.Pressed.Contains(PadButton.DpadUp) ? 1 : 0) - (s.Pressed.Contains(PadButton.DpadDown) ? 1 : 0);
        if (h != 0 || v != 0)
        {
            float len = MathF.Sqrt(h * h + v * v);
            return (h / len, v / len);
        }
        return (s.Left.X, s.Left.Y);
    }

    // 割り当てなし: ボタン 8 個ずつ折り返して最大 32 個、軸は最大 8 本、ハットは最大 2 つ(画面設計書 §4.1)
    void BuildRaw(PadInfo? pad)
    {
        int buttons = Math.Min(32, pad?.RawButtons ?? 0);
        for (int i = 0; i < buttons; i++)
        {
            var t = Draw.Text("", 11, mono: true).At(10 + i % 8 * 43, 10 + i / 8 * 22);
            _raw.Add(t);
            Canvas.Children.Add(t);
        }
        if ((pad?.RawButtons ?? 0) > 32)
            Canvas.Children.Add(Draw.Text($"ほか {pad!.RawButtons - 32} 個", 11).At(10, 98));
        for (int i = 0; i < Math.Min(8, pad?.RawAxes ?? 0); i++)
        {
            var t = Draw.Text("", 11, mono: true).At(10 + i % 4 * 86, 122 + i / 4 * 20);
            _raw.Add(t);
            Canvas.Children.Add(t);
        }
        for (int i = 0; i < Math.Min(2, pad?.RawHats ?? 0); i++)
        {
            Canvas.Children.Add(Draw.Text($"H{i + 1}", 11, mono: true).At(10 + i * 90, 172));
            var arrow = Draw.Arrow(16);
            Canvas.Children.Add(arrow.At(50 + i * 90, 181));
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White }.At(48 + i * 90, 179);
            Canvas.Children.Add(dot);
        }
    }

    void RenderRaw(PadSnapshot s, Settings settings)
    {
        if (s.Raw is not { } raw)
            return;
        int bi = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < Math.Min(32, raw.Buttons.Count) && bi < _raw.Count; i++, bi++)
        {
            _raw[bi].Text = $"B{i + 1}{(raw.Buttons[i] ? "■" : "□")}";
            _raw[bi].Foreground = raw.Buttons[i] ? Draw.Pressed(settings) : Brushes.White;
        }
        for (int i = 0; i < Math.Min(8, raw.Axes.Count) && bi < _raw.Count; i++, bi++)
            _raw[bi].Text = $"A{i + 1} {raw.Axes[i].ToString("+0.000;-0.000", inv)}";
        // ハットの矢印と点(Canvas の後ろの方に H ごとに 3 つ並ぶ)
        var shapes = Canvas.Children.OfType<Path>().ToList();
        var dots = Canvas.Children.OfType<Ellipse>().ToList();
        for (int i = 0; i < Math.Min(shapes.Count, raw.Hats.Count); i++)
        {
            double? angle = Draw.AngleOf(DirectionResolver.FromHat(raw.Hats[i]));
            shapes[i].Visibility = angle is null ? Visibility.Hidden : Visibility.Visible;
            dots[i].Visibility = angle is null ? Visibility.Visible : Visibility.Hidden;
            if (angle is double a) ((RotateTransform)shapes[i].RenderTransform).Angle = a;
        }
    }
}
