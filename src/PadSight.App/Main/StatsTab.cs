// S4 統計(画面設計書 §4.8・仕様書 FR-26〜FR-28)。期間の合計、セッションの表(新しい順)、
// 今のセッションを区切る、GrindLog 向けの CSV の書き出し。週は月曜始まり、日の境目はローカルの 0 時、0 時をまたぐ物は始まった日に数える。
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class StatsTab : UserControl
{
    sealed record Row(string Start, string Length, string Inputs, string PerMin, string Peak, string Peak1, string Top);
    sealed record PadTotalRow(string Name, string Kind, string Inputs, string Top, string LastUsed);

    readonly ComboBox _period;
    readonly Button _split, _export;
    readonly TextBlock _exportNote = Ui.Weak();
    readonly TextBlock _totals = Ui.Text();
    readonly TextBlock _bad = Ui.Weak();
    readonly TextBlock _done = Ui.Text();
    readonly Button _openFolder;
    readonly ListView _list = new() { MinHeight = 200 };
    readonly TextBlock _empty = Ui.Weak("この期間の記録はありません");
    readonly ListView _padList = new() { MinHeight = 80 };
    readonly TextBlock _padEmpty = Ui.Weak("まだ記録がありません");
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
        stack.Children.Add(Ui.Horizontal(Ui.Text("期間 "), _period, Ui.Text("   "), _split, _export, _exportNote));
        stack.Children.Add(Ui.Horizontal(_done, _openFolder));
        stack.Children.Add(_totals);
        stack.Children.Add(_list);
        stack.Children.Add(_empty);
        stack.Children.Add(_bad);
        var padGrid = new GridView();
        foreach (var (h, p, w) in new[] { ("パッド", nameof(PadTotalRow.Name), 260.0), ("種類", nameof(PadTotalRow.Kind), 110.0), ("通算の入力", nameof(PadTotalRow.Inputs), 100.0),
            ("多いボタン", nameof(PadTotalRow.Top), 240.0), ("最後に使った日", nameof(PadTotalRow.LastUsed), 110.0) })
            padGrid.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new Binding(p) });
        _padList.View = padGrid;
        stack.Children.Add(Ui.Section("パッドごとの通算"));
        stack.Children.Add(_padList);
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
        Refresh();
    }

    public void Render(PadSnapshot s) => _split.IsEnabled = s.SessionActive;

    /// <summary>FR-38: パッドごとの通算を、入力の多い順に出す。期間には関係なく全部。</summary>
    void RefreshPads()
    {
        var inv = CultureInfo.InvariantCulture;
        var pads = App.Current.Sessions.ReadPadTotals().Pads.Values.OrderByDescending(p => p.Inputs).ToList();
        _padList.ItemsSource = pads.Select(p => new PadTotalRow(
            p.Name,
            Enum.TryParse<PadKind>(p.Kind, out var k) ? HomeTab.KindName(k) : p.Kind,
            p.Inputs.ToString("N0", inv),
            string.Join(" / ", p.Buttons.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(3)
                .Select(kv => $"{Labels.ForRecord(kv.Key, p.Kind)} {kv.Value.ToString("N0", inv)}")),
            p.LastUsedUtc.ToLocalTime().ToString("yyyy/MM/dd", inv))).ToList();
        _padEmpty.Visibility = pads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
