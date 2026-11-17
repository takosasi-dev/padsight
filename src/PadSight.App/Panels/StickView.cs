// O3 スティック(画面設計書 §4.3・仕様書 FR-22)。左右の位置・軌跡・デッドゾーンの円・数値・トリガーの値を出す。
// パッド表示と同じ作り: 本体の色の縁の中に、くぼみの円(半径 1)と、下から満ちるトリガーの板を置く。
// 軌跡は最大 64 本の線を使い回し、古いほど細く薄くする。デッドゾーンの円は表示だけで値を変えない。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class StickView : PanelView
{
    // 片側 180 の中の位置: くぼみの中心と半径、その右のトリガーの板、下の数値の行
    const double Cx = 72, Cy = 76, R = 52, Rim = 6;
    const double GaugeX = 144, GaugeY = 28, GaugeW = 20, GaugeH = 92;
    const double TextX = 14, TextY = 144, RowH = 18;
    const int Segments = 64;

    sealed class Side
    {
        public required double Ox;
        public required Ellipse Deadzone;
        public required TranslateTransform Knob;
        public required Ellipse Cap;
        public required Ellipse Ring;
        public required Line[] Trail;
        public required OutlineText[] Rows;
        public required OutlineText TriggerLabel;
        public required Rectangle TriggerFill;
        public required Line Threshold;
        public required OutlineText TriggerPct;
        /// <summary>前に数値の行へ書いた値(同じなら行を書き直さない。軌跡のある間は毎フレーム描くため)。</summary>
        public (StickValue V, bool Raw)? Shown;
    }

    readonly Side _l, _r;

    public StickView() : base(360, 200)
    {
        _l = BuildSide(0, "L");
        _r = BuildSide(180, "R");
    }

    public override bool Animating(PadSnapshot s) => s.LeftTrail.Count > 0 || s.RightTrail.Count > 0;

    Side BuildSide(double ox, string name)
    {
        double cx = ox + Cx, cy = Cy;
        // 本体の色の縁と、くぼみ(半径 1 の円)。縁の線はパッドの本体と同じ弱い線
        Canvas.Children.Add(new Ellipse { Width = (R + Rim) * 2, Height = (R + Rim) * 2, Fill = Draw.Body, Stroke = Draw.Weak, StrokeThickness = 1.5 }.At(cx - R - Rim, cy - R - Rim));
        Canvas.Children.Add(new Ellipse { Width = R * 2, Height = R * 2, Fill = Draw.Inset, Stroke = Draw.Line, StrokeThickness = Draw.Thin }.At(cx - R, cy - R));
        // 目盛り: 十字の線と、半分の円
        Canvas.Children.Add(new Line { X1 = cx - R, X2 = cx + R, Y1 = cy, Y2 = cy, Stroke = Draw.Faint, StrokeThickness = 1 });
        Canvas.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = cy - R, Y2 = cy + R, Stroke = Draw.Faint, StrokeThickness = 1 });
        Canvas.Children.Add(new Ellipse { Width = R, Height = R, Stroke = Draw.Faint, StrokeThickness = 1 }.At(cx - R / 2, cy - R / 2));
        var dz = new Ellipse { Fill = Draw.Body, Stroke = Draw.Weak, StrokeThickness = 1.25, StrokeDashArray = [3, 2] };
        Canvas.Children.Add(dz);
        var trail = new Line[Segments];
        for (int i = 0; i < Segments; i++)
        {
            trail[i] = new Line { Stroke = Draw.Line, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Hidden };
            Canvas.Children.Add(trail[i]);
        }
        // 今の位置: 縁取りの付いた小さい頭。押し込み中は押したときの色で塗り、外に輪を出す(色だけで示さない)
        var knob = new TranslateTransform();
        var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = Draw.Thin, RenderTransform = knob, Visibility = Visibility.Hidden };
        Canvas.Children.Add(ring.At(cx - 11, cy - 11));
        var cap = new Ellipse { Width = 13, Height = 13, Stroke = Draw.Halo, StrokeThickness = 1.5, RenderTransform = knob };
        Canvas.Children.Add(cap.At(cx - 6.5, cy - 6.5));
        Canvas.Children.Add(Draw.Text(name, 14).At(ox + 8, 2));

        // トリガー: 角の丸い板を下から満たす(パッド表示のトリガーと同じ)。横の線は trigger_threshold
        double gx = ox + GaugeX;
        var plate = new RectangleGeometry(new Rect(0, 0, GaugeW, GaugeH), 6, 6);
        plate.Freeze();
        Canvas.Children.Add(new Path { Data = plate, Fill = Draw.Inset, Stroke = Draw.Line, StrokeThickness = Draw.Thin }.At(gx, GaugeY));
        var holder = new Canvas { Width = GaugeW, Height = GaugeH, Clip = plate, IsHitTestVisible = false };
        var fill = new Rectangle { Width = GaugeW, Height = 0 };
        holder.Children.Add(fill.At(0, GaugeH));
        Canvas.Children.Add(holder.At(gx, GaugeY));
        var threshold = new Line { X1 = gx - 4, X2 = gx + GaugeW + 4, Stroke = Draw.Weak, StrokeThickness = 1.5 };
        Canvas.Children.Add(threshold);
        var tl = new OutlineText { FontFamily = Draw.Ui, FontSize = 12, Foreground = Draw.TextBrush, Width = 44, TextAlignment = TextAlignment.Center };
        Canvas.Children.Add(tl.At(gx + GaugeW / 2 - 22, GaugeY - 20));
        var pct = new OutlineText { FontFamily = Draw.Mono, FontSize = 12, Foreground = Draw.TextBrush, Width = 44, TextAlignment = TextAlignment.Center };
        Canvas.Children.Add(pct.At(gx + GaugeW / 2 - 22, GaugeY + GaugeH + 3));

        var rows = new OutlineText[3];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = Draw.Text("", 13, mono: true);
            Canvas.Children.Add(rows[i].At(ox + TextX, TextY + i * RowH));
        }
        return new Side
        {
            Ox = ox, Deadzone = dz, Knob = knob, Cap = cap, Ring = ring, Trail = trail, Rows = rows,
            TriggerLabel = tl, TriggerFill = fill, Threshold = threshold, TriggerPct = pct,
        };
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
        var pressedBrush = Draw.Pressed(st);
        double cx = side.Ox + Cx, cy = Cy;
        double dz = st.Deadzone * R;
        side.Deadzone.Visibility = dz > 0 ? Visibility.Visible : Visibility.Hidden;
        side.Deadzone.Width = side.Deadzone.Height = dz * 2;
        side.Deadzone.At(cx - dz, cy - dz);
        side.Knob.X = v.X * R;
        side.Knob.Y = -v.Y * R;
        side.Cap.Fill = pressed ? pressedBrush : Draw.TextBrush;
        side.Ring.Stroke = pressedBrush;
        side.Ring.Visibility = pressed ? Visibility.Visible : Visibility.Hidden;

        // 軌跡: 直近 trail_ms の点(古い順に並んでいる)を、最大 64 本の線に間引いて結ぶ。新しいほど太く濃い
        ulong span = (ulong)Math.Max(0, st.TrailMs) * Values.NsPerMs;
        int first = 0;
        while (first < trail.Count && nowNs > trail[first].Ns && nowNs - trail[first].Ns > span)
            first++;
        int count = span == 0 ? 0 : trail.Count - first;
        int step = Math.Max(1, (int)Math.Ceiling(count / (double)Segments));
        int used = 0;
        for (int i = step; i < count && used < Segments; i += step, used++)
        {
            var a = trail[first + i - step];
            var b = trail[first + i];
            var line = side.Trail[used];
            line.X1 = cx + a.X * R; line.Y1 = cy - a.Y * R;
            line.X2 = cx + b.X * R; line.Y2 = cy - b.Y * R;
            double fresh = 1 - Math.Clamp(nowNs > b.Ns ? (nowNs - b.Ns) / (double)span : 0, 0, 1);
            line.Opacity = 0.08 + 0.72 * fresh * fresh;
            line.StrokeThickness = 1 + 2.5 * fresh;
            line.Visibility = Visibility.Visible;
        }
        for (int i = used; i < Segments; i++)
            side.Trail[i].Visibility = Visibility.Hidden;

        // 数値は値が変わったときだけ書き直す(軌跡のある間は毎フレーム描くため)
        if (side.Shown != (v, st.ShowRaw))
        {
            side.Shown = (v, st.ShowRaw);
            WriteRows(side.Rows, v, st.ShowRaw);
        }

        side.TriggerLabel.Text = trigName;
        double h = GaugeH * trigger;
        side.TriggerFill.Height = h;
        Canvas.SetTop(side.TriggerFill, GaugeH - h);
        side.TriggerFill.Fill = trigger >= st.TriggerThreshold ? pressedBrush : Draw.Weak;
        side.Threshold.Y1 = side.Threshold.Y2 = GaugeY + GaugeH * (1 - st.TriggerThreshold);
        side.TriggerPct.Text = Math.Round(trigger * 100).ToString(inv) + "%";
    }

    /// <summary>見出しは弱い色、値は文字の色(Consolas で桁をそろえる)。生の値を出すときは X と Y を 1 行ずつ。</summary>
    static void WriteRows(OutlineText[] rows, StickValue v, bool raw)
    {
        var inv = CultureInfo.InvariantCulture;
        string angle = v.Magnitude < 0.05f ? "—" : ((int)Math.Round(v.AngleDeg) % 360).ToString(inv) + "°";
        if (raw)
        {
            Row(rows[0], "X ", $"{v.RawX.ToString("+0;-0", inv)} ({v.X.ToString("+0.000;-0.000", inv)})");
            Row(rows[1], "Y ", $"{v.RawY.ToString("+0;-0", inv)} ({v.Y.ToString("+0.000;-0.000", inv)})");
            Row(rows[2], "倒し ", v.Magnitude.ToString("0.000", inv), "  角度 ", angle);
        }
        else
        {
            Row(rows[0], "X ", v.X.ToString("+0.000;-0.000", inv), "  Y ", v.Y.ToString("+0.000;-0.000", inv));
            Row(rows[1], "倒し ", v.Magnitude.ToString("0.000", inv), "  角度 ", angle);
            Row(rows[2], "");
        }
    }

    /// <summary>見出しと値を交互に渡す(見出しは弱い色)。</summary>
    static void Row(OutlineText row, params string[] parts) =>
        row.SetRuns(parts.Select((t, i) => (t, i % 2 == 0 ? (Brush)Draw.Weak : Draw.TextBrush)));
}
