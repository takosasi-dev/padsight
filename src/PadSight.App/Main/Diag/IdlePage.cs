// 診断の「放置監視」のページ(仕様書 FR-59、画面設計書 §4.7)。決めた時間(1〜60 分)パッドを置いておき、勝手な押下・スティックとトリガーの揺れ・切断と再接続を数える。
// 数えるのは Core の IdleMonitor。切断・再接続はスナップショットの Pads で見る(再接続で番号が変わるので、同じ GUID のパッドを探す)。
// 監視している間だけ DiagSample を受ける。ページやタブを離れると取り消す(IDiagPage.Cancel)。結果は「この結果を残す」で記録に残す(Kind idle。回数と最大のずれだけ)。
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.App.Main.Diag;

public sealed class IdlePage : IDiagPage
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record EventRow(string Time, string Kind, string Detail);

    readonly IdleMonitor _monitor = new();
    readonly Grid _minutesRow;
    readonly Button _startBtn, _stopBtn;
    readonly TextBlock _note = Ui.Text();
    readonly TextBlock _padWarn = Ui.Text();
    readonly StackPanel _live = new() { Margin = new Thickness(0, 10, 0, 4) };
    readonly TextBlock _remain = Ui.Text();
    readonly TextBlock _remainSub = Ui.Weak();
    readonly Border _barFill = new() { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(3), RenderTransform = new ScaleTransform(0, 1) };
    readonly TextBlock _drift, _buttons, _triggers, _disconnects;
    readonly TextBlock _summary = Ui.Text();
    readonly TextBlock _verdict = Ui.Text();
    readonly ObservableCollection<EventRow> _rows = [];
    readonly ListView _list = new() { MaxHeight = 300, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock _emptyText = Ui.Weak("まだ何も起きていません");
    readonly Border _empty;
    readonly TextBlock _capped = Ui.Weak();
    readonly Border _resultCard;
    readonly Button _keep;
    readonly TextBlock _kept = Ui.Weak();
    double _minutes = 10;
    int _shownEvents = -1;
    string _remainText = "";

    // 監視しているパッド(再接続で番号が変わるので、GUID でも探す)
    uint _padId;
    string _guid = "", _padName = "", _padKind = "";
    PadInfo? _padInfo;
    bool _gone;
    DiagRecord? _record;
    bool _startRequested; // 押されたら次の描画で始める(始めの時刻と位置に描画の時の物を使うため)

    public IdlePage()
    {
        _empty = Ui.Empty(_emptyText);
        _minutesRow = Ui.Slider("監視する時間", 1, 60, 1, _minutes, "0' 分'", v => _minutes = v);
        _startBtn = Ui.Button("始める", () => _startRequested = true);
        _stopBtn = Ui.Button("止める", Stop);
        _stopBtn.Visibility = Visibility.Collapsed;
        _note.Margin = new Thickness(0, 4, 0, 0);
        Ui.SetText(_note, "");
        _padWarn.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        Ui.SetText(_padWarn, "");

        _remain.FontSize = 40;
        _remain.FontWeight = FontWeights.SemiBold;
        _remain.FontFamily = new FontFamily("Consolas");
        Typography.SetNumeralAlignment(_remain, FontNumeralAlignment.Tabular);
        _remainSub.Margin = new Thickness(0);
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Child = _barFill, Margin = new Thickness(0, 6, 0, 4), Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        track.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        var remainLabel = Ui.Weak("残り");
        remainLabel.Margin = new Thickness(0);
        _live.Children.Add(remainLabel);
        _live.Children.Add(_remain);
        _live.Children.Add(track);
        _live.Children.Add(_remainSub);
        _live.Visibility = Visibility.Collapsed;

        var setup = Ui.Card("放置監視",
            Ui.Text("パッドに触らずに置いておくと、その間に勝手に入ったボタン・スティックとトリガーの揺れ・切断と再接続を記録します。机の上など、パッドが動かない所に置いてください"),
            _minutesRow,
            Ui.Horizontal(_startBtn, _stopBtn),
            _note,
            _padWarn,
            _live,
            Ui.Weak("監視中にこのページや診断のタブを離れる、Esc を押す、または PadSight の窓を閉じると、監視は取り消されます(結果は出ません)。終わるまで、このページを開いたままにしてください"));

        var tiles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        tiles.Children.Add(Ui.Stat("スティックの揺れ", out _drift));
        tiles.Children.Add(Ui.Stat("勝手な押下", out _buttons));
        tiles.Children.Add(Ui.Stat("トリガーの揺れ", out _triggers));
        tiles.Children.Add(Ui.Stat("切断・再接続", out _disconnects));
        var grid = new GridView();
        grid.Columns.Add(Ui.Column("時刻", nameof(EventRow.Time), 110, number: true));
        grid.Columns.Add(Ui.Column("種類", nameof(EventRow.Kind), 130));
        grid.Columns.Add(Ui.Column("内容", nameof(EventRow.Detail), 300));
        _list.View = grid;
        _list.ItemsSource = _rows;
        System.Windows.Automation.AutomationProperties.SetName(_list, "起きたことの一覧");
        _summary.Margin = new Thickness(0, 0, 0, 4);
        Typography.SetNumeralAlignment(_summary, FontNumeralAlignment.Tabular);
        _verdict.Margin = new Thickness(0, 2, 0, 6);
        _keep = Ui.Button("この結果を残す", Keep);
        _kept.Margin = new Thickness(4, 0, 0, 0);
        var listTitle = Ui.SubSection("起きたこと(時刻は始めてからの時間)");
        _resultCard = Ui.Card("結果",
            tiles,
            _summary,
            _verdict,
            listTitle,
            _list,
            _empty,
            _capped,
            Ui.Horizontal(_keep, _kept),
            Ui.Weak("スティックは始めた時の位置から 0.05、トリガーは 0.05 を越えたときに 1 回と数えます(越えている間は数え直しません)。報告の途切れは SDL からは見えないため数えません。判定は目安で、故障と決まったわけではありません"));
        Ui.SetText(_capped, "");
        View = TriggerPage.PageScroll(setup, _resultCard);
        ShowIdle();
    }

    public string Title => "放置監視";
    public FrameworkElement View { get; }
    public bool WantsSamples => _monitor.Running;

    public void Render(DiagContext c)
    {
        _startBtn.IsEnabled = c.Pad is not null;
        if (_startRequested)
        {
            _startRequested = false;
            if (c.Pad is not null) Start(c);
        }
        if (!_monitor.Running)
        {
            string why = c.Pad is null ? "パッドが見つかりません。パッドをつなぐと始められます" : "";
            if (_padWarn.Text != why) Ui.SetText(_padWarn, why);
            return;
        }
        var sum = _monitor.Summary;

        // 切断と再接続(スナップショットのパッドの一覧で見る)
        if (!_gone && !c.S.Pads.Any(p => p.InstanceId == _padId))
        {
            _gone = true;
            _monitor.NoteDisconnected(c.NowNs);
        }
        else if (_gone && c.S.Pads.FirstOrDefault(p => p.Guid == _guid && p.IsMapped) is { } back)
        {
            _gone = false;
            _padId = back.InstanceId;
            _padInfo = back;
            _monitor.NoteReconnected(c.NowNs);
        }
        foreach (var s in c.Samples)
            if (s.PadId == _padId) _monitor.Feed(s);
        _monitor.Advance(c.NowNs);

        // 上で別のパッドを選ぶと、監視しているパッドのサンプルが来ない
        bool blind = !_gone && c.Pad?.InstanceId != _padId;
        Ui.SetText(_padWarn, _gone ? $"{_padName} が外れています。つなぎ直すと、続けて見ます"
            : blind ? $"上で選んでいるパッドが、監視している {_padName} と違うため、入力が見えていません。上のパッドを {_padName} に戻してください"
            : "");

        sum = _monitor.Summary;
        UpdateCounts(sum);
        if (sum.Running)
        {
            double total = _monitor.Settings.DurationSeconds;
            var left = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(total - sum.ElapsedSeconds)));
            string text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            if (text != _remainText)
            {
                _remain.Text = _remainText = text;
                ((ScaleTransform)_barFill.RenderTransform).ScaleX = Math.Clamp(sum.ElapsedSeconds / total, 0, 1);
                _remainSub.Text = $"{(int)Math.Round(total / 60)} 分のうち {(int)(sum.ElapsedSeconds / 60)} 分 {(int)sum.ElapsedSeconds % 60} 秒たちました";
            }
        }
        else
        {
            Finish(sum, "時間になったので、監視を終えました");
        }
    }

    public void Cancel()
    {
        _startRequested = false;
        if (!_monitor.Running) return;
        _monitor.Reset();
        _record = null;
        ShowIdle();
        Ui.SetText(_note, "監視を取り消しました(ページやタブを離れた、Esc を押した、または窓を閉じたため)");
    }

    void Start(DiagContext c)
    {
        var pad = c.Pad!;
        var sticks = c.S.Sticks.TryGetValue(pad.InstanceId, out var st) ? st : default;
        uint held = c.S.Live.TryGetValue(pad.InstanceId, out var lv) ? DiagSample.Mask(lv.Pressed) : 0;
        _monitor.Settings = _monitor.Settings with { DurationSeconds = _minutes * 60 };
        _monitor.Start(c.NowNs, sticks.Left, sticks.Right, held);
        (_padId, _guid, _padName, _padKind, _padInfo, _gone, _record) = (pad.InstanceId, pad.Guid, pad.Name, pad.Kind.ToString(), pad, false, null);
        _remainText = "";
        _shownEvents = -1;
        _rows.Clear();
        Ui.SetText(_note, $"{pad.Name} を見ています。触らずに置いておいてください");
        _emptyText.Text = "まだ何も起きていません";
        _minutesRow.IsEnabled = false;
        _startBtn.Visibility = Visibility.Collapsed;
        _stopBtn.Visibility = Visibility.Visible;
        _live.Visibility = Visibility.Visible;
        _resultCard.Visibility = Visibility.Visible;
        _keep.Visibility = Visibility.Collapsed;
        _verdict.Visibility = Visibility.Collapsed;
        Ui.SetText(_kept, "");
        UpdateCounts(_monitor.Summary);
    }

    void Stop()
    {
        if (!_monitor.Running) return;
        _monitor.Stop();
        Finish(_monitor.Summary, "途中で止めました。それまでの分を出しています");
    }

    void Finish(IdleSummary sum, string note)
    {
        UpdateCounts(sum);
        _minutesRow.IsEnabled = true;
        _startBtn.Visibility = Visibility.Visible;
        _stopBtn.Visibility = Visibility.Collapsed;
        _live.Visibility = Visibility.Collapsed;
        Ui.SetText(_padWarn, "");
        Ui.SetText(_note, note);
        _emptyText.Text = "何も起きませんでした";
        string verdict = Verdict(sum);
        Ui.SetText(_verdict, verdict);
        _verdict.SetResourceReference(TextBlock.ForegroundProperty, sum.TotalEvents > 0 ? "WarningTextBrush" : "TextBrush");
        _record = new DiagRecord("idle", "放置監視", DateTime.UtcNow, _padName, _padKind,
            [
                new("監視した時間(分)", Math.Round(sum.ElapsedSeconds / 60, 1)),
                new("スティックの揺れ(回)", sum.DriftEvents),
                new("勝手な押下(回)", sum.ButtonEvents),
                new("トリガーの揺れ(回)", sum.TriggerEvents),
                new("切断・再接続(回)", sum.DisconnectEvents),
                new("左スティックの最大のずれ", Math.Round(sum.MaxLeftDeviation, 3)),
                new("右スティックの最大のずれ", Math.Round(sum.MaxRightDeviation, 3)),
            ],
            verdict, [], App.Version);
        _keep.Visibility = Visibility.Visible;
        _keep.IsEnabled = true;
        Ui.SetText(_kept, "");
    }

    static string Verdict(IdleSummary s)
    {
        if (s.TotalEvents == 0)
            return "触っていない間に、勝手な入力と切断はありませんでした";
        var parts = new List<string>();
        if (s.DriftEvents > 0) parts.Add("スティックが勝手に動いています。スティックのずれ(ドリフト)の可能性があります");
        if (s.ButtonEvents > 0) parts.Add("触っていないのにボタンが押されました。ボタンの接点の不調や、何かが当たっていた可能性があります");
        if (s.TriggerEvents > 0) parts.Add("トリガーが勝手に動いています。トリガーが元の位置に戻りきっていない可能性があります");
        if (s.DisconnectEvents > 0) parts.Add("途中で切れました。電池・ケーブル・電波の不調の可能性があります");
        return string.Join("\n", parts);
    }

    /// <summary>回数の札・ずれの文・一覧。一覧は件数が変わったときだけ足す。</summary>
    void UpdateCounts(IdleSummary s)
    {
        SetNum(_drift, s.DriftEvents);
        SetNum(_buttons, s.ButtonEvents);
        SetNum(_triggers, s.TriggerEvents);
        SetNum(_disconnects, s.DisconnectEvents);
        string summary = $"始めた時の位置からの最大のずれ  左スティック {s.MaxLeftDeviation.ToString("0.000", Inv)}  ・  右スティック {s.MaxRightDeviation.ToString("0.000", Inv)}";
        if (_summary.Text != summary) _summary.Text = summary;
        if (s.Events.Count == _shownEvents) return;
        if (s.Events.Count < _rows.Count) _rows.Clear();
        for (int i = _rows.Count; i < s.Events.Count; i++)
            _rows.Add(Row(s.Events[i]));
        _shownEvents = s.Events.Count;
        bool any = _rows.Count > 0;
        _list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Ui.SetText(_capped, s.Events.Count >= IdleMonitor.MaxListed ? $"一覧は最初の {IdleMonitor.MaxListed} 件までです(回数は全部を数えています)" : "");
    }

    static void SetNum(TextBlock t, int n)
    {
        string text = n.ToString("N0", Inv) + " 回";
        if (t.Text == text) return;
        t.Text = text;
        if (n > 0) t.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        else t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
    }

    EventRow Row(IdleEvent e)
    {
        var t = TimeSpan.FromTicks((long)(e.OffsetNs / 100));
        string time = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
        string mag = e.Magnitude.ToString("0.00", Inv);
        return e.Kind switch
        {
            IdleEventKind.StickDrift => new(time, "スティック", $"{e.Detail}が始めた位置から {mag} 動いた"),
            IdleEventKind.TriggerWobble => new(time, "トリガー", $"{e.Detail}が {(e.Magnitude * 100).ToString("0", Inv)}% まで動いた"),
            IdleEventKind.PhantomButton => new(time, "ボタン", $"{ButtonLabel(e.Button)} が押された"),
            IdleEventKind.Disconnected => new(time, "切断", "パッドが外れた"),
            _ => new(time, "再接続", "パッドがつながった"),
        };
    }

    string ButtonLabel(PadButton? b)
    {
        if (b is not { } button) return "?";
        var settings = App.Current.Settings;
        var skin = Labels.ChooseSkin(_padInfo, _padInfo is null ? null : settings.Devices.GetValueOrDefault(_padInfo.Guid));
        return Labels.For(button.ToString(), skin, settings.LabelStyle, _padInfo);
    }

    /// <summary>監視していないときの形(結果があれば残す)。</summary>
    void ShowIdle()
    {
        _minutesRow.IsEnabled = true;
        _startBtn.Visibility = Visibility.Visible;
        _stopBtn.Visibility = Visibility.Collapsed;
        _live.Visibility = Visibility.Collapsed;
        Ui.SetText(_padWarn, "");
        _resultCard.Visibility = _record is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Keep()
    {
        if (_record is null) return;
        bool ok = App.Current.Diagnostics.Save(_record) is not null;
        Ui.SetText(_kept, ok ? "残しました" : "残せませんでした(ログを見てください)");
        _keep.IsEnabled = !ok;
    }
}
