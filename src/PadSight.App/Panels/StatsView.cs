// O4 統計(画面設計書 §4.4・仕様書 FR-24)。今のセッションの長さ、入力の合計、直近 60 秒、60 秒の最大、上位 5 つを出す。
// 出す行は stats.rows で選ぶ。数の行と上位 5 つは、パッド表示の本体と同じ色の板にまとめ、上位の棒はくぼみの中に伸ばす。
// セッションが無いときは、前のセッションの長さと入力の数を弱い文字で出す。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class StatsView : PanelView
{
    const double Inner = 260 - 2 * 8 - 2 * 10; // 板の中の幅(パネルの余白 8、板の内側の余白 10)
    const double NameW = 46, CountW = 46, TrackW = Inner - NameW - CountW - 6;

    readonly StackPanel _stack = new() { Width = 260, Margin = new Thickness(0, 6, 0, 0) };
    readonly OutlineText _none = Draw.Text("", 12);
    string _builtRows = "";
    OutlineText? _session, _inputs, _last60, _peak, _peak1;
    readonly List<(OutlineText Name, Rectangle Track, Rectangle Bar, OutlineText Count)> _top = [];

    public StatsView() : base(260, 230) // 1 秒の最大の行(FR-37)を足したので、仕様書 §9.10.2 の 200 から広げた
    {
        Canvas.Children.Add(_stack);
        _none.Foreground = Draw.Weak;
        _none.Width = Inner;
    }

    public override bool Animating(PadSnapshot s) => s.Session.Active;

    public static string Duration(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    public override void Render(PadSnapshot s, Settings settings, ulong nowNs)
    {
        string rows = string.Join(",", settings.StatsRows);
        if (rows != _builtRows)
            Build(settings.StatsRows);
        ShowStatus(s.Status == InputStatus.Running ? null : StatusOf(s));
        var v = s.Session;
        var inv = CultureInfo.InvariantCulture;
        _none.Visibility = v.Active ? Visibility.Collapsed : Visibility.Visible;
        if (!v.Active)
        {
            _none.Text = "セッションはまだ始まっていません" + (v.PreviousDurationSeconds is long d
                ? $"\n前のセッション {Duration(TimeSpan.FromSeconds(d))}  入力 {v.PreviousInputs ?? 0:N0}" : "");
        }
        if (_session is not null) _session.Text = v.Active ? Duration(TimeSpan.FromSeconds((nowNs - Math.Min(nowNs, v.StartNs)) / (double)Values.NsPerSecond)) : "—";
        if (_inputs is not null) _inputs.Text = v.Active ? v.Inputs.ToString("N0", inv) : "—";
        if (_last60 is not null) _last60.Text = v.Active ? v.Last60.ToString("N0", inv) : "—";
        if (_peak is not null) _peak.Text = v.Active ? v.Peak.ToString("N0", inv) : "—";
        if (_peak1 is not null) _peak1.Text = v.Active ? v.Peak1s.ToString("N0", inv) : "—";
        var skin = Labels.ChooseSkin(s.Pad, s.Pad is null ? null : settings.Devices.GetValueOrDefault(s.Pad.Guid));
        long max = v.Top5.Count > 0 ? v.Top5[0].Value : 1;
        for (int i = 0; i < _top.Count; i++)
        {
            var (name, track, bar, count) = _top[i];
            bool has = v.Active && i < v.Top5.Count;
            name.Text = has ? Labels.For(v.Top5[i].Key, skin, settings.LabelStyle, s.Pad) : "";
            count.Text = has ? v.Top5[i].Value.ToString("N0", inv) : "";
            track.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            // 棒は丸い端が潰れないように、少しでもあれば高さ分(8)は出す
            bar.Width = has ? Math.Max(8, TrackW * v.Top5[i].Value / Math.Max(1, max)) : 0;
        }
    }

    void Build(IReadOnlyList<string> rows)
    {
        _builtRows = string.Join(",", rows);
        _stack.Children.Clear();
        _top.Clear();
        _session = _inputs = _last60 = _peak = _peak1 = null;
        (_none.Parent as Panel)?.Children.Remove(_none);
        StackPanel? plate = null, top = null;
        bool plateIsTop = false;
        foreach (var r in rows)
        {
            bool isTop = r == "top5";
            if (plate is null || plateIsTop != isTop)
            {
                plate = Plate();
                plateIsTop = isTop;
            }
            switch (r)
            {
                case "session": _session = Line(plate, "セッション"); break;
                case "inputs": _inputs = Line(plate, "入力"); break;
                case "last60": _last60 = Line(plate, "直近60秒"); break;
                case "peak": _peak = Line(plate, "60秒の最大"); break;
                case "peak1s": _peak1 = Line(plate, "1秒の最大"); break;
                case "top5":
                    top = plate;
                    for (int i = 0; i < 5; i++)
                    {
                        var g = new Grid { Height = 19 };
                        var name = Draw.Text("", 13);
                        name.VerticalAlignment = VerticalAlignment.Center;
                        // 棒はくぼみ(地の色を半分ほど透かした溝)の中に伸ばす。1 位は強い線の色、ほかは弱い色
                        var track = new Rectangle
                        {
                            Width = TrackW, Height = 8, RadiusX = 4, RadiusY = 4, Fill = Draw.Inset,
                            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NameW, 0, 0, 0),
                        };
                        var bar = new Rectangle
                        {
                            Height = 8, RadiusX = 4, RadiusY = 4, Fill = i == 0 ? Draw.Line : Draw.Weak,
                            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NameW, 0, 0, 0),
                        };
                        var count = Draw.Text("", 13, mono: true);
                        count.HorizontalAlignment = HorizontalAlignment.Right;
                        count.VerticalAlignment = VerticalAlignment.Center;
                        g.Children.Add(name);
                        g.Children.Add(track);
                        g.Children.Add(bar);
                        g.Children.Add(count);
                        plate.Children.Add(g);
                        _top.Add((name, track, bar, count));
                    }
                    break;
            }
        }
        // 「セッションはまだ始まっていません」: 上位 5 つの板があれば、その間は空なので板の真ん中に重ね、無ければ板の下に置く
        if (top is not null)
        {
            _none.HorizontalAlignment = HorizontalAlignment.Center;
            _none.VerticalAlignment = VerticalAlignment.Center;
            _none.TextAlignment = TextAlignment.Center;
            _none.Margin = new Thickness(0);
            ((Grid)top.Parent).Children.Add(_none);
        }
        else
        {
            _none.TextAlignment = TextAlignment.Left;
            _none.Margin = new Thickness(18, 2, 18, 0);
            _stack.Children.Add(_none);
        }
    }

    /// <summary>本体の色の板(パッド表示の本体と同じ塗り)を 1 枚足し、その中の行の入れ物を返す。</summary>
    StackPanel Plate()
    {
        var inner = new StackPanel();
        _stack.Children.Add(new Border
        {
            Background = Draw.Body, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 8, 6), Child = new Grid { Children = { inner } },
        });
        return inner;
    }

    /// <summary>数の行。見出しは弱い色の小さめの字、値は文字の色の Consolas(桁をそろえる)。</summary>
    static OutlineText Line(StackPanel plate, string label)
    {
        var g = new Grid { Height = 20 };
        var name = Draw.Text(label, 13);
        name.Foreground = Draw.Weak;
        name.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(name);
        var value = Draw.Text("", 15, mono: true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(value);
        plate.Children.Add(g);
        return value;
    }
}
