// O2 入力履歴(画面設計書 §4.2・仕様書 FR-19・FR-20)。新しい行を上に、フレーム数・方向・ボタンを 1 行ずつ出す。
// 一番上の行のフレーム数は、次の行ができるまで画面の 1 フレームごとに増やす。方向は矢印の図形か数字で出す。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class HistoryView : PanelView
{
    const double RowHeight = 24;

    sealed class Row
    {
        public required OutlineText Frames;
        public required Path Arrow;
        public required Ellipse Dot;
        public required OutlineText Number;
        public required OutlineText Buttons;
    }

    readonly List<Row> _rows = [];
    readonly OutlineText _empty;
    int _built = -1;

    public HistoryView() : base(240, 20 * RowHeight + 16)
    {
        _empty = Draw.Text("入力を待っています", 13);
        _empty.Foreground = Draw.Weak;
    }

    public static double HeightFor(int rows) => rows * RowHeight + 16;

    public override bool Animating(PadSnapshot s) => s.History.Count > 0 && s.History[0].EndNs is null;

    public override void Render(PadSnapshot s, Settings settings, ulong nowNs)
    {
        int n = settings.History.Rows;
        if (_built != n)
            Build(n);
        ShowStatus(s.Status == InputStatus.Running ? null : StatusOf(s));
        _empty.Visibility = s.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pressed = Draw.Pressed(settings);
        var pad = s.Pad;
        var skin = Labels.ChooseSkin(pad, pad is null ? null : settings.Devices.GetValueOrDefault(pad.Guid));
        bool arrow = settings.History.DirectionStyle == "arrow";
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= s.History.Count)
            {
                row.Frames.Text = "";
                row.Arrow.Visibility = row.Dot.Visibility = row.Number.Visibility = Visibility.Hidden;
                row.Buttons.SetRuns([]);
                row.Buttons.Tag = null;
                continue;
            }
            var h = s.History[i];
            ulong end = h.EndNs ?? Math.Max(nowNs, h.StartNs);
            string frames = HistoryBuilder.FormatLength(end - h.StartNs, settings.History.FrameBase);
            if (row.Frames.Text != frames)
                row.Frames.Text = frames;
            if (i == 0 && h.EndNs is null && !IsNewRow(row, h))
                continue; // 一番上の行は、フレーム数だけが変わる
            row.Buttons.Tag = h;
            double? angle = Draw.AngleOf(h.Direction);
            row.Number.Text = h.Direction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.Number.Visibility = arrow ? Visibility.Hidden : Visibility.Visible;
            row.Arrow.Visibility = arrow && angle is not null ? Visibility.Visible : Visibility.Hidden;
            row.Dot.Visibility = arrow && angle is null ? Visibility.Visible : Visibility.Hidden;
            if (angle is double a)
                ((RotateTransform)row.Arrow.RenderTransform).Angle = a;
            row.Buttons.SetRuns(
                h.NewlyPressed.Select(k => (Labels.For(k, skin, settings.LabelStyle, pad) + " ", pressed))
                .Concat(h.Held.Select(k => ($"({Labels.For(k, skin, settings.LabelStyle, pad)}) ", (Brush)Draw.Weak))));
        }
    }

    static bool IsNewRow(Row row, HistoryRow h) => !ReferenceEquals(row.Buttons.Tag, h);

    void Build(int n)
    {
        _built = n;
        Height = HeightFor(n);
        Canvas.Height = Height;
        Canvas.Children.Clear();
        _rows.Clear();
        for (int i = 0; i < n; i++)
        {
            double y = 8 + i * RowHeight;
            var row = new Row
            {
                Frames = new OutlineText { FontFamily = Draw.Mono, FontSize = 15, Foreground = Draw.TextBrush, Width = 56, TextAlignment = TextAlignment.Right },
                Arrow = Draw.Arrow(16),
                Dot = new Ellipse { Width = 5, Height = 5, Fill = Draw.TextBrush },
                Number = new OutlineText { FontFamily = Draw.Mono, FontSize = 15, Foreground = Draw.TextBrush },
                Buttons = new OutlineText { FontFamily = Draw.Ui, FontSize = 15, Foreground = Draw.TextBrush, Width = 150, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            Canvas.Children.Add(row.Frames.At(4, y));
            Canvas.Children.Add(row.Arrow.At(78, y + 11));
            Canvas.Children.Add(row.Dot.At(76, y + 9));
            Canvas.Children.Add(row.Number.At(73, y));
            Canvas.Children.Add(row.Buttons.At(100, y));
            _rows.Add(row);
        }
        Canvas.Children.Add(_empty.At(60, Height / 2 - 10));
    }
}
