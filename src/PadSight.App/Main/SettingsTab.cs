// S5 設定(画面設計書 §4.9・仕様書 FR-6・FR-8・FR-12・FR-29〜FR-34)。ホットキー、起動、入力の取り方、割り当て、
// セッション、見た目、データ(設定の写し)、注意、このソフトについて(版と部品のライセンス)。
// ホットキーの「変える」は、自分の窓が受けたキーだけを使う(グローバルなキーボードの読み取りはしない。INV-5)。
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverlayCore;

namespace PadSight.App.Main;

public sealed class SettingsTab : UserControl
{
    readonly StackPanel _stack = new();
    int _capturing;
    TextBlock? _captureText;

    public SettingsTab()
    {
        Content = Ui.Scroll(_stack);
        Build();
    }

    public bool CapturingHotkey => _capturing != 0;

    void Build()
    {
        var app = App.Current;
        var s = app.Settings;
        _stack.Children.Clear();

        _stack.Children.Add(Ui.Section("ホットキー"));
        HotkeyRow("パネルの表示切替", App.HotkeyToggle, () => s.HotkeyToggle, v => s.HotkeyToggle = v);
        HotkeyRow("配置を直す", App.HotkeyArrange, () => s.HotkeyArrange, v => s.HotkeyArrange = v);
        HotkeyRow("入力履歴を消す", App.HotkeyClear, () => s.HotkeyClearHistory, v => s.HotkeyClearHistory = v);

        _stack.Children.Add(Ui.Section("起動"));
        _stack.Children.Add(Ui.Check("ログオン時に起動する", AutoStart.IsEnabled(), AutoStart.Set));
        _stack.Children.Add(Ui.Check("起動したときにパネルを出す", s.VisibleOnStart, v => { s.VisibleOnStart = v; app.SaveSettings(); }));

        _stack.Children.Add(Ui.Section("入力の取り方"));
        _stack.Children.Add(Ui.Row("Windows.Gaming.Input を使う", Ui.Combo([("default", "既定"), ("on", "使う"), ("off", "使わない")], s.InputWgi,
            v => { s.InputWgi = v; app.SaveSettings(); app.ReconnectInput(); }), 280));
        _stack.Children.Add(Ui.Check("PlayStation のパッドを専用の方法で読む", s.InputHidapiPlayStation, v => { s.InputHidapiPlayStation = v; app.SaveSettings(); app.ReconnectInput(); }));
        _stack.Children.Add(Ui.Check("Switch のパッドを専用の方法で読む", s.InputHidapiSwitch, v => { s.InputHidapiSwitch = v; app.SaveSettings(); app.ReconnectInput(); }));
        _stack.Children.Add(Ui.Weak("変えるとパッドをつなぎ直します。裏で入力が届かないときに変えてください"));
        _stack.Children.Add(Ui.Horizontal(Ui.Button("今の設定でつなぎ直す", app.ReconnectInput)));

        _stack.Children.Add(Ui.Section("割り当て"));
        int? count = app.Bus.Latest.MappingCount;
        _stack.Children.Add(Ui.Text(!File.Exists(app.MappingFile) ? "gamecontrollerdb.txt: 置いていません"
            : count is null ? "gamecontrollerdb.txt: 次の起動で読みます"
            : count >= 0 ? $"gamecontrollerdb.txt: 読んだ件数 {count}" : "割り当てのファイルを読めませんでした"));
        _stack.Children.Add(Ui.Horizontal(Ui.Button("データのフォルダを開く", () => ShellOpen.Folder(App.DataDir))));

        _stack.Children.Add(Ui.Section("セッション"));
        _stack.Children.Add(Ui.Slider("操作の無い時間で区切る(分)", 1, 60, 1, s.SessionIdleMinutes, "0", v => { s.SessionIdleMinutes = (int)v; app.SaveSettings(); }));

        _stack.Children.Add(Ui.Section("見た目"));
        _stack.Children.Add(Ui.Row("テーマ", Ui.Combo([("system", "システムに合わせる"), ("light", "ライト"), ("dark", "ダーク")], s.Theme,
            v => { s.Theme = v; app.SaveSettings(); app.ApplyTheme(); })));
        _stack.Children.Add(Ui.Weak("メインの画面だけに効きます。パネルの見た目はパネルのタブで変えます"));

        _stack.Children.Add(Ui.Section("データ"));
        _stack.Children.Add(Ui.Horizontal(Ui.Button("データのフォルダを開く", () => ShellOpen.Folder(App.DataDir))));
        foreach (var file in app.Store.ListBackups())
        {
            string when = File.GetLastWriteTimeUtc(file).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            _stack.Children.Add(Ui.Horizontal(Ui.Text(when + "   "), Ui.Button("この写しに戻す", () => Restore(file, when))));
        }

        _stack.Children.Add(Ui.Section("注意"));
        _stack.Children.Add(Ui.Text(Texts.Fullscreen));

        _stack.Children.Add(Ui.Section("このソフトについて"));
        _stack.Children.Add(Ui.Text($"PadSight {App.Version}(MIT ライセンス)"));
        _stack.Children.Add(AboutPage.DoesAndDoesNot());
        _stack.Children.Add(Ui.Weak(Texts.AntiCheat));
        _stack.Children.Add(Ui.Section("使っている部品のライセンス"));
        _stack.Children.Add(new TextBox
        {
            Text = Notices(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 240,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12,
        });
    }

    static string Notices()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("THIRD-PARTY-NOTICES.md");
        if (s is null)
            return "(THIRD-PARTY-NOTICES.md が見つかりません)";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    void Restore(string file, string when)
    {
        if (MessageBox.Show(Window.GetWindow(this), $"今の設定を、{when} の写しで置き換えます", "PadSight", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        if (SettingsStore.ReadBackup(file) is not { } restored)
        {
            MessageBox.Show(Window.GetWindow(this), "写しを読めませんでした", "PadSight");
            return;
        }
        App.Current.ReplaceSettings(restored);
        Build();
    }

    void HotkeyRow(string label, int id, Func<string?> get, Action<string?> set)
    {
        var app = App.Current;
        var current = Ui.Text(get() ?? "使わない");
        current.MinWidth = 140;
        var status = Ui.Weak(app.HotkeyStatus.GetValueOrDefault(id, ""));
        status.Margin = new Thickness(8, 0, 0, 0);
        var change = Ui.Button("変える", () =>
        {
            _capturing = id;
            _captureText = current;
            current.Text = "組み合わせを押してください";
            app.UnregisterHotkeys(); // 受け付けの間は、自分の組み合わせも窓に届くようにする
            _onCaptured = combo =>
            {
                if (combo is not null) set(combo);
                app.SaveSettings();
                app.RegisterHotkeys();
                Build();
            };
        });
        var off = Ui.Button("使わない", () =>
        {
            set(null);
            app.SaveSettings();
            app.RegisterHotkeys();
            Build();
        });
        _stack.Children.Add(Ui.Row(label, Ui.Horizontal(current, change, off, status)));
    }

    Action<string?>? _onCaptured;

    /// <summary>「変える」の後に押したキー。Esc で取り消し。修飾キーの無い組み合わせは受けない(FR-12)。</summary>
    public void OnCaptureKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Finish(null);
            return;
        }
        string? combo = HotkeyHost.Format(Keyboard.Modifiers, key);
        if (combo is not null)
            Finish(combo);
        else if (_captureText is not null && Keyboard.Modifiers == ModifierKeys.None && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift))
            _captureText.Text = "修飾キー(Ctrl・Alt・Shift)と一緒に押してください";
    }

    void Finish(string? combo)
    {
        _capturing = 0;
        var cb = _onCaptured;
        _onCaptured = null;
        cb?.Invoke(combo);
    }
}
