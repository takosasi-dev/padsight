// S3 診断の「履歴」(画面設計書 §4.7・仕様書 FR-60)。残した結果を新しい順に並べ、選んだ物をレポートの画像(PNG)に書き出す・消す。
// 一覧はページを開いたとき(外枠が Cancel を呼んだ後の最初の描画)と消した後だけ読み直し、描画のたびにはディスクを読まない(D-9)。
// 種類を知らなくても出せるよう Title・Values・Verdict・Shape だけを使う。書き出した先のパスは画面にもログにも出さない(ユーザー名が入るため)。
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Win32;
using PadSight.Core;

namespace PadSight.App.Main.Diag;

public sealed class HistoryPage : IDiagPage
{
    readonly ListBox _list = new()
    {
        SelectionMode = SelectionMode.Extended, MaxHeight = 290, BorderThickness = new Thickness(0), Padding = new Thickness(0),
        Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(-8, 4, -8, 0),
    };
    readonly Border _emptyBox = Ui.Empty(Ui.Weak("まだ残した結果はありません\n各テストの結果の下にある「この結果を残す」で、ここに残せます"));
    readonly Button _export, _delete, _openExported;
    readonly TextBlock _done = Ui.Text();
    readonly TextBlock _hint = Ui.Weak();
    readonly Border _previewCard;
    readonly ContentControl _preview = new();
    bool _stale = true;
    string? _exportedDir;

    public string Title => "履歴";
    public FrameworkElement View { get; }
    public bool WantsSamples => false;

    public HistoryPage()
    {
        _export = Ui.Button("レポートの画像を書き出す", Export);
        _delete = Ui.Danger("消す", Delete);
        _delete.Margin = new Thickness(8, 2, 0, 2);
        _openExported = Ui.Button("書き出した場所を開く", () => { if (_exportedDir is not null) ShellOpen.Folder(_exportedDir); });
        var openDir = Ui.Button("記録のフォルダを開く", () => ShellOpen.Folder(App.Current.Diagnostics.Dir));
        System.Windows.Automation.AutomationProperties.SetName(_delete, "選んだ結果を消す");
        System.Windows.Automation.AutomationProperties.SetName(_list, "残した結果");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled); // 長い名前は「…」で切る(横に伸ばさない)
        _list.SelectionChanged += (_, _) => ShowSelection();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete && _list.SelectedItems.Count > 0) { Delete(); e.Handled = true; } };

        // 操作は左、戻せない「消す」は右に離す(画面設計書 §6)
        var bar = new DockPanel();
        DockPanel.SetDock(_delete, Dock.Right);
        bar.Children.Add(_delete);
        bar.Children.Add(Ui.Horizontal(_export, openDir));
        Ui.SetText(_done, "");
        _openExported.Visibility = Visibility.Collapsed;
        _hint.Margin = new Thickness(0, 8, 0, 0);

        var intro = Ui.Weak("各テストの「この結果を残す」で残した結果です。残しているのは数と判定だけで、時刻つきの入力は入っていません");
        intro.Margin = new Thickness(0, 0, 0, 8);
        var list = Ui.Card("残した結果", intro, bar, Ui.Horizontal(_done, _openExported), _list, _emptyBox, _hint);
        _previewCard = Ui.Card("レポートの見本", _preview);
        _previewCard.Visibility = Visibility.Collapsed;
        var stack = new StackPanel();
        stack.Children.Add(list);
        stack.Children.Add(_previewCard);
        View = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(0, 0, 4, 0) };
    }

    /// <summary>開いた後の最初の描画で 1 回だけ読み直す。</summary>
    public void Render(DiagContext c)
    {
        if (!_stale)
            return;
        _stale = false;
        Ui.SetText(_done, ""); // 前に開いたときの「書き出しました」は残さない
        _openExported.Visibility = Visibility.Collapsed;
        Reload();
    }

    /// <summary>外枠はページやタブを替えるたびに呼ぶ。次に開いたときに読み直す。</summary>
    public void Cancel() => _stale = true;

    sealed record Entry(string Path, DiagRecord Record);

    IReadOnlyList<Entry> Selected => [.. _list.SelectedItems.Cast<ListBoxItem>().Select(i => (Entry)i.Tag).OrderByDescending(e => e.Record.AtUtc)];

    void Reload()
    {
        var keep = Selected.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _list.Items.Clear();
        foreach (var (path, r) in App.Current.Diagnostics.List())
            _list.Items.Add(new ListBoxItem { Content = Row(r), Tag = new Entry(path, r) });
        bool any = _list.Items.Count > 0;
        _list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _emptyBox.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        foreach (ListBoxItem i in _list.Items)
            if (keep.Contains(((Entry)i.Tag).Path))
                _list.SelectedItems.Add(i);
        if (any && _list.SelectedItems.Count == 0)
            _list.SelectedIndex = 0;
        if (_list.SelectedItem is { } first)
            _list.ScrollIntoView(first);
        ShowSelection();
    }

    /// <summary>一覧の 1 行: 題と日時、パッドと主な数、判定。</summary>
    static StackPanel Row(DiagRecord r)
    {
        var title = Ui.Text(r.Title.Length > 0 ? r.Title : r.Kind);
        title.FontWeight = FontWeights.SemiBold;
        Trim(title);
        var when = Ui.Weak(r.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        when.Margin = new Thickness(12, 0, 0, 0);
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        var top = new DockPanel();
        DockPanel.SetDock(when, Dock.Right);
        top.Children.Add(when);
        top.Children.Add(title);

        var parts = new List<string>();
        if (r.PadName.Length > 0) parts.Add(r.PadName);
        parts.AddRange(r.Values.Take(3).Select(kv => $"{kv.Key} {ReportCard.Number(kv.Value)}"));
        var sub = Ui.Weak(string.Join("  ・  ", parts));
        sub.Margin = new Thickness(0, 1, 0, 0);
        Trim(sub);
        var s = new StackPanel();
        s.Children.Add(top);
        if (parts.Count > 0) s.Children.Add(sub);
        if (r.Verdict.Length > 0)
        {
            var v = Ui.Text(r.Verdict);
            v.Margin = new Thickness(0, 1, 0, 0);
            Trim(v);
            s.Children.Add(v);
        }
        return s;
    }

    static void Trim(TextBlock t)
    {
        t.TextWrapping = TextWrapping.NoWrap;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.ToolTip = t.Text;
    }

    void ShowSelection()
    {
        var sel = Selected;
        bool tooMany = sel.Count > ReportCard.MaxRecords;
        _export.IsEnabled = sel.Count > 0 && !tooMany;
        _delete.IsEnabled = sel.Count > 0;
        Ui.SetText(_hint, _list.Items.Count == 0 ? ""
            : tooMany ? $"1 枚にまとめられるのは {ReportCard.MaxRecords} 件までです(今は {sel.Count} 件を選んでいます)"
            : $"{_list.Items.Count} 件。Ctrl や Shift を押しながら選ぶと、{ReportCard.MaxRecords} 件まで 1 枚の画像にまとめられます");
        _previewCard.Visibility = sel.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _preview.Content = sel.Count > 0 ? ReportCard.Build([.. sel.Take(ReportCard.MaxRecords).Select(e => e.Record)]) : null;
    }

    void Export()
    {
        var sel = Selected;
        if (sel.Count == 0 || sel.Count > ReportCard.MaxRecords)
            return;
        var dlg = new SaveFileDialog
        {
            FileName = ReportCard.FileName([.. sel.Select(e => e.Record)]),
            Filter = "PNG 画像 (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
        };
        if (dlg.ShowDialog(Window.GetWindow(View)) == true)
            ExportTo(dlg.FileName);
    }

    /// <summary>選んだ結果を 1 枚の画像にして path に書く(ダイアログの後)。</summary>
    internal void ExportTo(string path)
    {
        var sel = Selected;
        try
        {
            ReportCard.SavePng([.. sel.Select(e => e.Record)], path);
            _exportedDir = Path.GetDirectoryName(path);
            Ui.SetText(_done, sel.Count == 1 ? $"レポートの画像を書き出しました({Path.GetFileName(path)})" : $"{sel.Count} 件を 1 枚の画像に書き出しました({Path.GetFileName(path)})");
            _openExported.Visibility = Visibility.Visible;
            AppLog.Write($"診断のレポートの画像を書き出した({sel.Count} 件)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("診断のレポートの書き出し", ex);
            Ui.SetText(_done, "書き出せませんでした。書き込める場所か、ほかのアプリが同じファイルを開いていないかを確かめてください");
            _openExported.Visibility = Visibility.Collapsed;
        }
    }

    void Delete()
    {
        var sel = Selected;
        if (sel.Count == 0)
            return;
        var r = sel[0].Record;
        string what = sel.Count == 1
            ? $"「{r.Title}」({r.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)})の結果を消します。元に戻せません"
            : $"選んだ {sel.Count} 件の結果を消します。元に戻せません";
        if (MessageBox.Show(Window.GetWindow(View), what, "PadSight", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            DeleteSelected();
    }

    /// <summary>選んだ結果を消して一覧を読み直す(確かめの後)。</summary>
    internal void DeleteSelected()
    {
        var sel = Selected;
        int failed = sel.Count(e => !App.Current.Diagnostics.Delete(e.Path));
        _list.SelectedItems.Clear();
        Reload();
        Ui.SetText(_done, failed > 0 ? $"消せなかった結果があります({failed} 件)" : $"{sel.Count} 件を消しました");
        _openExported.Visibility = Visibility.Collapsed;
    }
}
