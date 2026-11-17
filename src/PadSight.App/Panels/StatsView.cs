// O4 統計(画面設計書 §4.4・仕様書 FR-24)。今のセッションの長さ、入力の合計、直近 60 秒、60 秒の最大、上位 5 つを出す。
// 出す行は stats.rows で選ぶ。セッションが無いときは、前のセッションの長さと入力の数を弱い文字で出す。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class StatsView : PanelView
{
    readonly StackPanel _stack = new() { Margin = new Thickness(12, 8, 12, 8) };
    string _builtRows = "";
    OutlineText? _session, _inputs, _last60, _peak, _peak1, _none;
    readonly List<(OutlineText Name, Rectangle Bar, OutlineText Count)> _top = [];

    public StatsView() : base(260, 230) // 1 秒の最大の行(FR-37)を足したので、仕様書 §9.10.2 の 200 から広げた
    {
        Canvas.Children.Add(_stack);
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
        _none!.Visibility = v.Active ? Visibility.Collapsed : Visibility.Visible;
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
            var (name, bar, count) = _top[i];
            bool has = v.Active && i < v.Top5.Count;
            name.Text = has ? Labels.For(v.Top5[i].Key, skin, settings.LabelStyle, s.Pad) : "";
            count.Text = has ? v.Top5[i].Value.ToString("N0", inv) : "";
            bar.Width = has ? 110.0 * v.Top5[i].Value / Math.Max(1, max) : 0;
            bar.Fill = Draw.Line;
        }
    }

    void Build(IReadOnlyList<string> rows)
    {
        _builtRows = string.Join(",", rows);
        _stack.Children.Clear();
        _top.Clear();
        _session = _inputs = _last60 = _peak = _peak1 = null;
        _none = Draw.Text("", 12);
        _none.Foreground = Draw.Weak;
        _stack.Children.Add(_none);
        foreach (var r in rows)
        {
            switch (r)
            {
                case "session": _session = Line("セッション"); break;
                case "inputs": _inputs = Line("入力"); break;
                case "last60": _last60 = Line("直近60秒"); break;
                case "peak": _peak = Line("60秒の最大"); break;
                case "peak1s": _peak1 = Line("1秒の最大"); break;
                case "top5":
                    for (int i = 0; i < 5; i++)
                    {
                        var g = new Grid { Height = 20 };
                        var name = Draw.Text("", 13);
                        var bar = new Rectangle { Height = 9, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(56, 0, 0, 0) };
                        var count = Draw.Text("", 13, mono: true);
                        count.HorizontalAlignment = HorizontalAlignment.Right;
                        g.Children.Add(name);
                        g.Children.Add(bar);
                        g.Children.Add(count);
                        _stack.Children.Add(g);
                        _top.Add((name, bar, count));
                    }
                    break;
            }
        }
    }

    OutlineText Line(string label)
    {
        var g = new Grid { Height = 22 };
        g.Children.Add(Draw.Text(label, 14));
        var value = Draw.Text("", 14, mono: true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(value);
        g.Width = 236;
        _stack.Children.Add(g);
        return value;
    }
}
