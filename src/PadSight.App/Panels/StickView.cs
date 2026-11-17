// O3 スティック(画面設計書 §4.3・仕様書 FR-22)。左右の位置・軌跡・デッドゾーンの円・数値・トリガーの値を出す。
// 軌跡は最大 64 本の線を使い回し、古いほど薄くする。デッドゾーンの円は表示だけで値を変えない。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class StickView : PanelView
{
    const double Box = 100, R = 46;
    const int Segments = 64;

    sealed class Side
    {
        public required double Ox;
        public required Ellipse Deadzone;
        public required Ellipse Knob;
        public required Line[] Trail;
        public required OutlineText Xy;
        public required OutlineText Mag;
        public required OutlineText TriggerLabel;
        public required Rectangle TriggerFill;
        public required OutlineText TriggerPct;
    }

    readonly Side _l, _r;

    public StickView() : base(360, 200)
    {
        _l = BuildSide(20, "L");
        _r = BuildSide(200, "R");
    }

    public override bool Animating(PadSnapshot s) => s.LeftTrail.Count > 0 || s.RightTrail.Count > 0;

    Side BuildSide(double ox, string name)
    {
        Canvas.Children.Add(Draw.Text(name, 12).At(ox, 2));
        double cx = ox + 20 + Box / 2, cy = 20 + Box / 2;
        Canvas.Children.Add(new Ellipse { Width = R * 2, Height = R * 2, Stroke = Draw.Line, StrokeThickness = 1 }.At(cx - R, cy - R));
        Canvas.Children.Add(new Line { X1 = cx - R, X2 = cx + R, Y1 = cy, Y2 = cy, Stroke = Draw.Faint, StrokeThickness = 1 });
        Canvas.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = cy - R, Y2 = cy + R, Stroke = Draw.Faint, StrokeThickness = 1 });
        var dz = new Ellipse { Stroke = Draw.Weak, StrokeThickness = 1, StrokeDashArray = [3, 3] };
        Canvas.Children.Add(dz);
        var trail = new Line[Segments];
        for (int i = 0; i < Segments; i++)
        {
            trail[i] = new Line { Stroke = Draw.TextBrush, StrokeThickness = 2, Visibility = Visibility.Hidden };
            Canvas.Children.Add(trail[i]);
        }
        var knob = new Ellipse { Width = 10, Height = 10, Fill = Draw.TextBrush, RenderTransform = new TranslateTransform() };
        Canvas.Children.Add(knob.At(cx - 5, cy - 5));
        var xy = Draw.Text("", 12, mono: true).At(ox, 124);
        var mag = Draw.Text("", 12, mono: true).At(ox, 142);
        var tl = Draw.Text("", 12, mono: true).At(ox, 166);
        Canvas.Children.Add(xy);
        Canvas.Children.Add(mag);
        Canvas.Children.Add(tl);
        Canvas.Children.Add(new Rectangle { Width = 70, Height = 10, Stroke = Draw.Line, StrokeThickness = 1 }.At(ox + 26, 170));
        var fill = new Rectangle { Width = 0, Height = 10 }.At(ox + 26, 170);
        Canvas.Children.Add(fill);
        var pct = Draw.Text("", 12, mono: true).At(ox + 102, 166);
        Canvas.Children.Add(pct);
        return new Side { Ox = ox, Deadzone = dz, Knob = knob, Trail = trail, Xy = xy, Mag = mag, TriggerLabel = tl, TriggerFill = fill, TriggerPct = pct };
    }

    public override void Render(PadSnapshot s, Settings settings, ulong nowNs)
    {
        ShowStatus(StatusOf(s));
        var skin = Labels.ChooseSkin(s.Pad, s.Pad is null ? null : settings.Devices.GetValueOrDefault(s.Pad.Guid));
        RenderSide(_l, s.Left, s.LeftTrail, s.Pressed.Contains(PadButton.LeftStick), s.LeftTrigger,
            Labels.For("LeftTrigger", skin, settings.LabelStyle, s.Pad), settings, nowNs);
        RenderSide(_r, s.Right, s.RightTrail, s.Pressed.Contains(PadButton.RightStick), s.RightTrigger,
            Labels.For("RightTrigger", skin, settings.LabelStyle, s.Pad), settings, nowNs);
    }

    void RenderSide(Side side, StickValue v, IReadOnlyList<TrailPoint> trail, bool pressed, float trigger, string trigName, Settings st, ulong nowNs)
    {
        var inv = CultureInfo.InvariantCulture;
        double cx = side.Ox + 20 + Box / 2, cy = 20 + Box / 2;
        double dz = st.Deadzone * R;
        side.Deadzone.Visibility = dz > 0 ? Visibility.Visible : Visibility.Hidden;
        side.Deadzone.Width = side.Deadzone.Height = dz * 2;
        side.Deadzone.At(cx - dz, cy - dz);
        var t = (TranslateTransform)side.Knob.RenderTransform;
        t.X = v.X * R;
        t.Y = -v.Y * R;
        side.Knob.Fill = pressed ? Draw.Pressed(st) : Draw.TextBrush;

        // 軌跡: 直近 trail_ms の点を、最大 64 本の線に間引いて結ぶ
        var pts = trail.Where(p => nowNs < p.Ns || nowNs - p.Ns <= (ulong)st.TrailMs * Values.NsPerMs).ToList();
        int step = Math.Max(1, (int)Math.Ceiling(pts.Count / (double)Segments));
        int used = 0;
        for (int i = step; i < pts.Count && used < Segments; i += step, used++)
        {
            var a = pts[i - step];
            var b = pts[i];
            var line = side.Trail[used];
            line.X1 = cx + a.X * R; line.Y1 = cy - a.Y * R;
            line.X2 = cx + b.X * R; line.Y2 = cy - b.Y * R;
            double age = nowNs > b.Ns ? (nowNs - b.Ns) / (double)Math.Max(1UL, (ulong)st.TrailMs * Values.NsPerMs) : 0;
            line.Opacity = Math.Clamp(1 - age, 0.05, 1) * 0.8;
            line.Visibility = Visibility.Visible;
        }
        for (int i = used; i < Segments; i++)
            side.Trail[i].Visibility = Visibility.Hidden;

        side.Xy.Text = st.ShowRaw
            ? $"X {v.RawX.ToString("+0;-0", inv)} ({v.X.ToString("+0.000;-0.000", inv)})  Y {v.RawY.ToString("+0;-0", inv)}"
            : $"X {v.X.ToString("+0.000;-0.000", inv)}  Y {v.Y.ToString("+0.000;-0.000", inv)}";
        side.Mag.Text = $"倒し {v.Magnitude.ToString("0.000", inv)}  角度 {(v.Magnitude < 0.05f ? "—" : ((int)Math.Round(v.AngleDeg) % 360).ToString(inv) + "°")}";
        side.TriggerLabel.Text = trigName;
        side.TriggerFill.Width = 70 * trigger;
        side.TriggerPct.Text = Math.Round(trigger * 100).ToString(inv) + "%";
        side.TriggerFill.Fill = trigger >= st.TriggerThreshold ? Draw.Pressed(st) : Draw.Line;
    }
}
