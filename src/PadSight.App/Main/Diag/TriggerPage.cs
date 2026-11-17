// 診断の「トリガー」のページ(仕様書 FR-57・FR-46・§9.18、画面設計書 §4.7)。直近 5 秒の左右のトリガーの値の線と、10 秒でトリガーの範囲を測る。
// 線は DiagSample から Core の TriggerTrace に積み、描画のたびに決まった数の列に直して、作っておいた図形の点だけを書き換える(D-9)。
// 範囲の判定は StickDiagnostics.TriggerRange。結果は「この結果を残す」で記録に残す(Kind trigger。数と判定だけ)。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.App.Panels;
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.App.Main.Diag;

public sealed class TriggerPage : IDiagPage
{
    const int Columns = 240;
    const ulong WindowNs = 5 * Values.NsPerSecond;
    const ulong MeasureNs = 10 * Values.NsPerSecond;
    const double LaneH = 76, LaneGap = 30, Top = 10, PlotLeft = 52, ValueW = 70, ChartH = Top + LaneH * 2 + LaneGap + 30;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly Lane[] _lanes;
    readonly float[] _cols = new float[Columns];
    readonly Canvas _chart = new() { Height = ChartH, Background = Brushes.Transparent, ClipToBounds = true };
    readonly TextBlock _threshold = Ui.Weak();
    readonly TextBlock[] _timeLabels = new TextBlock[6];
    double _w = -1;

    readonly Button _measureBtn;
    readonly TextBlock _status = Ui.Text();
    readonly RangeRow _leftRange, _rightRange;
    readonly TextBlock _hint = Ui.Weak();
    readonly Button _keep;
    readonly TextBlock _kept = Ui.Weak();
    readonly List<float> _lt = [], _rt = [];
    bool _measuring;
    ulong _measureEnd;
    DiagRecord? _result;
    TriggerRangeResult? _l, _r;
    bool _start; // 押されたら次の描画で始める(始めの時刻に描画の時刻を使うため)

    uint? _padId;
    ulong _lastRenderNs;
    string _labelsKey = "";

    public TriggerPage()
    {
        _lanes = [new Lane(this, 0), new Lane(this, 1)];
        for (int i = 0; i < _timeLabels.Length; i++)
        {
            var t = Label(i == 5 ? "今" : $"-{5 - i} 秒", Draw.Weak, 11);
            _timeLabels[i] = t;
            _chart.Children.Add(t);
        }
        _chart.SizeChanged += (_, _) => Layout();
        var picture = Ui.Picture(_chart);
        picture.HorizontalAlignment = HorizontalAlignment.Stretch;
        picture.Padding = new Thickness(12, 10, 12, 6);
        picture.Margin = new Thickness(0, 4, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(picture, "直近 5 秒の左右のトリガーの値の線");

        var trace = Ui.Card("トリガーの動き",
            Ui.Text("左右のトリガーの値を、直近 5 秒の線で出します。引いた分だけ線が上がります"),
            picture,
            _threshold);

        _measureBtn = Ui.Button("トリガーを測る", () => _start = true);
        _leftRange = new RangeRow();
        _rightRange = new RangeRow();
        _status.MinHeight = 22; // 測っている間に文が変わるので、空でも 1 行分の高さを取っておく(下が上下に動かないように)
        _status.Margin = new Thickness(0, 6, 0, 4);
        Typography.SetNumeralAlignment(_status, FontNumeralAlignment.Tabular);
        _keep = Ui.Button("この結果を残す", Keep);
        _kept.Margin = new Thickness(4, 0, 0, 0);
        _hint.Margin = new Thickness(0, 6, 0, 2);
        var range = Ui.Card("トリガーの範囲",
            Ui.Text("[トリガーを測る] を押したら、10 秒の間に、左右のトリガーを奥まで引いて離すのを何回か繰り返してください。離したときに 0%、奥まで引いたときに 100% になるかを見ます"),
            Ui.Horizontal(_measureBtn),
            _status,
            _leftRange.View,
            _rightRange.View,
            _hint,
            Ui.Horizontal(_keep, _kept),
            Ui.Weak("値は SDL を通した値で、ゲームが受け取る値と同じとは限りません。判定は目安です"));
        View = PageScroll(trace, range);
        ShowResult();
    }

    public string Title => "トリガー";
    public FrameworkElement View { get; }
    public bool WantsSamples => true; // 線を描くので、このページが出ている間はずっと

    public void Render(DiagContext c)
    {
        var pad = c.Pad;
        if (pad?.InstanceId != _padId)
        {
            // パッドが替わった・外れた: 線と測っている途中の物を捨てる
            _padId = pad?.InstanceId;
            foreach (var l in _lanes) l.Trace.Reset();
            Cancel();
        }
        else if (c.NowNs > _lastRenderNs + Values.NsPerSecond / 2)
        {
            // ページを離れていた間は値が積まれていない(古い値のままの線を出さない)。今の値から描き直す
            foreach (var l in _lanes) l.Trace.Reset();
        }
        _lastRenderNs = c.NowNs;
        UpdateLabels(pad);
        _measureBtn.IsEnabled = pad is not null;

        float liveL = 0, liveR = 0;
        PadLive? lv = null;
        bool live = pad is not null && c.S.Live.TryGetValue(pad.InstanceId, out lv);
        if (live)
        {
            (liveL, liveR) = (lv!.LeftTrigger, lv.RightTrigger);
            // 引いたまま止めているトリガーからはイベントが来ないので、最初の 1 点は今の値から取る
            if (_lanes[0].Trace.Count == 0) _lanes[0].Trace.Add(c.NowNs, liveL);
            if (_lanes[1].Trace.Count == 0) _lanes[1].Trace.Add(c.NowNs, liveR);
        }
        foreach (var s in c.Samples)
        {
            _lanes[0].Trace.Feed(s);
            _lanes[1].Trace.Feed(s);
        }
        if (_w < 0) Layout();
        for (int i = 0; i < 2; i++)
        {
            _lanes[i].Trace.Resample(c.NowNs, WindowNs, _cols);
            _lanes[i].Plot(_cols, live);
        }

        if (_start)
        {
            _start = false;
            if (pad is not null) StartMeasure(c.NowNs, liveL, liveR, pad);
        }
        if (_measuring && pad is not null)
            Measure(c, liveL, liveR, pad);
        else if (pad is null)
            Ui.SetText(_status, "パッドが見つかりません");
        else if (_status.Text == "パッドが見つかりません")
            Ui.SetText(_status, "");
    }

    public void Cancel()
    {
        _start = false;
        if (!_measuring) return;
        _measuring = false;
        _lt.Clear();
        _rt.Clear();
        Ui.SetText(_status, "");
        ShowResult(); // 前の結果に戻す
    }

    void StartMeasure(ulong now, float l, float r, PadInfo pad)
    {
        _measuring = true;
        _measureEnd = now + MeasureNs;
        _lt.Clear();
        _rt.Clear();
        _lt.Add(l);
        _rt.Add(r);
        _hint.Visibility = Visibility.Collapsed;
        _keep.Visibility = Visibility.Collapsed;
        Ui.SetText(_kept, "");
    }

    void Measure(DiagContext c, float liveL, float liveR, PadInfo pad)
    {
        foreach (var s in c.Samples)
        {
            _lt.Add(s.LeftTrigger);
            _rt.Add(s.RightTrigger);
        }
        _lt.Add(liveL);
        _rt.Add(liveR);
        long left = _measureEnd > c.NowNs ? (long)Math.Ceiling((_measureEnd - c.NowNs) / 1e9) : 0;
        string text = $"左右のトリガーを奥まで引いて、離してください。何回か繰り返してください。残り {left} 秒";
        if (_status.Text != text) Ui.SetText(_status, text);
        var l = StickDiagnostics.TriggerRange(_lt);
        var r = StickDiagnostics.TriggerRange(_rt);
        _leftRange.Show(l, liveL, measuring: true);
        _rightRange.Show(r, liveR, measuring: true);
        if (left > 0)
            return;
        _measuring = false;
        Ui.SetText(_status, "");
        (_l, _r) = (l, r);
        _result = new DiagRecord("trigger", "トリガーの範囲", DateTime.UtcNow, pad.Name, pad.Kind.ToString(),
            [
                new("左の最小(%)", Math.Round(l.Min * 100, 1)), new("左の最大(%)", Math.Round(l.Max * 100, 1)),
                new("右の最小(%)", Math.Round(r.Min * 100, 1)), new("右の最大(%)", Math.Round(r.Max * 100, 1)),
            ],
            $"左: {l.Verdict} / 右: {r.Verdict}", [], App.Version);
        _keep.IsEnabled = true;
        Ui.SetText(_kept, "");
        ShowResult();
        _lt.Clear();
        _rt.Clear();
    }

    /// <summary>最後に測り終えた結果を出す(無ければ空の行)。</summary>
    void ShowResult()
    {
        _leftRange.Show(_l);
        _rightRange.Show(_r);
        _keep.Visibility = _result is null ? Visibility.Collapsed : Visibility.Visible;
        var hints = new List<string>();
        foreach (var (side, v) in new[] { ("左", _l), ("右", _r) })
            if (v is not null && Hint(v.Verdict) is { } h) hints.Add($"{side}: {h}");
        Ui.SetText(_hint, string.Join("\n", hints));
    }

    static string? Hint(string verdict) => verdict switch
    {
        "戻りきっていません" => "離しても 0% まで戻っていない可能性があります。ばねの弱りやごみの挟まり、パッドの補正のずれが考えられます",
        "奥まで届いていません" => "奥まで引いても 100% に届いていない可能性があります。引きしろを短くするスイッチ(トリガーストップ)が入っていないかも確かめてください",
        "引き方が足りません" => "動いた幅が小さすぎて判定できません。奥まで引いて離すのを繰り返して、もう一度測ってください",
        _ => null,
    };

    void Keep()
    {
        if (_result is null) return;
        bool ok = App.Current.Diagnostics.Save(_result) is not null;
        Ui.SetText(_kept, ok ? "残しました" : "残せませんでした(ログを見てください)");
        _keep.IsEnabled = !ok;
    }

    void UpdateLabels(PadInfo? pad)
    {
        var settings = App.Current.Settings;
        string key = $"{pad?.InstanceId}|{settings.LabelStyle}|{settings.TriggerThreshold}|{(pad is null ? "" : settings.Devices.GetValueOrDefault(pad.Guid))}";
        if (key == _labelsKey) return;
        _labelsKey = key;
        string lt = "LT", rt = "RT";
        if (pad is not null)
        {
            var skin = Labels.ChooseSkin(pad, settings.Devices.GetValueOrDefault(pad.Guid));
            lt = Labels.For("LeftTrigger", skin, settings.LabelStyle, pad);
            rt = Labels.For("RightTrigger", skin, settings.LabelStyle, pad);
        }
        _lanes[0].Name.Text = lt;
        _lanes[1].Name.Text = rt;
        _leftRange.Name.Text = $"左({lt})";
        _rightRange.Name.Text = $"右({rt})";
        _threshold.Text = $"点線は、押したとみなす値({(settings.TriggerThreshold * 100).ToString("0", Inv)}%。設定の「トリガーのしきい値」)です。線が点線を越えると、入力履歴にトリガーが出ます";
        if (_w >= 0) Layout();
    }

    /// <summary>幅が変わったときだけ、線以外の図形(目盛り・点線・文字)を置き直す。</summary>
    void Layout()
    {
        _w = _chart.ActualWidth > 0 ? _chart.ActualWidth : 560;
        double plotR = _w - ValueW;
        float th = (float)App.Current.Settings.TriggerThreshold;
        foreach (var lane in _lanes) lane.Layout(plotR, th);
        double y = Top + LaneH * 2 + LaneGap + 8;
        for (int i = 0; i < _timeLabels.Length; i++)
        {
            var t = _timeLabels[i];
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = PlotLeft + (plotR - PlotLeft) * i / 5 - t.DesiredSize.Width / 2;
            Canvas.SetLeft(t, Math.Min(x, plotR - t.DesiredSize.Width));
            Canvas.SetTop(t, y);
        }
    }

    static TextBlock Label(string text, Brush brush, double size) => new()
    {
        Text = text, Foreground = brush, FontSize = size, FontFamily = Draw.Ui,
    };

    /// <summary>1 本のトリガーの帯: 目盛り・点線・塗り・線・名前・今の値。図形は最初に作り、点の位置だけを書き換える。</summary>
    sealed class Lane
    {
        public readonly TriggerTrace Trace;
        public readonly TextBlock Name;
        readonly TextBlock _value;
        readonly Polyline _line = new() { Stroke = Draw.Line, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        readonly Polygon _fill = new() { Fill = Draw.Body };
        readonly PointCollection _pts = [], _fillPts = [];
        readonly Line[] _grid = new Line[3];
        readonly Line _th = new() { Stroke = Draw.Weak, StrokeThickness = 1.2, StrokeDashArray = [4, 3] };
        readonly TextBlock[] _scale = new TextBlock[2];
        readonly double _top;
        double _plotR = 500;
        string _valueText = "";

        public Lane(TriggerPage page, int index)
        {
            Trace = new TriggerTrace(right: index == 1);
            _top = Top + index * (LaneH + LaneGap);
            for (int i = 0; i < Columns; i++) _pts.Add(default);
            for (int i = 0; i < Columns + 2; i++) _fillPts.Add(default);
            _line.Points = _pts;
            _fill.Points = _fillPts;
            var c = page._chart;
            for (int i = 0; i < 3; i++)
            {
                _grid[i] = new Line { Stroke = Draw.Faint, StrokeThickness = i == 0 ? 1 : 0.8 };
                c.Children.Add(_grid[i]);
            }
            c.Children.Add(_th);
            c.Children.Add(_fill);
            c.Children.Add(_line);
            Name = Label("", Draw.TextBrush, 13);
            Name.FontWeight = FontWeights.SemiBold;
            c.Children.Add(Name);
            _scale[0] = Label("100%", Draw.Weak, 10);
            _scale[1] = Label("0%", Draw.Weak, 10);
            c.Children.Add(_scale[0]);
            c.Children.Add(_scale[1]);
            _value = Label("—", Draw.TextBrush, 22);
            _value.FontFamily = Draw.Mono;
            _value.FontWeight = FontWeights.SemiBold;
            _value.TextAlignment = TextAlignment.Right;
            _value.Width = ValueW - 6;
            c.Children.Add(_value);
        }

        double Y(float v) => _top + LaneH - Math.Clamp(v, 0f, 1f) * LaneH;

        public void Layout(double plotR, float threshold)
        {
            _plotR = plotR;
            for (int i = 0; i < 3; i++)
            {
                double y = _top + LaneH * (1 - i / 2.0); // 0%・50%・100%
                (_grid[i].X1, _grid[i].X2, _grid[i].Y1, _grid[i].Y2) = (PlotLeft, plotR, y, y);
            }
            double ty = Y(threshold);
            (_th.X1, _th.X2, _th.Y1, _th.Y2) = (PlotLeft, plotR, ty, ty);
            Canvas.SetLeft(Name, 0);
            Canvas.SetTop(Name, _top + LaneH / 2 - 10);
            Canvas.SetLeft(_scale[0], PlotLeft - 34);
            Canvas.SetTop(_scale[0], _top - 7);
            Canvas.SetLeft(_scale[1], PlotLeft - 22);
            Canvas.SetTop(_scale[1], _top + LaneH - 8);
            Canvas.SetLeft(_value, plotR + 6);
            Canvas.SetTop(_value, _top + LaneH / 2 - 15);
        }

        public void Plot(float[] cols, bool live)
        {
            int first = Array.FindIndex(cols, v => !float.IsNaN(v));
            bool any = live && first >= 0;
            var vis = any ? Visibility.Visible : Visibility.Hidden;
            if (_line.Visibility != vis) _line.Visibility = _fill.Visibility = vis;
            string text = live ? (Trace.Current * 100).ToString("0", Inv) + "%" : "—";
            if (text != _valueText) _value.Text = _valueText = text;
            if (!any) return;
            double step = (_plotR - PlotLeft) / (Columns - 1);
            double bottom = _top + LaneH;
            for (int i = 0; i < Columns; i++)
            {
                // 最初の値より前は、最初の点に重ねる(線を出さない)
                int k = Math.Max(i, first);
                var p = new Point(PlotLeft + k * step, Y(cols[k]));
                if (_pts[i] != p) _pts[i] = p;
                if (_fillPts[i] != p) _fillPts[i] = p;
            }
            var br = new Point(_plotR, bottom);
            var bl = new Point(PlotLeft + first * step, bottom);
            if (_fillPts[Columns] != br) _fillPts[Columns] = br;
            if (_fillPts[Columns + 1] != bl) _fillPts[Columns + 1] = bl;
        }
    }

    /// <summary>範囲の 1 行: 名前、0〜100% の帯に最小〜最大の幅、数と判定。</summary>
    sealed class RangeRow
    {
        const double BarW = 240;
        public readonly TextBlock Name = Ui.Text();
        readonly Border _span = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Height = 10 };
        readonly Border _now = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 2, Height = 18 };
        readonly TextBlock _text = Ui.Text();
        public readonly FrameworkElement View;

        public RangeRow()
        {
            Name.Width = 80;
            Name.FontWeight = FontWeights.SemiBold;
            _span.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _now.SetResourceReference(Border.BackgroundProperty, "TextBrush");
            var track = new Border { Height = 10, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
            track.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            track.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var bar = new Grid { Width = BarW, Height = 18, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(track);
            bar.Children.Add(_span);
            bar.Children.Add(_now);
            Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular);
            View = Ui.Horizontal(Name, bar, _text);
            View.Margin = new Thickness(0, 3, 0, 3);
        }

        public void Show(TriggerRangeResult? r, float now = 0, bool measuring = false)
        {
            if (r is null)
            {
                _span.Visibility = _now.Visibility = Visibility.Hidden;
                _text.Text = "—";
                _text.FontWeight = FontWeights.Normal;
                _text.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                return;
            }
            _span.Visibility = Visibility.Visible;
            _span.Margin = new Thickness(r.Min * BarW, 0, 0, 0);
            _span.Width = Math.Max(3, (r.Max - r.Min) * BarW);
            _now.Visibility = measuring ? Visibility.Visible : Visibility.Hidden;
            _now.Margin = new Thickness(Math.Clamp(now * BarW - 1, 0, BarW - 2), 0, 0, 0);
            string range = $"最小 {Pct(r.Min)}%、最大 {Pct(r.Max)}%";
            _text.Text = measuring ? range : $"{range}({r.Verdict})";
            bool warn = !measuring && r.Verdict != "問題なし";
            _text.FontWeight = warn ? FontWeights.SemiBold : FontWeights.Normal;
            _text.SetResourceReference(TextBlock.ForegroundProperty, warn ? "WarningTextBrush" : "TextBrush");
        }

        static string Pct(float v) => (v * 100f).ToString("0", Inv);
    }

    /// <summary>ページの中身。縦にスクロールし、外枠の余白があるので左右の余白は付けない。</summary>
    internal static ScrollViewer PageScroll(params UIElement[] cards)
    {
        var sv = Ui.Page(cards);
        sv.Padding = new Thickness(0, 0, 8, 4);
        return sv;
    }
}
