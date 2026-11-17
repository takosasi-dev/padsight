// S3 スティック診断(画面設計書 §4.7・仕様書 §9.9・FR-23)。手を離したときのずれと、外周の形を測る。
// 値は描画のたびにスナップショットから集め、計算は Core の StickDiagnostics が行う。結果は保存しない。Esc で止める。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class DiagTab : UserControl
{
    const double R = 100;
    enum Phase { Idle, DriftWait, Drift, Outer }

    readonly ComboBox _pad = new() { MinWidth = 280 };
    readonly RadioButton _left = new() { Content = "左", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    readonly RadioButton _right = new() { Content = "右" };
    readonly Button _driftBtn, _outerBtn;
    readonly Canvas _fig = new() { Width = 240, Height = 240, Background = Brushes.Black, ClipToBounds = true };
    readonly Ellipse _dot = new() { Width = 8, Height = 8, Fill = Brushes.White };
    readonly Polyline _outline = new() { Stroke = Brushes.Orange, StrokeThickness = 2 };
    readonly TextBlock _status = Ui.Text();
    readonly TextBlock _result = Ui.Text();
    readonly List<StickValue> _samples = [];
    Phase _phase;
    DateTime _phaseEnd;
    string _padsKey = "";

    public DiagTab()
    {
        _driftBtn = Ui.Button("手を離して測る", StartDrift);
        _outerBtn = Ui.Button("外周を測る", StartOuter);
        _fig.Children.Add(new Ellipse { Width = R * 2, Height = R * 2, Stroke = Brushes.Gray, StrokeThickness = 1 }.At2(120 - R, 120 - R));
        _fig.Children.Add(new Line { X1 = 20, X2 = 220, Y1 = 120, Y2 = 120, Stroke = Brushes.DimGray });
        _fig.Children.Add(new Line { X1 = 120, X2 = 120, Y1 = 20, Y2 = 220, Stroke = Brushes.DimGray });
        _fig.Children.Add(_outline);
        _fig.Children.Add(_dot);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border { Child = _fig, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 16, 8) });
        var res = new StackPanel();
        res.Children.Add(Ui.Section("結果"));
        res.Children.Add(_result);
        Grid.SetColumn(res, 1);
        grid.Children.Add(res);
        var stack = new StackPanel();
        stack.Children.Add(Ui.Horizontal(Ui.Text("パッド "), _pad, Ui.Text("   スティック "), _left, _right));
        stack.Children.Add(Ui.Horizontal(_driftBtn, _outerBtn));
        stack.Children.Add(_status);
        stack.Children.Add(grid);
        stack.Children.Add(Ui.Weak("値は SDL を通した値で、ゲームが受け取る値と同じとは限りません"));
        Content = Ui.Scroll(stack);
    }

    StickValue? Current(PadSnapshot s)
    {
        if (_pad.SelectedItem is not ComboBoxItem { Tag: uint id } || !s.Sticks.TryGetValue(id, out var st))
            return null;
        return _left.IsChecked == true ? st.Left : st.Right;
    }

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

    /// <summary>止めたら結果を出さない。</summary>
    public void Cancel()
    {
        _phase = Phase.Idle;
        _samples.Clear();
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
        var v = Current(s);
        _driftBtn.IsEnabled = _outerBtn.IsEnabled = v is not null;
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
