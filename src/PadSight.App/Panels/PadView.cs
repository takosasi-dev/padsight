// O1 パッド表示(画面設計書 §4.1・仕様書 FR-15〜FR-18・FR-49)。スキンごとの配置を表で持ち、自分で描いた図形だけで描く(D-16・INV-12)。
// 型ごとに本体の輪郭・肩のボタン・トリガーの形を描き、ボタンはその上のくぼみ。押した物は「押したときの色」で塗って線を太くする。
// 押した瞬間は部品の外に光の輪を出し、180 ミリ秒で消す(FR-49)。トリガーは押した分だけ下から塗り、スティックの頭は値 × Travel だけ動かす。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class PadView : PanelView
{
    enum K { Circle, Rect, Trigger, Stick, Touchpad, Lever, Deco, Body, Bumper }

    /// <summary>Circle・Rect・Touchpad・Deco は左上と幅・高さ。Stick・Lever は中心と半径(W)。
    /// Body(本体の輪郭)・Trigger(肩の上のトリガー)は G の形で、X・Y・W・H は使わない。Bumper(肩のボタン)は G の形で、X・Y は文字の中心。</summary>
    sealed record El(K Kind, string Key, double X, double Y, double W, double H = 0, Geometry? G = null);

    /// <summary>絵の幅(左右の写しの軸は X = 180)。</summary>
    const double CanvasWidth = 360;

    static Geometry Geo(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>左側の形を、X = 180 を軸に右へ写す。</summary>
    static Geometry Mirror(Geometry g)
    {
        var m = g.Clone();
        m.Transform = new MatrixTransform(-1, 0, 0, 1, CanvasWidth, 0);
        m.Freeze();
        return m;
    }

    /// <summary>左のトリガーの形から、左右 2 つを作る。</summary>
    static El[] TriggerPair(string trigger)
    {
        var g = Geo(trigger);
        return [new(K.Trigger, "LeftTrigger", 0, 0, 0, G: g), new(K.Trigger, "RightTrigger", 0, 0, 0, G: Mirror(g))];
    }

    /// <summary>左の肩のボタンの形と、その文字の中心 (lx, ly) から、左右 2 つを作る。</summary>
    static El[] Bumpers(string bumper, double lx, double ly)
    {
        var b = Geo(bumper);
        return [new(K.Bumper, "LeftShoulder", lx, ly, 0, G: b), new(K.Bumper, "RightShoulder", CanvasWidth - lx, ly, 0, G: Mirror(b))];
    }

    /// <summary>本体の輪郭。左半分(上の真ん中から時計の逆回りに、下の真ん中まで)を渡すと、右半分を写して閉じる。</summary>
    static El Body(string leftHalf, double topY, double bottomY)
    {
        // 左半分を写した物を逆にたどる代わりに、左右を 1 つずつの図形にして合わせる
        var left = Geo($"M 180,{topY} {leftHalf} L 180,{bottomY} Z");
        var both = Geometry.Combine(left, Mirror(left), GeometryCombineMode.Union, null);
        both.Freeze();
        return new(K.Body, "", 0, 0, 0, G: both);
    }

    /// <summary>十字キー。中心 (cx, cy)、腕 1 本の幅 a。真ん中の四角は飾り。</summary>
    static El[] Dpad(double cx, double cy, double a) =>
    [
        new(K.Deco, "", cx - a / 2, cy - a / 2, a, a),
        new(K.Rect, "DpadUp", cx - a / 2, cy - a * 1.5, a, a), new(K.Rect, "DpadDown", cx - a / 2, cy + a / 2, a, a),
        new(K.Rect, "DpadLeft", cx - a * 1.5, cy - a / 2, a, a), new(K.Rect, "DpadRight", cx + a / 2, cy - a / 2, a, a),
    ];

    /// <summary>右の 4 つのボタン。中心 (cx, cy)、中心どうしの間 d、ボタンの直径 s。</summary>
    static El[] Face(double cx, double cy, double d, double s) =>
    [
        new(K.Circle, "North", cx - s / 2, cy - d - s / 2, s, s), new(K.Circle, "West", cx - d - s / 2, cy - s / 2, s, s),
        new(K.Circle, "East", cx + d - s / 2, cy - s / 2, s, s), new(K.Circle, "South", cx - s / 2, cy + d - s / 2, s, s),
    ];

    // 肩: トリガーは上の縁の後ろから出た丸い板、肩のボタンは本体の上の縁の外側に沿った帯(内側の線は本体の縁と同じ)
    const string XboxTrigger = "M 66,32 L 67,13 C 68,6 73,2 80,2 L 108,2 C 115,2 119,6 119,13 L 120,32 Z";
    const string XboxBumper = "M 14,86 C 24,46 52,26 90,26 L 128,25 C 133,25 135,29 133,33 L 130,40 L 90,40 C 58,42 36,60 26,92 Z";
    const string PsTrigger = "M 60,36 L 61,13 C 62,6 67,2 74,2 L 100,2 C 107,2 111,6 111,13 L 112,36 Z";
    const string PsBumper = "M 6,92 C 12,58 32,34 58,33 C 78,32 98,37 120,37 C 125,37 127,41 125,45 L 120,50 C 96,50 76,44 58,46 C 36,48 22,66 16,96 Z";
    const string ProTrigger = "M 62,34 L 63,13 C 64,6 69,2 76,2 L 104,2 C 111,2 115,6 115,13 L 116,34 Z";
    const string ProBumper = "M 10,100 C 20,58 46,32 82,31 L 128,30 C 133,30 135,34 133,38 L 130,44 C 112,42 96,42 82,44 C 50,48 30,70 22,104 Z";

    static readonly El[] Paddles =
    [
        new(K.Rect, "Paddle1", 128, 208, 24, 16), new(K.Rect, "Paddle2", 156, 208, 24, 16),
        new(K.Rect, "Paddle3", 184, 208, 24, 16), new(K.Rect, "Paddle4", 212, 208, 24, 16),
    ];

    static readonly El[] XboxLayout =
    [
        .. TriggerPair(XboxTrigger),
        Body("C 150,42 118,38 90,40 C 58,42 36,60 26,92 C 16,124 12,170 26,206 C 36,230 62,238 86,228 C 100,222 108,208 118,194 C 132,176 152,170 180,170", 46, 170),
        .. Bumpers(XboxBumper, 100, 33),
        new(K.Stick, "LeftStick", 86, 100, 24),
        new(K.Rect, "Back", 132, 86, 36, 16), new(K.Circle, "Guide", 166, 54, 28, 28), new(K.Rect, "Start", 192, 86, 36, 16),
        new(K.Rect, "Misc1", 160, 110, 40, 16),
        .. Face(276, 100, 24, 22),
        .. Dpad(128, 152, 19),
        new(K.Stick, "RightStick", 232, 152, 22),
        .. Paddles,
    ];

    static readonly El[] PlayStationLayout =
    [
        .. TriggerPair(PsTrigger),
        Body("L 120,50 C 96,50 76,44 58,46 C 36,48 22,66 16,96 C 8,136 10,190 26,220 C 36,238 62,240 76,226 C 88,214 98,196 112,180 C 126,166 150,164 180,164", 50, 164),
        .. Bumpers(PsBumper, 90, 41),
        .. Dpad(66, 106, 19),
        new(K.Rect, "Back", 94, 58, 44, 16), new(K.Touchpad, "Touchpad", 140, 44, 80, 44), new(K.Rect, "Start", 222, 58, 48, 16),
        .. Face(294, 106, 23, 22),
        new(K.Circle, "Guide", 166, 96, 28, 28),
        new(K.Rect, "Misc1", 158, 126, 44, 16),
        new(K.Stick, "LeftStick", 126, 144, 22), new(K.Stick, "RightStick", 234, 144, 22),
        .. Paddles,
    ];

    static readonly El[] SwitchProLayout =
    [
        .. TriggerPair(ProTrigger),
        Body("C 146,44 110,40 82,44 C 50,48 30,70 22,104 C 14,140 16,190 34,216 C 48,236 74,238 92,224 C 104,214 112,198 122,184 C 136,170 156,166 180,166", 48, 166),
        .. Bumpers(ProBumper, 100, 37),
        new(K.Stick, "LeftStick", 82, 100, 24),
        new(K.Rect, "Back", 136, 64, 26, 16), new(K.Rect, "Start", 198, 64, 26, 16),
        new(K.Rect, "Misc1", 118, 90, 56, 16), new(K.Circle, "Guide", 194, 86, 28, 28),
        .. Face(278, 100, 24, 22),
        .. Dpad(132, 148, 18),
        new(K.Stick, "RightStick", 228, 150, 22),
        .. Paddles,
    ];

    // Joy-Con は 2 本の別々の本体。内側の辺がまっすぐで、外側が丸い
    const string JoyConLeft = "M 166,40 L 166,236 L 96,236 C 56,236 34,210 34,170 L 34,104 C 34,64 56,40 96,40 Z";
    const string JoyConTrigger = "M 68,32 C 74,12 88,2 106,2 L 136,2 C 141,2 143,6 143,11 L 143,32 Z";
    const string JoyConBumper = "M 22,104 C 22,56 50,28 96,28 L 140,28 C 144,28 146,31 146,35 L 146,40 L 96,40 C 56,40 34,64 34,104 Z";

    static readonly El[] JoyConPairLayout =
    [
        .. TriggerPair(JoyConTrigger),
        new(K.Body, "", 0, 0, 0, G: Geo(JoyConLeft)), new(K.Body, "", 0, 0, 0, G: Mirror(Geo(JoyConLeft))),
        .. Bumpers(JoyConBumper, 112, 34),
        new(K.Rect, "Back", 138, 54, 22, 16), new(K.Rect, "Start", 200, 54, 22, 16),
        new(K.Stick, "LeftStick", 98, 104, 22),
        new(K.Circle, "DpadUp", 88, 138, 20, 20), new(K.Circle, "DpadLeft", 66, 160, 20, 20),
        new(K.Circle, "DpadRight", 110, 160, 20, 20), new(K.Circle, "DpadDown", 88, 182, 20, 20),
        new(K.Rect, "Misc1", 100, 210, 58, 16),
        .. Face(262, 104, 23, 22),
        new(K.Stick, "RightStick", 262, 172, 22), new(K.Circle, "Guide", 202, 202, 28, 28),
    ];

    static readonly El[] ArcadeLayout =
    [
        new(K.Body, "", 0, 0, 0, G: Geo("M 30,22 L 330,22 C 342,22 350,30 350,42 L 350,214 C 350,226 342,234 330,234 L 30,234 C 18,234 10,226 10,214 L 10,42 C 10,30 18,22 30,22 Z")),
        new(K.Lever, "", 80, 116, 30),
        new(K.Circle, "West", 160, 70, 30, 30), new(K.Circle, "North", 204, 62, 30, 30),
        new(K.Circle, "RightShoulder", 248, 62, 30, 30), new(K.Circle, "LeftShoulder", 292, 70, 30, 30),
        new(K.Circle, "South", 160, 116, 30, 30), new(K.Circle, "East", 204, 108, 30, 30),
        new(K.Circle, "RightTrigger", 248, 108, 30, 30), new(K.Circle, "LeftTrigger", 292, 116, 30, 30),
        new(K.Rect, "Back", 118, 194, 36, 16), new(K.Circle, "Guide", 165, 186, 30, 30), new(K.Rect, "Start", 206, 194, 36, 16),
    ];

    sealed class Part
    {
        public required El El;
        public Shape? Shape;
        public TextBlock? Label;
        public Path? Arrow;
        public Rectangle? Fill;
        public TranslateTransform? Knob;
        /// <summary>トリガーの塗りの高さの最大(形の高さ)。</summary>
        public double Span;
        /// <summary>FR-49 の光の輪(部品の外側の帯を 3 枚重ねた物)と、広げる変換。押せない部品は null。</summary>
        public Canvas? Glow;
        public ScaleTransform? GlowScale;
        public Size GlowSize;
        /// <summary>前の Render で押していたか。光を付けた時刻(0 は光っていない)。</summary>
        public bool WasDown;
        public ulong FlashAt;
    }

    /// <summary>スティックの頭が動く距離(土台の半径に対する割合)。頭の半径は土台の 0.56。</summary>
    const double Travel = 0.44, KnobRatio = 0.56;

    /// <summary>FR-49: 光の長さ。帯の幅と濃さ(内側ほど濃い。重ねると縁の近くが一番明るい)、消えるまでに外へ広がる幅。</summary>
    const ulong FlashNs = 180 * Values.NsPerMs;
    static readonly (double Width, double Opacity)[] GlowBands = [(9, 0.16), (5.5, 0.3), (3, 0.65)];
    const double GlowSpread = 3;

    readonly List<Part> _parts = [];
    readonly List<OutlineText> _raw = [];
    string _builtFor = "";
    Skin _lastSkin = Skin.Generic;
    // 作り直した直後の Render は押している物を覚えるだけにして光らせない(スキンや表記を変えただけで光らないように)
    bool _primed;
    bool _flashing;

    public PadView() : base(360, 240) { }

    /// <summary>光の輪が消えきるまでは毎フレーム描く(D-9。光っていなければ版が変わったときだけ)。</summary>
    public override bool Animating(PadSnapshot s) => _flashing;

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
            RenderParts(s, settings, nowNs);
    }

    void Build(Skin skin, PadInfo? pad, Settings settings)
    {
        Canvas.Children.Clear();
        _parts.Clear();
        _raw.Clear();
        _primed = _flashing = false;
        if (skin == Skin.Unmapped)
        {
            BuildRaw(pad);
            return;
        }
        var layout = skin switch
        {
            Skin.PlayStation => PlayStationLayout,
            Skin.SwitchPro => SwitchProLayout,
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
            case K.Body:
                Canvas.Children.Add(new Path { Data = el.G, Fill = Draw.Body, Stroke = Draw.Weak, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round });
                break;
            case K.Deco: // 十字キーの真ん中。腕の線がそのまま十字の輪郭になるように、線は引かない
                Canvas.Children.Add(new Rectangle { Width = el.W, Height = el.H, Fill = Draw.Inset }.At(el.X, el.Y));
                break;
            case K.Trigger:
            {
                var b = el.G!.Bounds;
                AddGlow(part, el.G);
                Canvas.Children.Add(new Path { Data = el.G, Fill = Draw.Inset, Stroke = Draw.Line, StrokeThickness = Draw.Thin, StrokeLineJoin = PenLineJoin.Round });
                // 塗りは形で切り抜いた入れ物の中で、下から伸ばす(写した形は自分の変換を持つので、組にして動かす)
                var clip = new GeometryGroup { Children = { el.G }, Transform = new TranslateTransform(-b.X, -b.Y) };
                clip.Freeze();
                var holder = new Canvas { Width = b.Width, Height = b.Height, Clip = clip, IsHitTestVisible = false };
                part.Fill = new Rectangle { Width = b.Width, Height = 0 };
                part.Span = b.Height;
                holder.Children.Add(part.Fill.At(0, b.Height));
                Canvas.Children.Add(holder.At(b.X, b.Y));
                var caption = Draw.Text(Labels.For(el.Key, skin, settings.LabelStyle, pad), 12);
                caption.Width = b.Width;
                caption.TextAlignment = TextAlignment.Center;
                Canvas.Children.Add(caption.At(b.X, b.Y + 6));
                break;
            }
            case K.Bumper:
                AddGlow(part, el.G!);
                part.Shape = new Path { Data = el.G, StrokeLineJoin = PenLineJoin.Round };
                Canvas.Children.Add(part.Shape);
                part.Label = new TextBlock
                {
                    Text = Labels.For(el.Key, skin, settings.LabelStyle, pad), FontFamily = Draw.Ui, FontSize = 11,
                    Width = 60, TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
                };
                Canvas.Children.Add(part.Label.At(el.X - 30, el.Y - 8));
                break;
            case K.Stick or K.Lever:
                Canvas.Children.Add(new Ellipse { Width = el.W * 2, Height = el.W * 2, Fill = Draw.Inset, Stroke = Draw.Line, StrokeThickness = Draw.Thin }.At(el.X - el.W, el.Y - el.W));
                double kr = el.W * KnobRatio;
                part.Knob = new TranslateTransform();
                if (el.Kind == K.Stick) // 押し込みの光は頭と一緒に動く
                    AddGlow(part, new EllipseGeometry(new Point(el.X, el.Y), kr, kr), part.Knob);
                part.Shape = new Ellipse { Width = kr * 2, Height = kr * 2, RenderTransform = part.Knob }.At(el.X - kr, el.Y - kr);
                Canvas.Children.Add(part.Shape);
                if (el.Kind == K.Stick) // 頭の上の滑り止めの輪。頭と一緒に動く
                    Canvas.Children.Add(new Ellipse { Width = kr * 1.1, Height = kr * 1.1, Stroke = Draw.Faint, StrokeThickness = 1.5, RenderTransform = part.Knob, IsHitTestVisible = false }.At(el.X - kr * 0.55, el.Y - kr * 0.55));
                break;
            default:
            {
                var r = new Rect(el.X, el.Y, el.W, el.H);
                double rad = el.Key.StartsWith("Dpad", StringComparison.Ordinal) ? 2 : 4;
                AddGlow(part, el.Kind == K.Circle ? new EllipseGeometry(r) : new RectangleGeometry(r, rad, rad));
                part.Shape = el.Kind == K.Circle
                    ? new Ellipse { Width = el.W, Height = el.H }
                    : new Rectangle { Width = el.W, Height = el.H, RadiusX = rad, RadiusY = rad };
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
        }
        return part;
    }

    /// <summary>FR-49 の光の輪を、部品の形のすぐ外側の帯として作り、部品の下に置く(作るのは Build のときだけ)。
    /// 帯は「形を太い線でなぞった範囲 − 形」なので、押していない部品の半透明の塗りから透けない。
    /// ぼかしの効果はソフトウェア描画になるので使わず、幅の違う帯を薄く重ねて柔らかく見せる。</summary>
    void AddGlow(Part part, Geometry shape, Transform? follow = null)
    {
        var glow = new Canvas { Visibility = Visibility.Hidden, IsHitTestVisible = false };
        var pen = new Pen(Brushes.Black, 1) { LineJoin = PenLineJoin.Round };
        foreach (var (w, opacity) in GlowBands)
        {
            pen.Thickness = w * 2;
            var band = Geometry.Combine(shape.GetWidenedPathGeometry(pen), shape, GeometryCombineMode.Exclude, null);
            band.Freeze();
            glow.Children.Add(new Path { Data = band, Opacity = opacity });
        }
        var b = shape.Bounds;
        part.GlowSize = new Size(Math.Max(1, b.Width), Math.Max(1, b.Height));
        part.GlowScale = new ScaleTransform(1, 1, b.X + b.Width / 2, b.Y + b.Height / 2);
        glow.RenderTransform = follow is null ? part.GlowScale : new TransformGroup { Children = { part.GlowScale, follow } };
        part.Glow = glow;
        Canvas.Children.Add(glow);
    }

    /// <summary>押した瞬間に光を付け、付けてから 180 ミリ秒の間、薄くしながら少し外へ広げる。消えきったら隠す。</summary>
    void Flash(Part p, bool down, bool allowed, Brush pressed, ulong nowNs)
    {
        if (p.Glow is null)
            return;
        if (down && !p.WasDown && allowed)
        {
            p.FlashAt = Math.Max(1, nowNs);
            foreach (Path band in p.Glow.Children)
                if (band.Fill != pressed) band.Fill = pressed;
        }
        p.WasDown = down;
        if (p.FlashAt == 0)
            return;
        ulong age = nowNs > p.FlashAt ? nowNs - p.FlashAt : 0;
        if (age >= FlashNs)
        {
            p.FlashAt = 0;
            p.Glow.Visibility = Visibility.Hidden;
            return;
        }
        double t = age / (double)FlashNs;
        p.Glow.Opacity = 0.5 + 0.5 * Math.Cos(Math.PI * t); // 初めは強く、終わりはなめらかに 0 へ
        double spread = GlowSpread * Math.Sin(t * Math.PI / 2);
        p.GlowScale!.ScaleX = 1 + 2 * spread / p.GlowSize.Width;
        p.GlowScale.ScaleY = 1 + 2 * spread / p.GlowSize.Height;
        p.Glow.Visibility = Visibility.Visible;
        _flashing = true;
    }

    void RenderParts(PadSnapshot s, Settings settings, ulong nowNs)
    {
        var pressedBrush = Draw.Pressed(settings);
        float th = (float)settings.TriggerThreshold;
        // FR-49: 作り直した直後の 1 回と、Windows のアニメーション効果が切のときは光らせない
        bool flash = _primed && SystemParameters.ClientAreaAnimation;
        _primed = true;
        _flashing = false;
        foreach (var p in _parts)
        {
            var el = p.El;
            bool down = false;
            switch (el.Kind)
            {
                case K.Trigger:
                    float v = el.Key == "LeftTrigger" ? s.LeftTrigger : s.RightTrigger;
                    double h = p.Span * v;
                    p.Fill!.Height = h;
                    Canvas.SetTop(p.Fill, p.Span - h);
                    down = v >= th;
                    p.Fill.Fill = down ? pressedBrush : Draw.Weak;
                    break;
                case K.Stick:
                    var st = el.Key == "LeftStick" ? s.Left : s.Right;
                    p.Knob!.X = st.X * el.W * Travel;
                    p.Knob.Y = -st.Y * el.W * Travel;
                    down = s.Pressed.Contains(el.Key == "LeftStick" ? PadButton.LeftStick : PadButton.RightStick);
                    Draw.SetPressed(p.Shape!, down, pressedBrush, Draw.Faint);
                    break;
                case K.Lever:
                    var (dx, dy) = LeverOffset(s);
                    p.Knob!.X = dx * el.W * Travel;
                    p.Knob.Y = -dy * el.W * Travel;
                    p.Shape!.Fill = Draw.Line;
                    break;
                case K.Deco or K.Body:
                    break;
                default:
                    down = el.Key switch
                    {
                        "LeftTrigger" => s.LeftTrigger >= th,
                        "RightTrigger" => s.RightTrigger >= th,
                        _ => Enum.TryParse<PadButton>(el.Key, out var b) && s.Pressed.Contains(b),
                    };
                    Draw.SetPressed(p.Shape!, down, pressedBrush, Draw.Inset);
                    if (p.Label is not null) p.Label.Foreground = down ? Draw.OnPressed(pressedBrush) : Draw.TextBrush;
                    if (p.Arrow is not null) p.Arrow.Fill = down ? Draw.OnPressed(pressedBrush) : Draw.TextBrush;
                    break;
            }
            Flash(p, down, flash, pressedBrush, nowNs);
        }
    }

    /// <summary>FR-47: 統計の画面で、ボタン別の通算の濃さ(ButtonHeat.Intensities。鍵 → 0〜1)で塗った絵にする。
    /// 重ねるパネルとは別の PadView で使い、Render とは混ぜない。スキンは呼ぶ側が選ぶ(Unmapped では呼ばない)。
    /// 一番多いボタンは線も太くする(色だけで示さない。画面設計書 §6)。アーケードのレバーは塗らない。</summary>
    public void ShowHeat(Skin skin, PadInfo pad, Settings settings, IReadOnlyDictionary<string, double> heat)
    {
        _builtFor = ""; // 後で Render が来ても作り直すように
        Build(skin, pad, settings);
        ShowStatus(null);
        var pressed = Draw.Pressed(settings);
        var color = ((SolidColorBrush)pressed).Color;
        foreach (var p in _parts)
        {
            var el = p.El;
            double v = heat.GetValueOrDefault(el.Key);
            var brush = v > 0 ? Draw.Freeze(new SolidColorBrush(color) { Opacity = ButtonHeat.Opacity(v) }) : Brushes.Transparent;
            switch (el.Kind)
            {
                case K.Trigger:
                    p.Fill!.Height = p.Span;
                    Canvas.SetTop(p.Fill, 0);
                    p.Fill.Fill = brush;
                    break;
                case K.Lever:
                    p.Shape!.Fill = Draw.Line;
                    break;
                case K.Deco or K.Body:
                    break;
                default: // ボタンと、スティックの押し込み(つまみは真ん中のまま)
                    p.Shape!.Fill = v > 0 ? brush : el.Kind == K.Stick ? Draw.Faint : Draw.Inset;
                    p.Shape.Stroke = v >= 1 ? pressed : Draw.Line;
                    p.Shape.StrokeThickness = v >= 1 ? Draw.Thick : Draw.Thin;
                    // 薄い塗りは地の色が透けるので、半分より濃いときだけ塗りに合わせた文字の色にする
                    var fg = v >= 0.5 ? Draw.OnPressed(pressed) : Draw.TextBrush;
                    if (p.Label is not null) p.Label.Foreground = fg;
                    if (p.Arrow is not null) p.Arrow.Fill = fg;
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
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Draw.TextBrush }.At(48 + i * 90, 179);
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
            _raw[bi].Foreground = raw.Buttons[i] ? Draw.Pressed(settings) : Draw.TextBrush;
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
