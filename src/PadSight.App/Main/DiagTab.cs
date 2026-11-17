// S3 スティック診断(画面設計書 §4.7・仕様書 §9.9・§9.18・FR-23・FR-45・FR-46)。手を離したときのずれ、外周の形、トリガーの範囲を測り、ボタンの検査の表を出す。
// 値は描画のたびにスナップショットから集め、計算は Core の StickDiagnostics と ChatterCounter が行う。結果は保存しない。Esc で止める。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class DiagTab : UserControl
{
    const double R = 100;
    enum Phase { Idle, DriftWait, Drift, Outer, Trigger }

    readonly ComboBox _pad = new() { MinWidth = 280 };
    readonly RadioButton _left = new() { Content = "左", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    readonly RadioButton _right = new() { Content = "右" };
    readonly Button _driftBtn, _outerBtn, _trigBtn;
    readonly Canvas _fig = new() { Width = 240, Height = 240, Background = Brushes.Transparent, ClipToBounds = true };
    readonly Ellipse _dot = new() { Width = 8, Height = 8, Fill = Brushes.White };
    readonly Polyline _outline = new() { Stroke = Brushes.Orange, StrokeThickness = 2 };
    readonly TextBlock _status = Ui.Text(); // 測っている間に文が変わるので、空でも 1 行分の高さを取っておく(図が上下に動かないように)
    readonly TextBlock _result = Ui.Text();
    readonly List<StickValue> _samples = [];
    readonly List<float> _lt = [], _rt = [];
    readonly Grid _chatter = new() { Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    string? _chatterKey;
    Phase _phase;
    DateTime _phaseEnd;
    string _padsKey = "";

    public DiagTab()
    {
        _driftBtn = Ui.Button("手を離して測る", StartDrift);
        _outerBtn = Ui.Button("外周を測る", StartOuter);
        _trigBtn = Ui.Button("トリガーを測る", StartTrigger);
        _fig.Children.Add(new Ellipse { Width = R * 2, Height = R * 2, Stroke = Brushes.Gray, StrokeThickness = 1 }.At2(120 - R, 120 - R));
        _fig.Children.Add(new Line { X1 = 20, X2 = 220, Y1 = 120, Y2 = 120, Stroke = Brushes.DimGray });
        _fig.Children.Add(new Line { X1 = 120, X2 = 120, Y1 = 20, Y2 = 220, Stroke = Brushes.DimGray });
        _fig.Children.Add(_outline);
        _fig.Children.Add(_dot);
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var fig = Ui.Picture(_fig);
        fig.Margin = new Thickness(0, 0, 24, 0);
        grid.Children.Add(fig);
        var res = new StackPanel();
        var resTitle = Ui.SubSection("結果");
        resTitle.Margin = new Thickness(0, 0, 0, 6);
        res.Children.Add(resTitle);
        _result.LineHeight = 24;
        Typography.SetNumeralAlignment(_result, FontNumeralAlignment.Tabular);
        res.Children.Add(_result);
        Grid.SetColumn(res, 1);
        grid.Children.Add(res);
        _status.MinHeight = 22;
        _status.Margin = new Thickness(0, 6, 0, 0);

        var padLabel = Ui.Text("パッド");
        padLabel.Margin = new Thickness(0, 0, 10, 0);
        var stickLabel = Ui.Text("スティック");
        stickLabel.Margin = new Thickness(24, 0, 12, 0);
        System.Windows.Automation.AutomationProperties.SetName(_pad, "パッド");
        var stick = Ui.Card(null,
            Ui.Horizontal(padLabel, _pad, stickLabel, _left, _right),
            Ui.Horizontal(_driftBtn, _outerBtn, _trigBtn),
            _status,
            grid,
            Ui.Weak("値は SDL を通した値で、ゲームが受け取る値と同じとは限りません"));
        for (int i = 0; i < 3; i++)
            _chatter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chatter = Ui.Card("ボタンの検査",
            Ui.Text("ボタンを 1 回ずつ押してください。1 回しか押していないのに 2 回に数えられたら、ボタンの接点がへたっているかもしれません"),
            _chatter,
            Ui.Horizontal(Ui.Button("数え直す", () => App.Current.PostToInput(e => e.ResetChatter()))),
            Ui.Weak("離してから 15 ミリ秒未満で押されたときに数えます。0 回でも壊れていないとは限りません"));
        Content = Ui.Page(stick, chatter);
        _pad.MinWidth = 300;
    }

    StickValue? Current(PadSnapshot s)
    {
        if (_pad.SelectedItem is not ComboBoxItem { Tag: uint id } || !s.Sticks.TryGetValue(id, out var st))
            return null;
        return _left.IsChecked == true ? st.Left : st.Right;
    }

    PadLive? SelectedLive(PadSnapshot s) =>
        _pad.SelectedItem is ComboBoxItem { Tag: uint id } && s.Live.TryGetValue(id, out var l) ? l : null;

    void StartDrift()
    {
        Cancel();
        _phase = Phase.DriftWait;
        _phaseEnd = DateTime.UtcNow.AddSeconds(3);
    }

    void StartOuter()
    {
        Cancel();
        _phase = Phase.Outer;
        _phaseEnd = DateTime.UtcNow.AddSeconds(10);
    }

    void StartTrigger()
    {
        Cancel();
        _phase = Phase.Trigger;
        _phaseEnd = DateTime.UtcNow.AddSeconds(10);
    }

    /// <summary>止めたら結果を出さない。</summary>
    public void Cancel()
    {
        _phase = Phase.Idle;
        _samples.Clear();
        _lt.Clear();
        _rt.Clear();
        _status.Text = "";
        _outline.Points = [];
    }

    public void Render(PadSnapshot s)
    {
        string key = string.Join("|", s.Pads.Select(p => p.InstanceId + p.Name));
        if (key != _padsKey)
        {
            _padsKey = key;
            object? sel = (_pad.SelectedItem as ComboBoxItem)?.Tag;
            _pad.Items.Clear();
            foreach (var p in s.Pads.Where(p => p.IsMapped))
                _pad.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.InstanceId });
            _pad.SelectedIndex = Math.Max(0, _pad.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => Equals(i.Tag, sel)));
        }
        RenderChatter(SelectedLive(s)?.Info, s);
        var v = Current(s);
        _driftBtn.IsEnabled = _outerBtn.IsEnabled = _trigBtn.IsEnabled = v is not null;
        if (v is not StickValue cur)
        {
            _status.Text = "パッドが見つかりません";
            _phase = Phase.Idle;
            return;
        }
        _dot.At2(120 + cur.X * R - 4, 120 - cur.Y * R - 4);
        double left = Math.Ceiling((_phaseEnd - DateTime.UtcNow).TotalSeconds);
        var inv = CultureInfo.InvariantCulture;
        switch (_phase)
        {
            case Phase.Idle:
                if (_status.Text == "パッドが見つかりません") _status.Text = "";
                break;
            case Phase.DriftWait:
                _status.Text = $"スティックから手を離してください。{left} 秒後に測り始めます";
                if (left <= 0) { _phase = Phase.Drift; _phaseEnd = DateTime.UtcNow.AddSeconds(5); _samples.Clear(); }
                break;
            case Phase.Drift:
                _samples.Add(cur);
                _status.Text = $"測っています。そのまま触らないでください。残り {left} 秒";
                if (left <= 0)
                {
                    var d = StickDiagnostics.Drift(_samples);
                    double dz = App.Current.Settings.Deadzone;
                    _result.Text = $"平均のずれ {d.MeanMagnitude.ToString("0.000", inv)}(生の値 x={d.RawMeanX.ToString("+0;-0", inv)}、y={d.RawMeanY.ToString("+0;-0", inv)})\n"
                        + $"最大 {d.MaxMagnitude.ToString("0.000", inv)}\n"
                        + $"デッドゾーンの円({dz.ToString("0.00", inv)})より{(d.MaxMagnitude <= dz ? "内側" : "外側")}";
                    Cancel();
                }
                break;
            case Phase.Outer:
                _samples.Add(cur);
                _status.Text = $"スティックを外周に沿って、ゆっくり 3 周回してください。残り {left} 秒";
                var o = StickDiagnostics.Outer(_samples);
                DrawOutline(o.BinMax);
                if (left <= 0)
                {
                    _result.Text = o.Insufficient
                        ? "回し方が足りません。もう一度測ってください"
                        : $"平均 {o.Mean.ToString("0.000", inv)}、最小 {o.Min.ToString("0.000", inv)}、最大 {o.Max.ToString("0.000", inv)}、1.00 からのずれの平均 {o.DeviationPercent.ToString("0.0", inv)}%";
                    var keep = o.BinMax;
                    Cancel();
                    if (!o.Insufficient) DrawOutline(keep);
                }
                break;
            case Phase.Trigger when SelectedLive(s) is PadLive lv:
                _lt.Add(lv.LeftTrigger);
                _rt.Add(lv.RightTrigger);
                _status.Text = $"左右のトリガーを奥まで引いて、離してください。何回か繰り返してください。残り {left} 秒\n"
                    + $"左 {Pct(lv.LeftTrigger)}%   右 {Pct(lv.RightTrigger)}%";
                if (left <= 0)
                {
                    _result.Text = $"左: {Range(_lt)}\n右: {Range(_rt)}";
                    Cancel();
                }
                break;
        }
    }

    static string Pct(float v) => (v * 100f).ToString("0", CultureInfo.InvariantCulture);

    static string Range(List<float> samples)
    {
        var r = StickDiagnostics.TriggerRange(samples);
        return $"最小 {Pct(r.Min)}%、最大 {Pct(r.Max)}%({r.Verdict})";
    }

    /// <summary>ボタンの検査の表。選んだパッドの押したことのあるボタンだけ。数が変わったときだけ作り直す。</summary>
    void RenderChatter(PadInfo? pad, PadSnapshot s)
    {
        var settings = App.Current.Settings;
        var rows = new List<(string Name, ChatterCount Count)>();
        if (pad is not null && s.Chatter.TryGetValue(pad.InstanceId, out var counts))
        {
            var skin = Labels.ChooseSkin(pad, settings.Devices.GetValueOrDefault(pad.Guid));
            rows.AddRange(counts.OrderBy(kv => kv.Key).Select(kv => (Labels.For(kv.Key.ToString(), skin, settings.LabelStyle, pad), kv.Value)));
        }
        string key = string.Join("|", rows.Select(r => $"{r.Name}:{r.Count.Presses}:{r.Count.Short}"));
        if (key == _chatterKey)
            return;
        _chatterKey = key;
        _chatter.Children.Clear();
        _chatter.RowDefinitions.Clear();
        AddChatterRow("ボタン", "押した回数", "すぐにまた押された回数", header: true, warn: false);
        var inv = CultureInfo.InvariantCulture;
        foreach (var (name, c) in rows)
            AddChatterRow(name, c.Presses.ToString(inv), c.Short.ToString(inv), header: false, warn: c.Short >= 1);
    }

    /// <summary>表の 1 行。見出しは小さい弱い文字と下の線、数は右にそろえる。「すぐにまた押された回数」が 1 以上の行は、警告の色の文字に太字を足す(色だけにしない)。</summary>
    void AddChatterRow(string name, string presses, string shorts, bool header, bool warn)
    {
        int row = _chatter.RowDefinitions.Count;
        _chatter.RowDefinitions.Add(new RowDefinition());
        string[] cells = [name, presses, shorts];
        if (header)
        {
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
            line.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            Grid.SetColumnSpan(line, 3);
            _chatter.Children.Add(line);
        }
        for (int col = 0; col < 3; col++)
        {
            var t = header ? Ui.Weak(cells[col]) : Ui.Text(cells[col]);
            t.Margin = new Thickness(col == 0 ? 0 : 32, header ? 6 : 4, 0, header ? 6 : 4);
            if (col > 0)
            {
                t.HorizontalAlignment = HorizontalAlignment.Right;
                Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            }
            if (header) t.FontSize = 12.5;
            if (warn)
            {
                t.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
                t.FontWeight = FontWeights.SemiBold;
            }
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            _chatter.Children.Add(t);
        }
    }

    void DrawOutline(float[] bins)
    {
        var pts = new PointCollection();
        for (int i = 0; i <= bins.Length; i++)
        {
            float m = bins[i % bins.Length];
            if (float.IsNaN(m)) continue;
            double a = (i % bins.Length * 5 + 2.5) * Math.PI / 180;
            pts.Add(new Point(120 + Math.Cos(a) * m * R, 120 - Math.Sin(a) * m * R));
        }
        _outline.Points = pts;
    }
}

static class CanvasExt
{
    public static T At2<T>(this T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }
}
