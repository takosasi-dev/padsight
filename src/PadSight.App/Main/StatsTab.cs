// S4 統計(画面設計書 §4.8・仕様書 FR-26〜FR-28・FR-38・FR-47)。期間の合計、セッションの表(新しい順)、パッドごとの通算とその絵、
// 今のセッションを区切る、GrindLog 向けの CSV の書き出し。週は月曜始まり、日の境目はローカルの 0 時、0 時をまたぐ物は始まった日に数える。
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PadSight.App.Panels;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class StatsTab : UserControl
{
    sealed record Row(string Start, string Length, string Inputs, string PerMin, string Peak, string Peak1, string Top);
    sealed record PadTotalRow(string Name, string Kind, string Inputs, string Top, string LastUsed, string Guid, PadTotal Total);

    readonly ComboBox _period;
    readonly Button _split, _export;
    readonly TextBlock _exportNote = Ui.Weak();
    readonly TextBlock _totals = Ui.Text();
    readonly TextBlock _bad = Ui.Weak();
    readonly TextBlock _done = Ui.Text();
    readonly Button _openFolder;
    readonly ListView _list = new() { MinHeight = 200 };
    readonly TextBlock _empty = Ui.Weak("この期間の記録はありません");
    readonly ListView _padList = new() { MinHeight = 80, VerticalAlignment = VerticalAlignment.Top }; // 横の絵の高さまで伸びないように
    readonly TextBlock _padEmpty = Ui.Weak("まだ記録がありません");
    // FR-47: 選んだパッドの絵(重ねるパネルとは別の PadView)。パネルの色で描くので、地はパネルの地の色にする
    readonly PadView _heat = new();
    readonly TextBlock _heatTop = Ui.Text();
    readonly TextBlock _heatNone = Ui.Weak("このパッドは絵がありません");
    readonly Border _heatBox;
    readonly StackPanel _heatPanel = new() { Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed };
    string? _selectedGuid;
    string _periodValue = "week";
    List<SessionRecord> _all = [];
    List<SessionRecord> _inPeriod = [];
    string? _lastExportDir;

    public StatsTab()
    {
        _period = Ui.Combo([("today", "今日"), ("week", "今週"), ("month", "今月"), ("30d", "直近 30 日"), ("all", "すべて")], _periodValue,
            v => { _periodValue = v; Refresh(); });
        _split = Ui.Button("今のセッションを区切る", () => App.Current.PostToInput(e => e.CloseSession()));
        _export = Ui.Button("GrindLog 向けに書き出す", Export);
        _openFolder = Ui.Button("フォルダを開く", () => { if (_lastExportDir is not null) ShellOpen.Folder(_lastExportDir); });
        _openFolder.Visibility = Visibility.Collapsed;
        var grid = new GridView();
        foreach (var (h, p, w) in new[] { ("始まり", nameof(Row.Start), 110.0), ("長さ", nameof(Row.Length), 80.0), ("入力", nameof(Row.Inputs), 80.0),
            ("1 分あたり", nameof(Row.PerMin), 90.0), ("60 秒の最大", nameof(Row.Peak), 90.0), ("1 秒の最大", nameof(Row.Peak1), 80.0), ("多いボタン", nameof(Row.Top), 260.0) })
            grid.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new Binding(p) });
        _list.View = grid;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Horizontal(Ui.Text("期間  "), _period, Ui.Text("   "), _split, _export));
        stack.Children.Add(_exportNote); // 長い文なので、ボタンの横ではなく次の行に折り返して出す
        stack.Children.Add(Ui.Horizontal(_done, _openFolder));
        _totals.FontSize = 15;
        _totals.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(_totals);
        stack.Children.Add(_list);
        stack.Children.Add(_empty);
        stack.Children.Add(_bad);
        stack.Children.Add(Ui.Section("直近 30 日"));
        stack.Children.Add(Ui.Row("見る値", Ui.Combo([("inputs", "入力の数"), ("minutes", "遊んだ時間(分)"), ("peak1s", "1 秒の最大")], _metric,
            v => { _metric = v; RefreshChart(); }), 80));
        stack.Children.Add(_chart);
        var padGrid = new GridView();
        foreach (var (h, p, w) in new[] { ("パッド", nameof(PadTotalRow.Name), 260.0), ("種類", nameof(PadTotalRow.Kind), 110.0), ("通算の入力", nameof(PadTotalRow.Inputs), 100.0),
            ("多いボタン", nameof(PadTotalRow.Top), 240.0), ("最後に使った日", nameof(PadTotalRow.LastUsed), 110.0) })
            padGrid.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new Binding(p) });
        _padList.View = padGrid;
        _padList.SelectionChanged += (_, _) => ShowHeat();
        _heatBox = new Border { Child = new Viewbox { Child = _heat, Width = 360, Height = 240 }, Background = Draw.Background, CornerRadius = new CornerRadius(8), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        _heatPanel.Children.Add(_heatTop);
        _heatPanel.Children.Add(_heatBox);
        _heatPanel.Children.Add(_heatNone);
        stack.Children.Add(Ui.Section("パッドごとの通算"));
        stack.Children.Add(Ui.Horizontal(_padList, _heatPanel)); // 表の右に絵。幅が足りなければ下へ折り返す
        stack.Children.Add(_padEmpty);
        Content = Ui.Scroll(stack);
        App.Current.Sessions.Appended += () => Dispatcher.BeginInvoke(Reload);
    }

    public void Reload()
    {
        var (records, bad) = App.Current.Sessions.ReadAll();
        _all = records;
        _bad.Text = bad > 0 ? $"読めなかった行: {bad}" : "";
        RefreshPads();
        RefreshChart();
        Refresh();
    }

    readonly Canvas _chart = new() { Width = 600, Height = 150, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    string _metric = "inputs";

    /// <summary>FR-41: 直近 30 日の日ごとの棒。期間の選択とは関係なく、いつも直近 30 日。</summary>
    void RefreshChart()
    {
        const double top = 20, height = 110, bar = 16, step = 20;
        var inv = CultureInfo.InvariantCulture;
        string Fmt(double v) => _metric == "inputs" ? v.ToString("N0", inv) : v.ToString("0", inv);
        var data = DailyChart.Build(_all, _metric, DateTime.UtcNow.ToLocalTime().Date, TimeZoneInfo.Local);
        double max = data.Max(d => d.Value);
        _chart.Children.Clear();
        _chart.Children.Add(Ui.Weak(max > 0 ? $"一番多い日: {Fmt(max)}" : "直近 30 日の記録はありません。遊ぶと日ごとの棒が出ます"));
        _chart.Height = max > 0 ? 150 : 28; // 記録が無いときは、空の枠と日付を出さない
        if (max <= 0)
            return;
        var baseLine = new System.Windows.Shapes.Rectangle { Width = step * data.Count, Height = 1 };
        baseLine.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BorderBrush");
        Canvas.SetTop(baseLine, top + height);
        _chart.Children.Add(baseLine);
        for (int i = 0; i < data.Count; i++)
        {
            var (day, v) = data[i];
            if (v > 0)
            {
                var r = new System.Windows.Shapes.Rectangle { Width = bar, Height = Math.Max(1, v / max * height), ToolTip = $"{day.ToString("MM/dd", inv)}: {Fmt(v)}" };
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
                Canvas.SetLeft(r, i * step);
                Canvas.SetTop(r, top + height - r.Height);
                _chart.Children.Add(r);
            }
            if ((data.Count - 1 - i) % 7 == 0) // 今日から 7 日ずつさかのぼって置く(左から数えると右端で「今日」と重なる)
            {
                var label = Ui.Weak(i == data.Count - 1 ? "今日" : day.ToString("MM/dd", inv));
                Canvas.SetLeft(label, i * step);
                Canvas.SetTop(label, top + height + 2);
                _chart.Children.Add(label);
            }
        }
    }

    public void Render(PadSnapshot s) => _split.IsEnabled = s.SessionActive;

    /// <summary>FR-38: パッドごとの通算を、入力の多い順に出す。期間には関係なく全部。</summary>
    void RefreshPads()
    {
        var inv = CultureInfo.InvariantCulture;
        var pads = App.Current.Sessions.ReadPadTotals().Pads.OrderByDescending(kv => kv.Value.Inputs).ToList();
        string? keep = _selectedGuid; // 表を作り直すと選択が外れるので、同じパッドを選び直す
        var rows = pads.Select(x => new PadTotalRow(
            x.Value.Name,
            Enum.TryParse<PadKind>(x.Value.Kind, out var k) ? HomeTab.KindName(k) : x.Value.Kind,
            x.Value.Inputs.ToString("N0", inv),
            string.Join(" / ", x.Value.Buttons.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(3)
                .Select(kv => $"{Labels.ForRecord(kv.Key, x.Value.Kind)} {kv.Value.ToString("N0", inv)}")),
            x.Value.LastUsedUtc.ToLocalTime().ToString("yyyy/MM/dd", inv),
            x.Key, x.Value)).ToList();
        _padList.ItemsSource = rows;
        _padList.SelectedItem = rows.FirstOrDefault(r => r.Guid == keep);
        _padEmpty.Visibility = pads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowHeat();
    }

    /// <summary>FR-47: 選んだパッドのスキンの絵に、ボタン別の通算を濃さで出す。スキンはパネルと同じ選び方(種類か、GUID ごとに手で選んだ物)。</summary>
    void ShowHeat()
    {
        var row = _padList.SelectedItem as PadTotalRow;
        _selectedGuid = row?.Guid;
        if (row is null)
        {
            _heatPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var settings = App.Current.Settings;
        var p = row.Total;
        var kind = Enum.TryParse<PadKind>(p.Kind, out var k) ? k : PadKind.Generic;
        // 保存した物には種類しか無いので、Labels.ForRecord と同じ形の仮の PadInfo を作る。
        // 持っているボタンは数えた鍵から決める(共有・タッチパッド・パドルは 1 回でも押していれば描く)
        var buttons = p.Buttons.Where(kv => kv.Value > 0).Select(kv => Enum.TryParse<PadButton>(kv.Key, out var b) ? (PadButton?)b : null)
            .OfType<PadButton>().ToHashSet();
        var pad = new PadInfo(0, row.Guid, p.Name, kind, buttons, kind != PadKind.Unmapped);
        var skin = Labels.ChooseSkin(pad, settings.Devices.GetValueOrDefault(row.Guid));
        var top = ButtonHeat.Top(p.Buttons);
        _heatTop.Text = top is { } t ? $"よく押すボタン: {Labels.For(t.Key, skin, settings.LabelStyle, pad)} {t.Count.ToString("N0", CultureInfo.InvariantCulture)}" : "";
        _heatTop.Visibility = top is null ? Visibility.Collapsed : Visibility.Visible;
        bool picture = skin != Skin.Unmapped;
        _heatBox.Visibility = picture ? Visibility.Visible : Visibility.Collapsed;
        _heatNone.Visibility = picture ? Visibility.Collapsed : Visibility.Visible;
        if (picture)
            _heat.ShowHeat(skin, pad, settings, ButtonHeat.Intensities(p.Buttons));
        _heatPanel.Visibility = Visibility.Visible;
    }

    void Refresh()
    {
        var today = DateTime.UtcNow.ToLocalTime().Date;
        DateTime? from = _periodValue switch
        {
            "today" => today,
            "week" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            "month" => new DateTime(today.Year, today.Month, 1),
            "30d" => today.AddDays(-29),
            _ => null,
        };
        _inPeriod = _all.Where(r => from is null || r.StartUtc.ToLocalTime() >= from).OrderByDescending(r => r.StartUtc).ToList();
        long secs = _inPeriod.Sum(r => r.DurationSeconds), inputs = _inPeriod.Sum(r => r.Inputs);
        var inv = CultureInfo.InvariantCulture;
        var t = TimeSpan.FromSeconds(secs);
        _totals.Text = $"合計 {(int)t.TotalHours} 時間 {t.Minutes} 分    入力 {inputs.ToString("N0", inv)}    1 分あたり {(secs > 0 ? (inputs / (secs / 60.0)).ToString("0.0", inv) : "—")}    セッション {_inPeriod.Count}";
        _list.ItemsSource = _inPeriod.Select(r => new Row(
            r.StartUtc.ToLocalTime().ToString("MM/dd HH:mm", inv),
            Length(r.DurationSeconds),
            r.Inputs.ToString("N0", inv),
            r.DurationSeconds > 0 ? (r.Inputs / (r.DurationSeconds / 60.0)).ToString("0.0", inv) : "—",
            r.PeakPerMinute.ToString(inv),
            r.PeakPerSecond.ToString(inv),
            string.Join(" / ", r.Buttons.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(3)
                .Select(kv => $"{Labels.ForRecord(kv.Key, r.PadKind)} {kv.Value}")))).ToList();
        _empty.Visibility = _inPeriod.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int n = _inPeriod.Count(GrindLogCsv.Exportable);
        _export.IsEnabled = n > 0;
        _exportNote.Text = n > 0 ? "" : "書き出せるセッションがありません(1 分以上のセッションだけを書き出します)";
    }

    static string Length(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    void Export()
    {
        var s = App.Current.Settings;
        int n = _inPeriod.Count(GrindLogCsv.Exportable), m = _inPeriod.Count - n;
        var dlg = new ExportWindow(s.GrindLogGame, s.GrindLogType, n, m) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;
        s.GrindLogGame = dlg.Game;
        s.GrindLogType = dlg.Kind;
        App.Current.SaveSettings();
        var save = new SaveFileDialog
        {
            FileName = $"PadSight_GrindLog_{DateTime.UtcNow.ToLocalTime():yyyyMMdd_HHmmss}.csv",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (save.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            // UTF-8(BOM 付き)・CRLF(§9.6.3)
            File.WriteAllText(save.FileName, GrindLogCsv.Build(_inPeriod, dlg.Game, dlg.Kind, TimeZoneInfo.Local), new UTF8Encoding(true));
            _lastExportDir = Path.GetDirectoryName(save.FileName);
            _done.Text = m > 0 ? $"書き出しました({n} 件。1 分に満たない {m} 件は除きました)" : $"書き出しました({n} 件)";
            _openFolder.Visibility = Visibility.Visible;
            AppLog.Write($"GrindLog 向けに {n} 件を書き出した");
        }
        catch (IOException ex)
        {
            AppLog.Error("CSV の書き出し", ex);
            _done.Text = "書き出せませんでした: " + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("CSV の書き出し", ex);
            _done.Text = "書き出せませんでした: " + ex.Message;
        }
    }
}
