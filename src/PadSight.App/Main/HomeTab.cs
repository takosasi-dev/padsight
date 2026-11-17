// S1 ホーム(画面設計書 §4.5・仕様書 FR-1・FR-4・FR-7)。パッドの一覧、表示するパッドの選び方、パネルの出し入れ、
// 裏の入力の確かめ(10 秒の間、メインの画面が前に無い状態で届いた押下を数える)、小さいパッド表示を持つ。
// 裏かどうかは自分の窓の IsActive だけで見て、他の窓の状態は見ない(INV-6)。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PadSight.App.Panels;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class HomeTab : UserControl
{
    sealed record PadRow(string Name, string Kind, string Shown, string Skin, string Guid, uint Id);

    readonly Button _toggle, _arrange, _check;
    readonly TextBlock _checkText = Ui.Weak();
    readonly ListView _list = new() { MinHeight = 90, MaxHeight = 200 };
    readonly TextBlock _empty = Ui.Text("パッドが見つかりません。つないでから数秒待ってください");
    readonly TextBlock _same = Ui.Weak("同じ型が 2 台あります。固定は先につながった方になります");
    readonly RadioButton _lastUsed = new() { Content = "最後に操作", Margin = new Thickness(0, 0, 12, 0) };
    readonly RadioButton _pinned = new() { Content = "固定", Margin = new Thickness(0, 0, 12, 0) };
    readonly PadView _mini = new();
    string _padsKey = "";
    long _lastVersion = -1;
    DateTime? _checkEnd;
    long _checkCounter = -1, _checkCount;

    /// <summary>FR-40: デモを流す・止める。流している間は横に知らせを出す。</summary>
    UIElement DemoButton()
    {
        var note = Ui.Weak();
        note.VerticalAlignment = VerticalAlignment.Center;
        Button? b = null;
        void Show()
        {
            bool on = App.Current.DemoRunning;
            b!.Content = on ? "デモを止める" : "デモを流す";
            note.Text = on ? "デモの入力を流しています(統計には数えません)" : "";
        }
        b = Ui.Button("", () => { App.Current.SetDemo(!App.Current.DemoRunning); Show(); });
        Show();
        return Ui.Horizontal(b, note);
    }

    public HomeTab()
    {
        _toggle = Ui.Primary("パネルを隠す", () => App.Current.Panels.Toggle());
        _arrange = Ui.Button("配置を直す", () => App.Current.Panels.SetArranging(!App.Current.Panels.Arranging));
        _check = Ui.Button("裏の入力を確かめる", StartCheck);
        _checkText.VerticalAlignment = VerticalAlignment.Center;

        var grid = new GridView();
        grid.Columns.Add(new GridViewColumn { Header = "パッド", Width = 330, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PadRow.Name)) });
        grid.Columns.Add(new GridViewColumn { Header = "種類", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PadRow.Kind)) });
        grid.Columns.Add(new GridViewColumn { Header = "表示", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PadRow.Shown)) });
        grid.Columns.Add(new GridViewColumn { Header = "スキン", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PadRow.Skin)) });
        _list.View = grid;

        var s = App.Current.Settings;
        _lastUsed.IsChecked = s.SelectionMode == "last_used";
        _pinned.IsChecked = s.SelectionMode == "pinned";
        _lastUsed.Checked += (_, _) => SetMode("last_used", s.PinnedGuid);
        _pinned.Checked += (_, _) =>
        {
            string? guid = (_list.SelectedItem as PadRow)?.Guid ?? s.PinnedGuid ?? (App.Current.Bus.Latest.Pad?.Guid);
            SetMode("pinned", guid);
        };
        var pin = Ui.Button("選んだパッドに固定する", () =>
        {
            if (_list.SelectedItem is PadRow row)
            {
                _pinned.IsChecked = true;
                SetMode("pinned", row.Guid);
            }
        });

        var miniBox = new Border { Child = new Viewbox { Child = _mini, Width = 216, Height = 144 }, Background = Draw.Background, CornerRadius = new CornerRadius(8), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) };

        var stack = new StackPanel();
        // 主な操作は左に、終了(戻せない操作)は右端に離して置く
        var quit = Ui.Danger("PadSight を終了", () => App.Current.Quit());
        quit.Margin = new Thickness(8, 4, 0, 4);
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(quit, Dock.Right);
        top.Children.Add(quit);
        top.Children.Add(Ui.Horizontal(_toggle, _arrange, _check, _checkText));
        stack.Children.Add(top);
        stack.Children.Add(Ui.Section("パネルの置き場所"));
        stack.Children.Add(Ui.Row("まとめて置き直す", Ui.Horizontal(
            Ui.Button("四隅", () => App.Current.Panels.ArrangePreset("corners")),
            Ui.Button("左側に縦に並べる", () => App.Current.Panels.ArrangePreset("left")),
            Ui.Button("右側に縦に並べる", () => App.Current.Panels.ArrangePreset("right"))), 140));
        stack.Children.Add(Ui.Weak($"1 枚ずつ動かすときは「配置を直す」({App.Current.Settings.HotkeyArrange ?? "ホットキーなし"})のあと、ドラッグで動かします。右下のつまみで大きさも変わります。次の起動でも同じ位置に出ます"));
        stack.Children.Add(Ui.Row("パッドが無いとき", DemoButton(), 140));
        stack.Children.Add(Ui.Section("パッド"));
        stack.Children.Add(_list);
        stack.Children.Add(_empty);
        stack.Children.Add(_same);
        stack.Children.Add(Ui.Row("表示するパッド", Ui.Horizontal(_lastUsed, _pinned, pin), 120));
        stack.Children.Add(miniBox);
        stack.Children.Add(Ui.Weak(Texts.HidHide));
        Content = Ui.Scroll(stack);
        App.Current.Panels.StateChanged += UpdateButtons;
        UpdateButtons();
    }

    static void SetMode(string mode, string? guid)
    {
        var s = App.Current.Settings;
        s.SelectionMode = mode;
        s.PinnedGuid = guid;
        App.Current.SaveSettings();
    }

    void UpdateButtons()
    {
        _toggle.Content = App.Current.Panels.Visible ? "パネルを隠す" : "パネルを表示する";
        _arrange.Content = App.Current.Panels.Arranging ? "配置を終える" : "配置を直す";
    }

    void StartCheck()
    {
        _checkEnd = DateTime.UtcNow.AddSeconds(10);
        _checkCount = 0;
        _checkCounter = App.Current.Bus.Latest.PressCounter;
    }

    public void CancelCheck()
    {
        _checkEnd = null;
        _checkText.Text = "";
    }

    /// <summary>FR-7: メインの画面が前に無い間に届いた押下を数える。</summary>
    public void TrackBackground(PadSnapshot s, bool active)
    {
        if (_checkEnd is not DateTime end)
            return;
        if (!active)
            _checkCount += s.PressCounter - _checkCounter;
        _checkCounter = s.PressCounter;
        double left = (end - DateTime.UtcNow).TotalSeconds;
        if (left > 0)
        {
            _checkText.Text = $"別の窓(メモ帳)を前にして、パッドのボタンを押してください。残り {Math.Ceiling(left)} 秒";
            return;
        }
        _checkEnd = null;
        _checkText.Text = _checkCount > 0
            ? $"PadSight が前に無い間に、{_checkCount} 回の押下を受け取りました"
            : "受け取れませんでした。設定の『入力の取り方』を変えて、もう一度試してください";
    }

    public void Render(PadSnapshot s, ulong nowNs, bool active)
    {
        TrackBackground(s, active);
        if (s.Version == _lastVersion)
            return;
        _lastVersion = s.Version;
        var settings = App.Current.Settings;
        string key = string.Join("|", s.Pads.Select(p => p.InstanceId + p.Name)) + "|" + s.Pad?.InstanceId + string.Join(",", settings.Devices);
        if (key != _padsKey)
        {
            _padsKey = key;
            _list.ItemsSource = s.Pads.Select(p =>
            {
                var skin = Labels.ChooseSkin(p, settings.Devices.GetValueOrDefault(p.Guid));
                string manual = settings.Devices.GetValueOrDefault(p.Guid) ?? "auto";
                return new PadRow(p.Name, KindName(p.Kind), p.InstanceId == s.Pad?.InstanceId ? "表示中" : "—",
                    manual == "auto" ? $"自動({SkinName(skin)})" : SkinName(skin), p.Guid, p.InstanceId);
            }).ToList();
            _empty.Visibility = s.Pads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _same.Visibility = s.Pads.GroupBy(p => p.Guid).Any(g => g.Count() > 1) ? Visibility.Visible : Visibility.Collapsed;
        }
        // 一覧で選んだ行のパッドを描く(パネルの表示は変えない)。選んでいなければ表示中のパッド
        var view = _list.SelectedItem is PadRow row && s.Live.TryGetValue(row.Id, out var live)
            ? s with { Pad = live.Info, Connected = true, Pressed = live.Pressed, Left = live.Left, Right = live.Right, LeftTrigger = live.LeftTrigger, RightTrigger = live.RightTrigger, Raw = live.Raw }
            : s;
        _mini.Render(view, settings, nowNs);
    }

    public static string KindName(PadKind k) => k switch
    {
        PadKind.Xbox => "Xbox",
        PadKind.PlayStation => "PlayStation",
        PadKind.SwitchPro => "Switch Pro",
        PadKind.JoyConPair => "Joy-Con(2 本)",
        PadKind.JoyConLeft => "Joy-Con(左)",
        PadKind.JoyConRight => "Joy-Con(右)",
        PadKind.GameCube => "GameCube",
        PadKind.Unmapped => "割り当てなし",
        _ => "汎用",
    };

    public static string SkinName(Skin s) => s switch
    {
        Skin.Xbox => "Xbox 型",
        Skin.PlayStation => "PlayStation 型",
        Skin.SwitchPro => "Switch Pro 型",
        Skin.JoyConPair => "Joy-Con ペア型",
        Skin.Arcade => "アケコン型",
        Skin.Unmapped => "割り当てなし",
        _ => "汎用型",
    };
}
