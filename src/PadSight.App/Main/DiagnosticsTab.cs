// S3 診断(画面設計書 §4.7・仕様書 FR-54・§9.20)。PadLab を統合したタブの外枠: 上にパッドの選択と情報・機種のプロファイル、左に 6 つの一覧、右に選んだページ。
// ページは IDiagPage。測る物があるページが出ている間だけ、SDL のスレッドに診断用の細かい値(DiagSample)を積ませ、描画のたびに選んだパッドの分を渡す。
// ページを足す・入れ替えるときは Pages の並びだけを直す(外枠はページの中身を知らない)。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PadSight.Core;

namespace PadSight.App.Main;

/// <param name="S">今のスナップショット</param>
/// <param name="NowNs">今の時刻(SDL の時刻)</param>
/// <param name="Pad">上で選んだパッド。無ければ null</param>
/// <param name="Profile">選んだパッドの機種のプロファイル(自動か手で選んだ物)。パッドが無ければ null</param>
/// <param name="Samples">前の描画から今までに積まれた、選んだパッドの DiagSample(古い順)。測る物が無いページでは空</param>
public sealed record DiagContext(PadSnapshot S, ulong NowNs, PadInfo? Pad, PadProfile? Profile, IReadOnlyList<DiagSample> Samples);

/// <summary>診断のタブの 1 ページ(ボタン・スティック・トリガー・振動・放置監視・履歴)。</summary>
public interface IDiagPage
{
    /// <summary>左の一覧に出す名前。</summary>
    string Title { get; }

    FrameworkElement View { get; }

    /// <summary>このページが出ている間、DiagSample が要るか(測っている間だけ真にしてよい)。</summary>
    bool WantsSamples { get; }

    /// <summary>描画のたびに呼ぶ(このページが出ているときだけ)。</summary>
    void Render(DiagContext c);

    /// <summary>Esc・ページやタブを離れた・窓を閉じたときに、測定を止める。結果は出さない。</summary>
    void Cancel();
}

public sealed class DiagnosticsTab : UserControl
{
    readonly ComboBox _pad = new() { MinWidth = 300 };
    readonly ComboBox _profile = new() { MinWidth = 260 };
    readonly TextBlock _info = Ui.Weak();
    readonly ListBox _nav = new() { MinWidth = 150, Margin = new Thickness(0, 0, 16, 0) };
    readonly ContentControl _page = new();
    readonly List<DiagSample> _drain = [];
    readonly List<DiagSample> _mine = [];
    readonly Dictionary<string, string> _profileByGuid = []; // 手で選んだプロファイル(起動している間だけ覚える)
    string _padsKey = "";
    bool _tapOn;

    public IReadOnlyList<IDiagPage> Pages { get; }

    public DiagnosticsTab()
    {
        Pages =
        [
            new PlaceholderPage("ボタン"),
            new StickPage(),
            new PlaceholderPage("トリガー"),
            new PlaceholderPage("振動"),
            new PlaceholderPage("放置監視"),
            new PlaceholderPage("履歴"),
        ];
        foreach (var p in Pages)
            _nav.Items.Add(new ListBoxItem { Content = p.Title, Tag = p });
        _nav.SelectionChanged += (_, _) =>
        {
            foreach (var p in Pages) p.Cancel();
            _page.Content = (_nav.SelectedItem as ListBoxItem)?.Tag is IDiagPage page ? page.View : null;
        };
        _nav.SelectedIndex = 0;

        _profile.SelectionChanged += (_, _) =>
        {
            if (SelectedPad is { } pad && _profile.SelectedItem is ComboBoxItem { Tag: string id })
            {
                if (id == "auto") _profileByGuid.Remove(pad.Guid); else _profileByGuid[pad.Guid] = id;
            }
        };

        var header = Ui.Card(null,
            Ui.Horizontal(Ui.Text("パッド"), _pad, Ui.Text("機種"), _profile),
            _info);
        var body = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_nav, Dock.Left);
        body.Children.Add(_nav);
        body.Children.Add(_page);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);
        Content = Ui.LeftCapped(new Border { Child = root, Padding = new Thickness(20, 16, 20, 16) }, 1180);
    }

    PadInfo? SelectedPad => (_pad.SelectedItem as ComboBoxItem)?.Tag as PadInfo;

    IDiagPage? CurrentPage => (_nav.SelectedItem as ListBoxItem)?.Tag as IDiagPage;

    /// <summary>ページを選ぶ(0 から。帯のボタンなどから使う)。</summary>
    public void SelectPage(int index) => _nav.SelectedIndex = index;

    public void Render(PadSnapshot s, ulong nowNs)
    {
        RefreshPads(s);
        var pad = SelectedPad;
        // 選んだパッドが今もあるか(外れたら情報を消す)
        if (pad is not null && !s.Pads.Any(p => p.InstanceId == pad.InstanceId))
            pad = null;
        var profile = pad is null ? null : PadProfiles.ById(_profileByGuid.GetValueOrDefault(pad.Guid)) ?? PadProfiles.For(pad);
        _info.Text = pad is null ? "パッドが見つかりません" : Info(pad, s);

        var page = CurrentPage;
        SetTap(page?.WantsSamples == true && IsVisible);
        _mine.Clear();
        if (_tapOn)
        {
            _drain.Clear();
            App.Current.DrainDiag(_drain);
            if (pad is not null)
                foreach (var d in _drain)
                    if (d.PadId == pad.InstanceId) _mine.Add(d);
        }
        page?.Render(new DiagContext(s, nowNs, pad, profile, _mine));
    }

    /// <summary>Esc・タブを離れた・窓を閉じたとき。</summary>
    public void Cancel()
    {
        foreach (var p in Pages) p.Cancel();
        SetTap(false);
    }

    void SetTap(bool on)
    {
        if (on == _tapOn)
            return;
        _tapOn = on;
        App.Current.SetDiagTap(on);
    }

    void RefreshPads(PadSnapshot s)
    {
        string key = string.Join("|", s.Pads.Where(p => p.IsMapped).Select(p => p.InstanceId + p.Name));
        if (key == _padsKey)
            return;
        _padsKey = key;
        uint? sel = SelectedPad?.InstanceId;
        _pad.Items.Clear();
        foreach (var p in s.Pads.Where(p => p.IsMapped))
            _pad.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        int idx = _pad.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (i.Tag as PadInfo)?.InstanceId == sel);
        _pad.SelectedIndex = idx >= 0 ? idx : _pad.Items.Count > 0 ? 0 : -1;
        RefreshProfiles();
    }

    void RefreshProfiles()
    {
        _profile.Items.Clear();
        var pad = SelectedPad;
        _profile.Items.Add(new ComboBoxItem { Content = pad is null ? "自動" : $"自動({PadProfiles.For(pad).Name})", Tag = "auto" });
        foreach (var p in PadProfiles.All)
            _profile.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        string want = pad is null ? "auto" : _profileByGuid.GetValueOrDefault(pad.Guid, "auto");
        _profile.SelectedIndex = Math.Max(0, _profile.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == want));
    }

    static string Info(PadInfo p, PadSnapshot s)
    {
        var inv = CultureInfo.InvariantCulture;
        string conn = p.Connection switch { "Wired" => "USB", "Wireless" => "ワイヤレス", _ => "つなぎ方は不明" };
        string ids = p.Vendor != 0 ? $"VID {p.Vendor.ToString("X4", inv)} / PID {p.Product.ToString("X4", inv)}" : "VID/PID 不明";
        string power = s.Power.TryGetValue(p.InstanceId, out var pw) ? pw.Power switch
        {
            "OnBattery" => pw.Percent >= 0 ? $"電池 {pw.Percent}%" : "電池",
            "Charging" => pw.Percent >= 0 ? $"充電中 {pw.Percent}%" : "充電中",
            "Charged" => "充電済み",
            "NoBattery" => "電池なし(有線)",
            _ => "電池は不明",
        } : "電池は不明";
        return $"{p.TypeName}  ・  {conn}  ・  {ids}  ・  {power}";
    }
}

/// <summary>まだ作っていないページ(統合の途中だけ使う)。</summary>
sealed class PlaceholderPage(string title) : IDiagPage
{
    public string Title => title;
    public FrameworkElement View { get; } = Ui.Card(title, Ui.Weak("準備中です"));
    public bool WantsSamples => false;
    public void Render(DiagContext c) { }
    public void Cancel() { }
}

/// <summary>スティックのページ。今は前からのスティック診断(ボタンの検査・トリガーの範囲を含む)をそのまま載せている。</summary>
sealed class StickPage : IDiagPage
{
    readonly DiagTab _old = new();
    public string Title => "スティック";
    public FrameworkElement View => _old;
    public bool WantsSamples => false;
    public void Render(DiagContext c) => _old.Render(c.S);
    public void Cancel() => _old.Cancel();
}
