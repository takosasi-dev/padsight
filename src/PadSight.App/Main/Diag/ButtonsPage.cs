// 診断の「ボタン」のページ(画面設計書 §4.7・仕様書 FR-55・FR-45)。ボタンの網羅、パドルの確認(機種が持つときだけ)、ボタンの検査を縦に並べる。
// 網羅とパドルは DiagSample から Core の ButtonCoverage・PaddleMapper が数え、ボタンの検査は InputEngine の ChatterCounter の数をスナップショットから出す。
// パッドか機種のプロファイルが替わったら網羅とパドルを初めからにする。結果は「この結果を残す」でだけ残す(INV-8)。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.App.Main.Diag;

public sealed class ButtonsPage : IDiagPage
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ボタンの網羅
    readonly TextBlock _remaining = Big();
    readonly TextBlock _progress = Ui.Weak();
    readonly ColumnDefinition _barDone = new(), _barLeft = new();
    readonly Border _barFill = new() { CornerRadius = new CornerRadius(3) };
    readonly Canvas _allDoneMark = Mark(CoverageMark.Done, 20);
    readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 10, 0, 2) };
    readonly StackPanel _coverageBody = new();
    readonly Border _coverageEmpty = Ui.Empty(Ui.Weak("パッドが見つかりません。つなぐと、機種のボタンがここに並びます"));
    readonly TextBlock _extendedNote = Ui.Weak();
    readonly TextBlock _triggerNote = Ui.Weak();
    readonly TextBlock _coverageSaved = Ui.Weak();
    readonly Button _coverageSave;
    readonly Dictionary<string, Tile> _tileByKey = [];

    // パドルの確認
    readonly Border _paddleCard;
    readonly WrapPanel _paddleTiles = new() { Margin = new Thickness(0, 8, 0, 2) };
    readonly TextBlock _paddleGuide = Ui.Text();
    readonly TextBlock _paddleStatus = Ui.Text();
    readonly Button _paddleBegin, _paddleNext, _paddleRetry, _paddleSave;
    readonly TextBlock _paddleSaved = Ui.Weak();
    readonly List<Tile> _paddleTileList = [];

    // ボタンの検査
    readonly Grid _chatter = new() { Margin = new Thickness(0, 8, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _chatterEmpty = Ui.Empty(Ui.Weak("まだ押していません。ボタンを押すと、ここに回数が出ます"));
    readonly TextBlock _chatterSummary = Ui.Text();
    readonly TextBlock _chatterSaved = Ui.Weak();
    readonly Button _chatterSave;

    ButtonCoverage? _coverage;
    PaddleMapper? _paddles;
    DiagContext? _last;
    string _setupKey = "", _coverageKey = "", _paddleKey = "", _chatterKey = "";
    bool _coverageComplete;
    List<(string Name, ChatterCount Count)> _chatterRows = [];

    public string Title => "ボタン";

    public FrameworkElement View { get; }

    /// <summary>網羅にまだ押していないボタンが残っているか、パドルを待っている間だけ。</summary>
    public bool WantsSamples => !_coverageComplete || _paddles?.State.Phase == PaddlePhase.Waiting;

    public ButtonsPage()
    {
        // ---- ボタンの網羅 ----
        _coverageSave = Ui.Button("この結果を残す", SaveCoverage);
        var redo = Ui.Button("やり直す", () => { _coverage?.Reset(); _coverageKey = ""; _coverageSaved.Text = ""; Refresh(); });
        redo.Margin = new Thickness(0);
        redo.VerticalAlignment = VerticalAlignment.Center;
        _allDoneMark.Margin = new Thickness(0, 0, 10, 0);
        _allDoneMark.VerticalAlignment = VerticalAlignment.Center;
        _progress.Margin = new Thickness(16, 0, 0, 0);
        _progress.VerticalAlignment = VerticalAlignment.Bottom;
        _progress.Padding = new Thickness(0, 0, 0, 3);
        Typography.SetNumeralAlignment(_progress, FontNumeralAlignment.Tabular);
        var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(redo, Dock.Right);
        head.Children.Add(redo);
        head.Children.Add(_allDoneMark);
        head.Children.Add(_remaining);
        head.Children.Add(_progress);

        var bar = new Grid { Height = 6, Margin = new Thickness(0, 10, 0, 0) };
        bar.ColumnDefinitions.Add(_barDone);
        bar.ColumnDefinitions.Add(_barLeft);
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        _barFill.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
        bar.Children.Add(_barFill);

        _coverageBody.Children.Add(head);
        _coverageBody.Children.Add(bar);
        _coverageBody.Children.Add(_tiles);
        var coverage = Ui.Card("ボタンの網羅",
            Ui.Text("全部のボタンを 1 回ずつ押して、離してください。押して離したボタンに印が付きます"),
            _coverageEmpty, _coverageBody, _extendedNote, _triggerNote,
            Ui.Horizontal(_coverageSave, _coverageSaved));

        // ---- パドルの確認 ----
        _paddleBegin = Ui.Button("確かめ始める", () => { _paddles?.Begin(); _paddleSaved.Text = ""; Refresh(); });
        _paddleNext = Ui.Button("次へ", () => { _paddles?.Next(); Refresh(); });
        _paddleRetry = Ui.Button("やり直す", () => { _paddles?.Retry(); Refresh(); });
        _paddleSave = Ui.Button("この結果を残す", SavePaddles);
        _paddleGuide.FontWeight = FontWeights.SemiBold;
        _paddleGuide.Margin = new Thickness(0, 10, 0, 2);
        _paddleStatus.MinHeight = 22;
        _paddleStatus.Margin = new Thickness(0, 0, 0, 4);
        Typography.SetNumeralAlignment(_paddleStatus, FontNumeralAlignment.Tabular);
        _paddleCard = Ui.Card("パドルの確認",
            Ui.Text("背面のパドルが、どのボタンとして届くかを 1 本ずつ確かめます"),
            _paddleTiles, _paddleGuide, _paddleStatus,
            Ui.Horizontal(_paddleBegin, _paddleNext, _paddleRetry),
            Ui.Weak("SDL がパドルを「パドル 1〜4」として出すか、ほかのボタンとして出すかは、実機ではまだ確かめていません。反応が無いときは、パドルの不調か割り当てが無いのかを、ここでは区別できません"),
            Ui.Horizontal(_paddleSave, _paddleSaved));
        _paddleCard.Visibility = Visibility.Collapsed;

        // ---- ボタンの検査(FR-45。前のスティック診断の節を写して作り直した) ----
        for (int i = 0; i < 3; i++)
            _chatter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _chatterSave = Ui.Button("この結果を残す", SaveChatter);
        _chatterSummary.Margin = new Thickness(0, 2, 0, 0);
        Typography.SetNumeralAlignment(_chatterSummary, FontNumeralAlignment.Tabular);
        var chatter = Ui.Card("ボタンの検査",
            Ui.Text("ボタンを 1 回ずつ押してください。1 回しか押していないのに 2 回に数えられたら、ボタンの接点がへたっているかもしれません"),
            _chatterEmpty, _chatter, _chatterSummary,
            Ui.Horizontal(Ui.Button("数え直す", () => App.Current.PostToInput(e => e.ResetChatter())), _chatterSave, _chatterSaved),
            Ui.Weak("離してから 15 ミリ秒未満で押されたときに数えます。0 回でも壊れていないとは限りません"));

        var page = Ui.Page(coverage, _paddleCard, chatter);
        page.Padding = new Thickness(0, 0, 12, 4); // 外枠がもう余白を持つので、上は左の一覧とそろえ、右はスクロールバーの分だけ空ける
        View = page;
        ShowNoPad();
    }

    public void Render(DiagContext c)
    {
        _last = c;
        if (c.Pad is not { } pad || c.Profile is not { } profile)
        {
            if (_setupKey.Length > 0)
            {
                _setupKey = "";
                _coverage = null;
                _paddles = null;
                ShowNoPad();
            }
            RenderChatter(c);
            return;
        }
        float threshold = (float)App.Current.Settings.TriggerThreshold;
        string setup = $"{pad.InstanceId}|{profile.Id}|{threshold.ToString(Inv)}";
        if (setup != _setupKey)
            Setup(setup, profile, threshold);
        // パドルは描画の時刻で数える(デモの DiagSample は Stopwatch の時刻で、NowNs と基準が違うことがあるため。窓の 100 ミリ秒は描画の間隔の粗さになる)
        foreach (var s in c.Samples)
        {
            _coverage!.Feed(s);
            _paddles!.Feed(c.NowNs, s.Buttons);
        }
        _paddles!.Tick(c.NowNs);
        Refresh();
    }

    /// <summary>途中のパドルの確認を止める(結果は出さない)。確かめ終えた結果と網羅は、離れても残す。</summary>
    public void Cancel()
    {
        if (_paddles?.State.Phase is not (null or PaddlePhase.Idle or PaddlePhase.Done))
            _paddles!.Reset();
        _paddleKey = "";
        if (_last is not null) Refresh();
    }

    void Setup(string key, PadProfile profile, float threshold)
    {
        _setupKey = key;
        _coverage = new ButtonCoverage(profile, threshold);
        _paddles = new PaddleMapper(profile.PaddleCount);
        _coverageKey = _paddleKey = "";
        _coverageSaved.Text = _paddleSaved.Text = "";
        _tiles.Children.Clear();
        _tileByKey.Clear();
        _paddleTiles.Children.Clear();
        _paddleTileList.Clear();
        for (int i = 1; i <= profile.PaddleCount; i++)
        {
            var t = new Tile($"パドル {i}", 132);
            _paddleTileList.Add(t);
            _paddleTiles.Children.Add(t.Root);
        }
        _paddleCard.Visibility = profile.PaddleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        _coverageBody.Visibility = Visibility.Visible;
        _coverageEmpty.Visibility = Visibility.Collapsed;
        Ui.SetText(_triggerNote, profile.Buttons.Any(b => b.Key.EndsWith("Trigger", StringComparison.Ordinal))
            ? $"トリガーは、パネルの設定の「トリガーを押したとみなす値」({threshold.ToString("0.00", Inv)})まで引くと、押したことにします"
            : "");
    }

    void ShowNoPad()
    {
        _tiles.Children.Clear();
        _tileByKey.Clear();
        _coverageBody.Visibility = Visibility.Collapsed;
        _coverageEmpty.Visibility = Visibility.Visible;
        Ui.SetText(_extendedNote, "");
        Ui.SetText(_triggerNote, "");
        _coverageSave.IsEnabled = false;
        _coverageComplete = true;
        _paddleCard.Visibility = Visibility.Collapsed;
        _coverageKey = _paddleKey = "";
    }

    void Refresh()
    {
        if (_last is not { } c)
            return;
        RenderCoverage(c);
        RenderPaddles(c);
        RenderChatter(c);
    }

    // ---------------- ボタンの網羅 ----------------

    void RenderCoverage(DiagContext c)
    {
        if (_coverage is null || c.Pad is null)
            return;
        var st = _coverage.State;
        string key = string.Join("|", st.Items.Select(i => i.Key + (int)i.Mark));
        if (key == _coverageKey)
            return;
        _coverageKey = key;
        _coverageComplete = st.AllDone; // 拡張(無いことがある)を待ち続けて、細かい値を積み続けない

        if (_tiles.Children.Count != st.Items.Count)
        {
            _tiles.Children.Clear();
            foreach (var i in st.Items)
            {
                if (!_tileByKey.TryGetValue(i.Key, out var t))
                    _tileByKey[i.Key] = t = new Tile(i.Unregistered ? PadLabel(i.Key, c) : i.Label, 124, i.Unregistered ? "未登録" : i.Extended ? "拡張" : null);
                _tiles.Children.Add(t.Root);
            }
        }
        foreach (var i in st.Items)
            _tileByKey[i.Key].Set(i.Mark, i.Mark switch { CoverageMark.Done => "反応しました", CoverageMark.Held => "離してください", _ => "まだ" });

        int required = st.Items.Count(i => !i.Extended);
        int requiredDone = required - st.Remaining;
        if (st.AllDone)
        {
            _remaining.Text = "全部のボタンが反応しました";
            _allDoneMark.Visibility = Visibility.Visible;
        }
        else
        {
            _remaining.Text = $"残り {st.Remaining.ToString(Inv)} 個";
            _allDoneMark.Visibility = Visibility.Collapsed;
        }
        _progress.Text = $"反応したボタン {st.Done.ToString(Inv)} / {st.Items.Count.ToString(Inv)}";
        SetBar(requiredDone, required - requiredDone);

        var missingExt = st.Items.Where(i => i.Extended && !i.Unregistered && i.Mark != CoverageMark.Done).ToList();
        var notes = new List<string>();
        if (missingExt.Count > 0)
            notes.Add($"「拡張」の{string.Join("・", missingExt.Select(i => i.Label))}は、機種や接続によって無いことがあります。反応しなくても「全部」に数えます");
        if (st.Items.Any(i => i.Unregistered))
            notes.Add("「未登録」は、この機種のボタンの一覧に無いボタンです。上の「機種」が合っているか確かめてください");
        Ui.SetText(_extendedNote, string.Join(Environment.NewLine, notes));
        _coverageSave.IsEnabled = st.Done > 0;
        _coverageSaved.Text = "";
    }

    void SetBar(int done, int left)
    {
        _barDone.Width = new GridLength(done, GridUnitType.Star);
        _barLeft.Width = new GridLength(left, GridUnitType.Star);
        _barFill.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void SaveCoverage()
    {
        if (_coverage is null || _last is not { Pad: { } pad, Profile: not null })
            return;
        var st = _coverage.State;
        var notDone = st.Items.Where(i => i.Mark != CoverageMark.Done).Select(i => i.Unregistered ? PadLabel(i.Key, _last) : i.Label).ToList();
        string verdict = st.AllDone
            ? "全部のボタンが反応しました" + (notDone.Count > 0 ? $"(拡張の{string.Join("・", notDone)}は反応していません。機種や接続によって無いことがあります)" : "")
            : $"反応を確かめられなかったボタン: {string.Join("・", notDone)}。押し忘れか、反応していない可能性があります";
        Save(_coverageSaved, new DiagRecord("buttons", "ボタンの網羅", DateTime.UtcNow, pad.Name, pad.Kind.ToString(),
            [new("反応したボタン", st.Done), new("ボタンの数", st.Items.Count), new("残り", st.Remaining)], verdict, [], App.Version));
    }

    // ---------------- パドルの確認 ----------------

    void RenderPaddles(DiagContext c)
    {
        if (_paddles is null || c.Profile is not { PaddleCount: > 0 })
            return;
        var st = _paddles.State;
        string key = $"{st.Phase}|{st.CurrentPaddle}|{(int)st.WaitedSeconds}|" + string.Join(";", st.Mappings.Select(m => m.PaddleIndex + ":" + string.Join(",", m.Arrived)));
        if (key == _paddleKey)
            return;
        _paddleKey = key;
        var p = st.Phase;
        _paddleBegin.Content = p == PaddlePhase.Done ? "もう一度確かめる" : p == PaddlePhase.Idle ? "確かめ始める" : "最初から";
        _paddleNext.IsEnabled = p is PaddlePhase.Captured or PaddlePhase.NoResponse or PaddlePhase.Invalid;
        _paddleRetry.IsEnabled = p is not (PaddlePhase.Idle or PaddlePhase.Done);
        _paddleSave.IsEnabled = p == PaddlePhase.Done;

        for (int i = 1; i <= _paddleTileList.Count; i++)
        {
            var tile = _paddleTileList[i - 1];
            var m = st.Mappings.FirstOrDefault(x => x.PaddleIndex == i);
            bool current = i == st.CurrentPaddle && p is not (PaddlePhase.Idle or PaddlePhase.Done);
            if (m is not null && !(current && p == PaddlePhase.Waiting))
                tile.Set(m.Arrived.Count > 0 ? CoverageMark.Done : CoverageMark.NotYet, m.Arrived.Count > 0 ? Arrived(m, c) : "反応なし", warn: m.Arrived.Count == 0);
            else if (current && p == PaddlePhase.Invalid)
                tile.Set(CoverageMark.NotYet, "重なりました", warn: true);
            else if (current)
                tile.Set(CoverageMark.Held, "押してください");
            else
                tile.Set(CoverageMark.NotYet, "まだ");
        }

        Ui.SetText(_paddleGuide, p switch
        {
            PaddlePhase.Idle => "[確かめ始める] を押すと、パドルを 1 本ずつ案内します",
            PaddlePhase.Done => "全部のパドルを確かめました",
            _ => $"パドル {st.CurrentPaddle} だけを押してください(ほかのボタンには触らないでください)",
        });
        var cur = st.Mappings.FirstOrDefault(m => m.PaddleIndex == st.CurrentPaddle);
        _paddleStatus.Text = p switch
        {
            PaddlePhase.Waiting => $"待っています {((int)st.WaitedSeconds).ToString(Inv)} 秒 / 10 秒",
            PaddlePhase.Captured => $"パドル {st.CurrentPaddle} は「{(cur is null ? "" : Arrived(cur, c))}」として届きました。[次へ] で進みます",
            PaddlePhase.NoResponse => "10 秒待っても反応がありませんでした。[やり直す] か [次へ] を押してください",
            PaddlePhase.Invalid => "ほかの入力と重なりました。パドルだけを押して、もう一度試してください",
            PaddlePhase.Done => string.Join("   ", st.Mappings.Select(m => PaddleLine(m, c))),
            _ => "",
        };
        if (p != PaddlePhase.Done) _paddleSaved.Text = "";
    }

    static string Arrived(PaddleMapping m, DiagContext c) => string.Join("・", m.Arrived.Select(b => PadLabel(b.ToString(), c)));

    /// <summary>「パドル 1 は A」「パドル 2 は反応なし」(矢印は字で書かない。§6)。</summary>
    static string PaddleLine(PaddleMapping m, DiagContext c) =>
        m.Arrived.Count > 0 ? $"パドル {m.PaddleIndex.ToString(Inv)} は「{Arrived(m, c)}」" : $"パドル {m.PaddleIndex.ToString(Inv)} は反応なし";

    void SavePaddles()
    {
        if (_paddles is null || _last is not { Pad: { } pad, Profile: not null })
            return;
        var st = _paddles.State;
        int responded = st.Mappings.Count(m => m.Arrived.Count > 0);
        string list = string.Join("、", st.Mappings.Select(m => PaddleLine(m, _last)));
        string verdict = responded == st.Mappings.Count
            ? list
            : list + "。反応が無いパドルは、不調か、割り当てが無い可能性があります";
        Save(_paddleSaved, new DiagRecord("paddles", "パドルの確認", DateTime.UtcNow, pad.Name, pad.Kind.ToString(),
            [new("パドルの数", st.Mappings.Count), new("反応したパドル", responded)], verdict, [], App.Version));
    }

    // ---------------- ボタンの検査 ----------------

    /// <summary>選んだパッドの押したことのあるボタンだけ。数が変わったときだけ作り直す。</summary>
    void RenderChatter(DiagContext c)
    {
        var rows = new List<(string Name, ChatterCount Count)>();
        if (c.Pad is { } pad && c.S.Chatter.TryGetValue(pad.InstanceId, out var counts))
        {
            var settings = App.Current.Settings;
            var skin = Labels.ChooseSkin(pad, settings.Devices.GetValueOrDefault(pad.Guid));
            rows.AddRange(counts.OrderBy(kv => kv.Key).Select(kv => (Labels.For(kv.Key.ToString(), skin, settings.LabelStyle, pad), kv.Value)));
        }
        string key = (c.Pad?.InstanceId.ToString(Inv) ?? "-") + "#" + string.Join("|", rows.Select(r => $"{r.Name}:{r.Count.Presses}:{r.Count.Short}"));
        if (key == _chatterKey)
            return;
        _chatterKey = key;
        _chatterRows = rows;
        _chatter.Children.Clear();
        _chatter.RowDefinitions.Clear();
        bool any = rows.Count > 0;
        _chatter.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _chatterEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ((TextBlock)_chatterEmpty.Child).Text = c.Pad is null ? "パッドが見つかりません" : "まだ押していません。ボタンを押すと、ここに回数が出ます";
        _chatterSave.IsEnabled = any;
        _chatterSaved.Text = "";
        int presses = rows.Sum(r => r.Count.Presses), shorts = rows.Sum(r => r.Count.Short);
        Ui.SetText(_chatterSummary, !any ? ""
            : shorts == 0 ? $"押した回数の合計 {presses.ToString(Inv)}。すぐにまた押されたボタンはありません"
            : $"押した回数の合計 {presses.ToString(Inv)}。すぐにまた押された回数の合計 {shorts.ToString(Inv)}({rows.Count(r => r.Count.Short > 0).ToString(Inv)} 個のボタン)");
        if (shorts > 0)
        {
            _chatterSummary.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            _chatterSummary.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            _chatterSummary.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _chatterSummary.FontWeight = FontWeights.Normal;
        }
        if (!any)
            return;
        AddChatterRow("ボタン", "押した回数", "すぐにまた押された回数", header: true, warn: false);
        foreach (var (name, n) in rows)
            AddChatterRow(name, n.Presses.ToString(Inv), n.Short.ToString(Inv), header: false, warn: n.Short >= 1);
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

    void SaveChatter()
    {
        if (_last is not { Pad: { } pad } || _chatterRows.Count == 0)
            return;
        var bad = _chatterRows.Where(r => r.Count.Short > 0).ToList();
        var values = new List<KeyValuePair<string, double>>
        {
            new("押した回数", _chatterRows.Sum(r => r.Count.Presses)),
            new("すぐにまた押された回数", bad.Sum(r => r.Count.Short)),
        };
        values.AddRange(bad.Select(r => new KeyValuePair<string, double>($"{r.Name} のすぐにまた押された回数", r.Count.Short)));
        string verdict = bad.Count == 0
            ? "離してから 15 ミリ秒未満の押し直しはありませんでした(0 回でも壊れていないとは限りません)"
            : $"{string.Join("・", bad.Select(r => r.Name))} で、離してから 15 ミリ秒未満の押し直しがありました。接点がへたっている可能性があります";
        Save(_chatterSaved, new DiagRecord("chatter", "ボタンの検査", DateTime.UtcNow, pad.Name, pad.Kind.ToString(),
            values, verdict, [], App.Version));
    }

    // ---------------- 共通 ----------------

    static void Save(TextBlock saved, DiagRecord r) =>
        saved.Text = App.Current.Diagnostics.Save(r) is null ? "残せませんでした(ログを確かめてください)" : "残しました";

    /// <summary>ボタンの名前。プロファイルにあればその名前、SDL のパドルは「パドル n(SDL)」、ほかは FR-18 の表記。</summary>
    static string PadLabel(string key, DiagContext c)
    {
        if (c.Profile?.Buttons.FirstOrDefault(b => b.Key == key) is { } pb)
            return pb.Label;
        if (Enum.TryParse<PadButton>(key, out var b) && PaddleMapper.IsPaddle(b))
            return $"{Labels.For(key, Skin.Xbox, "pad", null)}(SDL)";
        var settings = App.Current.Settings;
        return c.Pad is { } pad ? Labels.For(key, Labels.ChooseSkin(pad, settings.Devices.GetValueOrDefault(pad.Guid)), settings.LabelStyle, pad) : key;
    }

    static TextBlock Big()
    {
        var t = Ui.Text("—");
        t.FontSize = 22;
        t.FontWeight = FontWeights.SemiBold;
        t.TextWrapping = TextWrapping.NoWrap;
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>状態の印(色だけにしない): まだ = 空の丸、押している = 中の点、済み = チェックの線。注意 = 横の線。</summary>
    static Canvas Mark(CoverageMark mark, double size, bool warn = false)
    {
        var c = new Canvas { Width = size, Height = size };
        double k = size / 16;
        if (warn)
        {
            var dash = new Line { X1 = 3 * k, X2 = 13 * k, Y1 = 8 * k, Y2 = 8 * k, StrokeThickness = 2.2 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            dash.SetResourceReference(Shape.StrokeProperty, "WarningTextBrush");
            c.Children.Add(dash);
            return c;
        }
        switch (mark)
        {
            case CoverageMark.Done:
                var check = new Path
                {
                    Data = new PathGeometry([new PathFigure(new Point(2 * k, 8.5 * k), [new PolyLineSegment([new Point(6 * k, 12.5 * k), new Point(14 * k, 3.5 * k)], true)], false)]),
                    StrokeThickness = 2.4 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                };
                check.SetResourceReference(Shape.StrokeProperty, "SuccessBrush");
                c.Children.Add(check);
                break;
            case CoverageMark.Held:
                var ring = new Ellipse { Width = 14 * k, Height = 14 * k, StrokeThickness = 1.6 * k };
                ring.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                Canvas.SetLeft(ring, k); Canvas.SetTop(ring, k);
                var dot = new Ellipse { Width = 7 * k, Height = 7 * k };
                dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                Canvas.SetLeft(dot, 4.5 * k); Canvas.SetTop(dot, 4.5 * k);
                c.Children.Add(ring);
                c.Children.Add(dot);
                break;
            default:
                var empty = new Ellipse { Width = 13 * k, Height = 13 * k, StrokeThickness = 1.4 * k };
                empty.SetResourceReference(Shape.StrokeProperty, "SubTextBrush");
                Canvas.SetLeft(empty, 1.5 * k); Canvas.SetTop(empty, 1.5 * k);
                c.Children.Add(empty);
                break;
        }
        return c;
    }

    /// <summary>ボタン 1 つの札。まだ = 地の色に細い縁、押している = 強調色の太い縁、済み = 選んだ色の塗りに緑の太い縁とチェック。状態は文字でも出す。</summary>
    sealed class Tile
    {
        public readonly Border Root;
        readonly TextBlock _state = new() { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly ContentControl _mark = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
        string _stateKey = "";

        public Tile(string label, double width, string? tag = null)
        {
            var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; // 長い名前は折り返す(同じ行の札は WrapPanel が同じ高さに伸ばす)
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var top = new DockPanel();
            if (tag is not null)
            {
                var t = new TextBlock { Text = tag, FontSize = 11, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                t.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                DockPanel.SetDock(t, Dock.Right);
                top.Children.Add(t);
            }
            top.Children.Add(name);
            var bottom = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            DockPanel.SetDock(_mark, Dock.Left);
            bottom.Children.Add(_mark);
            bottom.Children.Add(_state);
            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(bottom);
            Root = new Border { Child = body, Width = width, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 8) };
            System.Windows.Automation.AutomationProperties.SetName(Root, label);
            Set(CoverageMark.NotYet, "まだ");
        }

        public void Set(CoverageMark mark, string state, bool warn = false)
        {
            string key = $"{mark}|{state}|{warn}";
            if (key == _stateKey)
                return;
            _stateKey = key;
            _state.Text = state;
            Root.ToolTip = state.Length > 12 ? state : null;
            _mark.Content = Mark(mark, 14, warn);
            System.Windows.Automation.AutomationProperties.SetHelpText(Root, state);
            // 縁が太い時は中の余白を減らし、文字が動かないようにする
            bool strong = mark != CoverageMark.NotYet || warn;
            Root.BorderThickness = new Thickness(strong ? 2 : 1);
            Root.Padding = strong ? new Thickness(10, 8, 10, 8) : new Thickness(11, 9, 11, 9);
            Root.SetResourceReference(Border.BorderBrushProperty, warn ? "WarningTextBrush" : mark switch
            {
                CoverageMark.Done => "SuccessBrush",
                CoverageMark.Held => "AccentBrush",
                _ => "BorderBrush",
            });
            Root.SetResourceReference(Border.BackgroundProperty, mark == CoverageMark.Done ? "SelectedBrush" : mark == CoverageMark.Held ? "SurfaceBrush" : "BackgroundBrush");
            _state.SetResourceReference(TextBlock.ForegroundProperty, warn ? "WarningTextBrush" : mark == CoverageMark.NotYet ? "SubTextBrush" : "TextBrush");
            _state.FontWeight = mark == CoverageMark.NotYet && !warn ? FontWeights.Normal : FontWeights.SemiBold;
        }
    }
}
