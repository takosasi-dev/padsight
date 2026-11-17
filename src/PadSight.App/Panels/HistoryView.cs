// O2 入力履歴(画面設計書 §4.2・仕様書 FR-19・FR-20・FR-50・FR-51)。新しい行を上に、フレーム数・方向・コマンドの印・ボタン・ずれを出す。
// 一番上の行のフレーム数は、次の行ができるまで画面の 1 フレームごとに増やす。方向は矢印の図形か数字で出す。
// 止めている間(FR-48)は、止めた時点の行と時刻を持ち続けて描き、右上に「停止中」を出す。行づくりは裏で続いている。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class HistoryView : PanelView
{
    const double RowHeight = 24;
    const double PanelWidth = 240;
    const double ButtonsX = 100;      // ボタンの列の左端(印があれば印の右へずらす)
    const char Nbsp = '\u00A0'; // 改行しない空白
    const double BadgeCharWidth = 6.6; // Consolas 12 の 1 文字の幅
    const double PausedTextWidth = 44; // 「停止中」(13) の幅と少しの間

    sealed class Row
    {
        public required OutlineText Frames;
        public required Path Arrow;
        public required Ellipse Dot;
        public required OutlineText Number;
        public required Border Badge;
        public required OutlineText BadgeText;
        public required OutlineText Buttons;
        public required OutlineText Gap;
    }

    readonly List<Row> _rows = [];
    readonly OutlineText _empty;
    readonly OutlineText _pausedText;
    // 一番上の行(今の行)の下に敷く薄い帯
    readonly Rectangle _current = new() { RadiusX = 4, RadiusY = 4, Fill = Draw.Body, Width = PanelWidth - 8, Height = RowHeight - 2, Visibility = Visibility.Collapsed };
    int _built = -1;
    // 止めた時点の行と時刻。止めていなければ null
    (IReadOnlyList<HistoryRow> Rows, ulong NowNs)? _frozen;

    public HistoryView() : base(PanelWidth, 20 * RowHeight + 16)
    {
        _empty = Draw.Text("入力を待っています", 13);
        _empty.Foreground = Draw.Weak;
        _pausedText = new OutlineText { Text = "停止中", FontFamily = Draw.Ui, FontSize = 13, Width = 80, TextAlignment = TextAlignment.Right, Visibility = Visibility.Collapsed };
    }

    /// <summary>FR-48。真にした後の最初の Render で、その時点の行を写して止める。</summary>
    public bool Paused
    {
        get => _paused;
        set { _paused = value; _frozen = null; }
    }
    bool _paused;

    /// <summary>止めている間に履歴を消したときに呼ぶ。出している行も空にする(止めたまま)。</summary>
    public void ClearFrozen() => _frozen = ([], _frozen?.NowNs ?? 0);

    public static double HeightFor(int rows) => rows * RowHeight + 16;

    public override bool Animating(PadSnapshot s) => !Paused && s.History.Count > 0 && s.History[0].EndNs is null;

    public override void Render(PadSnapshot s, Settings settings, ulong nowNs)
    {
        int n = settings.History.Rows;
        if (_built != n)
            Build(n);
        ShowStatus(s.Status == InputStatus.Running ? null : StatusOf(s));
        if (Paused)
        {
            _frozen ??= (s.History, nowNs); // スナップショットの行は写しで、行は書き換わらない記録なので、そのまま持てる
            s = s with { History = _frozen.Value.Rows };
            nowNs = _frozen.Value.NowNs;
        }
        _pausedText.Visibility = Paused ? Visibility.Visible : Visibility.Collapsed;
        var pressed = Draw.Pressed(settings);
        if (Paused && _pausedText.Foreground != pressed)
            _pausedText.Foreground = pressed;
        _empty.Visibility = s.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _current.Visibility = s.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var pad = s.Pad;
        var skin = Labels.ChooseSkin(pad, pad is null ? null : settings.Devices.GetValueOrDefault(pad.Guid));
        bool arrow = settings.History.DirectionStyle == "arrow";
        int frameBase = settings.History.FrameBase;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= s.History.Count)
            {
                row.Frames.Text = "";
                row.Arrow.Visibility = row.Dot.Visibility = row.Number.Visibility = row.Badge.Visibility = Visibility.Hidden;
                row.Buttons.SetRuns([]);
                row.Buttons.Tag = null;
                row.Gap.Text = "";
                continue;
            }
            var h = s.History[i];
            ulong end = h.EndNs ?? Math.Max(nowNs, h.StartNs);
            string frames = HistoryBuilder.FormatLength(end - h.StartNs, frameBase);
            if (row.Frames.Text != frames)
                row.Frames.Text = frames;
            // 一番上の行は、フレーム数だけが変わる(まとめた行は作り直されるので、ここで描き直しになる)
            string layout = LayoutKey(settings);
            if (i == 0 && h.EndNs is null && !IsNewRow(row, h) && (string?)row.Gap.Tag == layout)
                continue;
            row.Buttons.Tag = h;
            double? angle = Draw.AngleOf(h.Direction);
            row.Number.Text = h.Direction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.Number.Visibility = arrow ? Visibility.Hidden : Visibility.Visible;
            row.Arrow.Visibility = arrow && angle is not null ? Visibility.Visible : Visibility.Hidden;
            row.Dot.Visibility = arrow && angle is null ? Visibility.Visible : Visibility.Hidden;
            if (angle is double a)
                ((RotateTransform)row.Arrow.RenderTransform).Angle = a;

            // コマンドの印(FR-51): ボタンの前に、押したときの色の細い枠で囲んだ数字。矢印の設定でも数字で出す
            double x = ButtonsX;
            if (h.Motion is string m)
            {
                double w = Math.Round(7 + m.Length * BadgeCharWidth);
                row.BadgeText.Text = m;
                row.BadgeText.Width = w - 2;
                row.Badge.Width = w;
                if (row.Badge.BorderBrush != pressed)
                    row.Badge.BorderBrush = pressed;
                row.Badge.Visibility = Visibility.Visible;
                x += w + 4;
            }
            else
                row.Badge.Visibility = Visibility.Hidden;

            // 区切りは改行しない空白にする(狭いときに次の行へ折り返さず、末尾を「…」で切る。押しっぱなしの物が先に切れる)
            var newly = h.NewlyPressed.Select(k => Labels.For(k, skin, settings.LabelStyle, pad)).ToList();
            var runs = newly.Select(t => (Text: t, Brush: pressed))
                .Concat(h.Held.Select(k => (Text: $"({Labels.For(k, skin, settings.LabelStyle, pad)})", Brush: (Brush)Draw.Weak)))
                .Select((r, j) => (j == 0 ? r.Text : Nbsp + r.Text, r.Brush));

            // ずれ(FR-50): 右端に弱い小さい文字で。数は行の長さと同じ単位。文字の幅を測って右に寄せ、ボタンの欄をその分狭める。
            // 止めている間の一番上の行は、右上の「停止中」に重ならないように右端を空ける。
            // 新しく押したボタンが入らなくなるときは、ずれを出さない(ボタンを優先する)
            double right = PanelWidth - 6 - (Paused && i == 0 ? PausedTextWidth : 0);
            row.Gap.Text = "";
            if (h.MergeGapNs is ulong gap)
            {
                row.Gap.Text = HistoryBuilder.FormatGap(gap, frameBase).Replace(' ', Nbsp);
                row.Gap.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double gapLeft = right - row.Gap.DesiredSize.Width;
                if (TextWidth(string.Join(Nbsp, newly), row.Buttons.FontSize) <= gapLeft - 4 - x)
                {
                    Canvas.SetLeft(row.Gap, gapLeft);
                    right = gapLeft - 4;
                }
                else
                    row.Gap.Text = "";
            }
            row.Gap.Tag = layout;
            Canvas.SetLeft(row.Buttons, x);
            row.Buttons.Width = Math.Max(1, right - x);
            row.Buttons.SetRuns(runs);
        }
    }

    static bool IsNewRow(Row row, HistoryRow h) => !ReferenceEquals(row.Buttons.Tag, h);

    // ボタンの欄と同じ書体で、文字の幅を測る(OutlineText の縁取りの分 2 を足す)
    double TextWidth(string text, double size) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(Draw.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Draw.TextBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace + 2;

    // 一番上の行の形を決める設定。変えたら、行が同じでも描き直す(ずれの単位・印の色・停止中の文字の分の幅など)
    string LayoutKey(Settings s) => $"{s.History.FrameBase}|{s.History.DirectionStyle}|{s.LabelStyle}|{s.PressedColor}|{Paused}";

    void Build(int n)
    {
        _built = n;
        Height = HeightFor(n);
        Canvas.Height = Height;
        Canvas.Children.Clear();
        _rows.Clear();
        Canvas.Children.Add(_current.At(4, 8 + 1));
        for (int i = 0; i < n; i++)
        {
            double y = 8 + i * RowHeight;
            var badgeText = new OutlineText { FontFamily = Draw.Mono, FontSize = 12, Foreground = Draw.TextBrush, TextAlignment = TextAlignment.Center };
            var row = new Row
            {
                Frames = new OutlineText { FontFamily = Draw.Mono, FontSize = 15, Foreground = Draw.TextBrush, Width = 56, TextAlignment = TextAlignment.Right },
                Arrow = Draw.Arrow(16),
                Dot = new Ellipse { Width = 5, Height = 5, Fill = Draw.TextBrush },
                Number = new OutlineText { FontFamily = Draw.Mono, FontSize = 15, Foreground = Draw.TextBrush },
                BadgeText = badgeText,
                Badge = new Border
                {
                    Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.25), Background = Draw.Inset,
                    Child = badgeText, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden,
                    Padding = new Thickness(0, 0, 0, 1),
                },
                Buttons = new OutlineText { FontFamily = Draw.Ui, FontSize = 15, Foreground = Draw.TextBrush, Width = PanelWidth - ButtonsX - 6, TextTrimming = TextTrimming.CharacterEllipsis },
                Gap = new OutlineText { FontFamily = Draw.Ui, FontSize = 11.5, Foreground = Draw.Weak },
            };
            Canvas.Children.Add(row.Frames.At(4, y));
            Canvas.Children.Add(row.Arrow.At(78, y + 11));
            Canvas.Children.Add(row.Dot.At(76, y + 9));
            Canvas.Children.Add(row.Number.At(73, y));
            Canvas.Children.Add(row.Badge.At(ButtonsX, y + 3));
            Canvas.Children.Add(row.Buttons.At(ButtonsX, y));
            Canvas.Children.Add(row.Gap.At(PanelWidth - 6, y + 4));
            _rows.Add(row);
        }
        Canvas.Children.Add(_empty.At(60, Height / 2 - 10));
        Canvas.Children.Add(_pausedText.At(Width - 84, 2));
    }
}
