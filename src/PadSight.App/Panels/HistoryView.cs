// O2 入力履歴(画面設計書 §4.2・仕様書 FR-19・FR-20・FR-50・FR-51)。新しい行を上に、フレーム数・方向・コマンドの印・ボタン・ずれを出す。
// 一番上の行のフレーム数は、次の行ができるまで画面の 1 フレームごとに増やす。行は変わったときだけ組み直す(D-9)。
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
    const double Top = 8;              // 一番上の行の上の余白
    const double DirX = 80, DirY = 12; // 方向のくぼみの中心(行の中の座標)
    const double DirSize = 18;
    const double ButtonsX = 100;       // ボタンの列の左端(印があれば印の右へずらす)
    const double PillHeight = 18;
    const char Nbsp = '\u00A0';        // 改行しない空白
    const double BadgeCharWidth = 6.6; // Consolas 12 の 1 文字の幅
    const double PausedWidth = 50;     // 「停止中」の札の幅
    const double OldestOpacity = 0.55; // 一番下の行の濃さ(上から 1.0 → 0.55)

    // 行の形を決める設定。変えたら、行が同じでも組み直す(参照と値の組で、毎フレーム作り直しても割り当てない)
    readonly record struct Layout(int FrameBase, bool Arrow, string LabelStyle, string PressedColor, bool Paused, Skin Skin, PadInfo? Pad);

    sealed class Row
    {
        public required Canvas Host;
        public required OutlineText Frames;
        public required Path Arrow;
        public required Ellipse Dot;
        public required OutlineText Number;
        public required Border Badge;
        public required OutlineText BadgeText;
        public required OutlineText Buttons;
        public required OutlineText Gap;
        public HistoryRow? Shown;
        public Layout Layout;
        /// <summary>今いる段(0 が一番上)。-1 はまだ置いていない</summary>
        public int Slot = -1;
        /// <summary>止めている間の一番上の段として組んだか(右端を「停止中」の分空ける)</summary>
        public bool TopPaused;
    }

    readonly List<Row> _rows = [];
    // 段ごとの行の部品(描くたびに使い回す。割り当てない)
    Row?[] _slots = [];
    readonly List<Row> _free = [];
    readonly OutlineText _empty;
    readonly Border _paused;
    readonly OutlineText _pausedText;
    // 一番上の行(今の行)の下に敷く、本体の色の帯
    readonly Rectangle _current = new() { RadiusX = 4, RadiusY = 4, Fill = Draw.Body, Width = PanelWidth - 8, Height = RowHeight - 2, Visibility = Visibility.Collapsed };
    int _built = -1;
    // 止めた時点の行と時刻。止めていなければ null
    (IReadOnlyList<HistoryRow> Rows, ulong NowNs)? _frozen;

    public HistoryView() : base(PanelWidth, 20 * RowHeight + 16)
    {
        _empty = new OutlineText
        {
            Text = "入力を待っています", FontFamily = Draw.Ui, FontSize = 13, Foreground = Draw.Weak,
            Width = PanelWidth, TextAlignment = TextAlignment.Center,
        };
        _pausedText = new OutlineText { Text = "停止中", FontFamily = Draw.Ui, FontSize = 12, TextAlignment = TextAlignment.Center, Width = PausedWidth - 3 };
        _paused = Pill(_pausedText);
        _paused.Width = PausedWidth;
        _paused.Visibility = Visibility.Collapsed;
    }

    /// <summary>FR-48。真にした後の最初の Render で、その時点の行を写して止める。</summary>
    public bool Paused
    {
        get => _isPaused;
        set { _isPaused = value; _frozen = null; }
    }
    bool _isPaused;

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
        var pressed = Draw.Pressed(settings);
        _paused.Visibility = Paused ? Visibility.Visible : Visibility.Collapsed;
        if (Paused && _paused.BorderBrush != pressed)
            _paused.BorderBrush = _pausedText.Foreground = pressed;
        _empty.Visibility = s.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _current.Visibility = s.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var pad = s.Pad;
        var skin = Labels.ChooseSkin(pad, pad is null ? null : settings.Devices.GetValueOrDefault(pad.Guid));
        var layout = new Layout(settings.History.FrameBase, settings.History.DirectionStyle == "arrow", settings.LabelStyle, settings.PressedColor, Paused, skin, pad);
        int count = Math.Min(_rows.Count, s.History.Count);
        Assign(s.History, count);
        for (int i = 0; i < count; i++)
        {
            var row = _slots[i]!;
            var h = s.History[i];
            Place(row, i);
            bool topPaused = Paused && i == 0;
            bool fresh = !ReferenceEquals(row.Shown, h) || row.Layout != layout || row.TopPaused != topPaused;
            // フレーム数は、組み直すときと、伸びている一番上の行だけ数え直す
            if (fresh || h.EndNs is null)
            {
                ulong end = h.EndNs ?? Math.Max(nowNs, h.StartNs);
                string frames = HistoryBuilder.FormatLength(end - h.StartNs, layout.FrameBase);
                if (row.Frames.Text != frames)
                    row.Frames.Text = frames;
            }
            if (!fresh)
                continue;
            row.Shown = h;
            row.Layout = layout;
            row.TopPaused = topPaused;
            row.Host.Visibility = Visibility.Visible;
            Fill(row, h, pressed);
        }
        foreach (var row in _free)
        {
            if (row.Shown is null)
                continue;
            row.Shown = null;
            row.Host.Visibility = Visibility.Hidden;
        }
    }

    /// <summary>行ごとに、すでにその行を出している部品を探して段に割り当てる。新しい行が上に足されたとき、20 行の中身を
    /// 1 段ずつ書き直すと 1 フレームに 10 ミリ秒を超えた(2026-11-18 に UiPerfProbe で測った)ので、部品ごと 1 段下げ、
    /// 新しい行には外れた部品を使う。一番上の行は終わると記録が作り直されるが、ボタンの一覧は同じ物を指している。</summary>
    void Assign(IReadOnlyList<HistoryRow> history, int count)
    {
        Array.Clear(_slots);
        _free.Clear();
        foreach (var row in _rows)
        {
            int at = -1;
            if (row.Shown is { } shown)
                for (int i = 0; i < count && at < 0; i++)
                    if (_slots[i] is null && Same(shown, history[i]))
                        at = i;
            if (at >= 0) _slots[at] = row;
            else _free.Add(row);
        }
        int next = 0;
        for (int i = 0; i < count; i++)
            if (_slots[i] is null)
                _slots[i] = _free[next++];
        _free.RemoveRange(0, next);

        static bool Same(HistoryRow a, HistoryRow b) =>
            ReferenceEquals(a, b) || a.StartNs == b.StartNs && ReferenceEquals(a.NewlyPressed, b.NewlyPressed) && ReferenceEquals(a.Held, b.Held);
    }

    void Place(Row row, int slot)
    {
        if (row.Slot == slot)
            return;
        row.Slot = slot;
        Canvas.SetTop(row.Host, Top + slot * RowHeight);
        // 古い行ほど薄く(上から 1.0 → 0.55)。行ごとに 1 つの入れ物にまとめて濃さをかける
        row.Host.Opacity = 1 - (1 - OldestOpacity) * slot / Math.Max(1, _rows.Count - 1);
    }

    // 1 行を組み直す。行が変わったか、行の形の設定が変わったときだけ呼ぶ
    void Fill(Row row, HistoryRow h, Brush pressed)
    {
        var (pad, skin, style) = (row.Layout.Pad, row.Layout.Skin, row.Layout.LabelStyle);
        bool arrow = row.Layout.Arrow;
        double? angle = Draw.AngleOf(h.Direction);
        row.Number.Text = h.Direction.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row.Number.Visibility = arrow ? Visibility.Hidden : Visibility.Visible;
        row.Arrow.Visibility = arrow && angle is not null ? Visibility.Visible : Visibility.Hidden;
        row.Dot.Visibility = arrow && angle is null ? Visibility.Visible : Visibility.Hidden;
        if (angle is double a)
            ((RotateTransform)row.Arrow.RenderTransform).Angle = a;

        // コマンドの印(FR-51): ボタンの前に、押したときの色の細い枠の札で数字を出す。矢印の設定でも数字
        double x = ButtonsX;
        if (h.Motion is string m)
        {
            double w = Math.Round(10 + m.Length * BadgeCharWidth);
            row.BadgeText.Text = m;
            row.BadgeText.Width = w - 3;
            row.Badge.Width = w;
            if (row.Badge.BorderBrush != pressed)
                row.Badge.BorderBrush = pressed;
            row.Badge.Visibility = Visibility.Visible;
            x += w + 4;
        }
        else
            row.Badge.Visibility = Visibility.Hidden;

        // 区切りは改行しない空白にする(狭いときに次の行へ折り返さず、末尾を「…」で切る。押しっぱなしの物が先に切れる)
        var newly = h.NewlyPressed.Select(k => Labels.For(k, skin, style, pad)).ToList();
        var runs = newly.Select(t => (Text: t, Brush: pressed))
            .Concat(h.Held.Select(k => (Text: $"({Labels.For(k, skin, style, pad)})", Brush: (Brush)Draw.Weak)))
            .Select((r, j) => (j == 0 ? r.Text : Nbsp + r.Text, r.Brush));

        // ずれ(FR-50): 右端に弱い小さい文字で。数は行の長さと同じ単位。文字の幅を測って右に寄せ、ボタンの欄をその分狭める。
        // 止めている間の一番上の行は、右上の「停止中」に重ならないように右端を空ける。
        // 新しく押したボタンが入らなくなるときは、ずれを出さない(ボタンを優先する)
        double right = PanelWidth - 6 - (row.TopPaused ? PausedWidth + 4 : 0);
        string gapText = "";
        if (h.MergeGapNs is ulong gap)
        {
            string t = HistoryBuilder.FormatGap(gap, row.Layout.FrameBase).Replace(' ', Nbsp);
            double gapLeft = right - TextWidth(t, row.Gap.FontSize);
            // 押しっぱなしの物が後ろに続くなら、切ったときの「…」の分も要る
            string need = string.Join(Nbsp, newly) + (h.Held.Count > 0 ? Nbsp + "…" : "");
            if (TextWidth(need, row.Buttons.FontSize) <= gapLeft - 4 - x)
            {
                gapText = t;
                Canvas.SetLeft(row.Gap, gapLeft);
                right = gapLeft - 4;
            }
        }
        row.Gap.Text = gapText;
        Canvas.SetLeft(row.Buttons, x);
        row.Buttons.Width = Math.Max(1, right - x);
        row.Buttons.SetRuns(runs);
    }

    // ボタンの欄と同じ書体で、文字の幅を測る(OutlineText の縁取りの分 2 を足す)
    double TextWidth(string text, double size) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(Draw.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Draw.TextBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace + 2;

    // くぼみの地に、押したときの色の細い枠の札(コマンドの印と「停止中」)
    static Border Pill(OutlineText text) => new()
    {
        Height = PillHeight, CornerRadius = new CornerRadius(PillHeight / 2), BorderThickness = new Thickness(1.25),
        Background = Draw.Inset, Child = text, Padding = new Thickness(0, 0, 0, 1),
    };

    void Build(int n)
    {
        _built = n;
        Height = HeightFor(n);
        Canvas.Height = Height;
        Canvas.Children.Clear();
        _rows.Clear();
        _slots = new Row?[n];
        Canvas.Children.Add(_current.At(4, Top + 1));
        for (int i = 0; i < n; i++)
        {
            var badgeText = new OutlineText { FontFamily = Draw.Mono, FontSize = 12, Foreground = Draw.TextBrush, TextAlignment = TextAlignment.Center };
            var badge = Pill(badgeText);
            badge.Visibility = Visibility.Hidden;
            var row = new Row
            {
                Host = new Canvas { Width = PanelWidth, Height = RowHeight, Visibility = Visibility.Hidden }, // 段と濃さは Place で決める
                Frames = new OutlineText { FontFamily = Draw.Mono, FontSize = 15, Foreground = Draw.TextBrush, Width = 56, TextAlignment = TextAlignment.Right },
                Arrow = Draw.Arrow(12),
                Dot = new Ellipse { Width = 5, Height = 5, Fill = Draw.TextBrush },
                Number = new OutlineText { FontFamily = Draw.Mono, FontSize = 13, Foreground = Draw.TextBrush, Width = DirSize, TextAlignment = TextAlignment.Center },
                BadgeText = badgeText,
                Badge = badge,
                Buttons = new OutlineText { FontFamily = Draw.Ui, FontSize = 15, Foreground = Draw.TextBrush, Width = PanelWidth - ButtonsX - 6, TextTrimming = TextTrimming.CharacterEllipsis },
                Gap = new OutlineText { FontFamily = Draw.Ui, FontSize = 11.5, Foreground = Draw.Weak },
            };
            var c = row.Host.Children;
            c.Add(row.Frames.At(4, 0));
            // フレーム数と方向の間の、ごく薄い区切りの線
            c.Add(new Rectangle { Width = 1, Height = 14, Fill = Draw.Faint }.At(65, 5));
            // 方向は小さな丸いくぼみの中に
            c.Add(new Ellipse { Width = DirSize, Height = DirSize, Fill = Draw.Inset }.At(DirX - DirSize / 2, DirY - DirSize / 2));
            c.Add(row.Arrow.At(DirX, DirY));
            c.Add(row.Dot.At(DirX - 2.5, DirY - 2.5));
            c.Add(row.Number.At(DirX - DirSize / 2 - 1, DirY - 10));
            c.Add(row.Badge.At(ButtonsX, (RowHeight - PillHeight) / 2));
            c.Add(row.Buttons.At(ButtonsX, 0));
            c.Add(row.Gap.At(PanelWidth - 6, 4));
            Canvas.Children.Add(row.Host.At(0, Top));
            _rows.Add(row);
        }
        Canvas.Children.Add(_empty.At(0, Height / 2 - 10));
        Canvas.Children.Add(_paused.At(PanelWidth - 6 - PausedWidth, Top + (RowHeight - PillHeight) / 2));
    }
}
