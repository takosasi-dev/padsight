// S2 パネル(画面設計書 §4.6・仕様書 FR-5・FR-10・FR-18・FR-20・FR-22・FR-24)。
// 左に 4 つのパネル、真ん中に設定、右に見本を置く。どの値も変えた時点でパネルと見本に効き、保存される。
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PadSight.App.Panels;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class PanelsTab : UserControl
{
    readonly ListBox _list = new() { Width = 140, Margin = new Thickness(16, 16, 0, 16) };
    readonly ContentControl _settingsHost = new();
    readonly Viewbox _previewBox = new() { MaxWidth = 300, MaxHeight = 300, VerticalAlignment = VerticalAlignment.Top };
    readonly Dictionary<string, PanelView> _previews = new()
    {
        ["pad"] = new PadView(), ["history"] = new HistoryView(), ["stick"] = new StickView(), ["stats"] = new StatsView(),
    };
    long _lastVersion = -1;
    bool _dirty;

    public PanelsTab()
    {
        foreach (var e in App.Current.Panels.Entries)
            _list.Items.Add(new ListBoxItem { Content = e.Title, Tag = e.Name });
        _list.SelectionChanged += (_, _) => Build();
        _list.SelectedIndex = 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.Children.Add(_list);
        var scroll = Ui.Scroll(_settingsHost);
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        var preview = new Border { Child = _previewBox, Background = Draw.Background, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 16, 16, 16), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);
        Content = grid;
    }

    string Selected => (_list.SelectedItem as ListBoxItem)?.Tag as string ?? "pad";

    void Changed()
    {
        App.Current.Panels.ApplyAll();
        App.Current.SaveSettings();
        _dirty = true;
    }

    void Build()
    {
        var s = App.Current.Settings;
        string name = Selected;
        var p = s.Panels[name];
        var stack = new StackPanel();
        stack.Children.Add(ProfileRows());
        // FR-39: 4 枚に共通の配色テーマ。選ぶと押したときの色もおすすめの色にする
        stack.Children.Add(Ui.Row("パネルの配色(4 枚共通)", Ui.Combo(PanelTheme.Choices, s.PanelTheme, v =>
        {
            s.PanelTheme = v;
            if (PanelTheme.RecommendedPressed(v) is { } pressed)
                s.PressedColor = pressed;
            Changed();
            Build();
        })));
        if (s.PanelTheme == "custom")
        {
            stack.Children.Add(ColorRow("背景の色", s.CustomBackground, v => s.CustomBackground = v));
            stack.Children.Add(ColorRow("線の色", s.CustomLine, v => s.CustomLine = v));
            stack.Children.Add(ColorRow("文字の色", s.CustomText, v => s.CustomText = v));
        }
        stack.Children.Add(Ui.Horizontal(Ui.Button("全部のパネルを見やすい設定にする(不透明度 100%・背景 85%)", () =>
        {
            foreach (var ps in s.Panels.Values) { ps.Opacity = 100; ps.BackgroundOpacity = 85; }
            Changed();
            Build();
        })));
        stack.Children.Add(Ui.Check("表示する", p.Visible, v => { p.Visible = v; Changed(); }));
        stack.Children.Add(Ui.Slider("大きさ(%)", 50, 300, 10, p.Scale, "0", v => { p.Scale = (int)v; Changed(); }));
        stack.Children.Add(Ui.Slider("不透明度(%)", 20, 100, 1, p.Opacity, "0", v => { p.Opacity = (int)v; Changed(); }));
        stack.Children.Add(Ui.Slider("背景の濃さ(%)", 0, 100, 1, p.BackgroundOpacity, "0", v => { p.BackgroundOpacity = (int)v; Changed(); }));
        stack.Children.Add(Ui.Horizontal(Ui.Button("既定の位置に戻す", () => App.Current.Panels.ResetPosition(name))));
        stack.Children.Add(PositionRow(name));
        stack.Children.Add(Ui.Section("このパネルの設定"));
        switch (name)
        {
            case "pad": BuildPad(stack, s); break;
            case "history": BuildHistory(stack, s); break;
            case "stick": BuildStick(stack, s); break;
            case "stats": BuildStats(stack, s); break;
        }
        _settingsHost.Content = stack;
        _previewBox.Child = _previews[name];
        _dirty = true;
    }

    string? _profileChoice;
    string _profileNote = "";

    /// <summary>FR-42: プロファイルの一覧・当てる・消す・今の設定を保存。</summary>
    UIElement ProfileRows()
    {
        var s = App.Current.Settings;
        var stack = new StackPanel();
        var names = s.Profiles.Keys.OrderBy(n => n, StringComparer.CurrentCulture).ToList();
        if (names.Count == 0)
            stack.Children.Add(Ui.Row("プロファイル", Ui.Weak("まだありません")));
        else
        {
            if (_profileChoice is null || !names.Contains(_profileChoice))
                _profileChoice = names[0];
            stack.Children.Add(Ui.Row("プロファイル", Ui.Horizontal(
                Ui.Combo([.. names.Select(n => (n, n))], _profileChoice, v => _profileChoice = v),
                Ui.Button("当てる", () =>
                {
                    string name = _profileChoice!;
                    App.Current.ReplaceSettings(s.WithProfile(name, out var reset));
                    foreach (var key in reset)
                        AppLog.Write("プロファイルの値を既定に戻した: " + key);
                    _profileNote = $"『{name}』を当てました";
                    Build();
                }),
                Ui.Button("消す", () =>
                {
                    string name = _profileChoice!;
                    if (MessageBox.Show(Window.GetWindow(this), $"プロファイル『{name}』を消します", "PadSight", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                        return;
                    s.Profiles.Remove(name);
                    App.Current.SaveSettings();
                    _profileNote = $"『{name}』を消しました";
                    Build();
                }))));
        }
        var nameBox = new TextBox { Width = 150, MaxLength = 30, VerticalAlignment = VerticalAlignment.Center };
        Button? save = null;
        save = Ui.Button("保存", () =>
        {
            string name = nameBox.Text.Trim();
            bool exists = s.Profiles.ContainsKey(name);
            if (!exists && s.Profiles.Count >= Settings.MaxProfiles)
                _profileNote = "20 個までです。どれかを消してください";
            else if (exists && MessageBox.Show(Window.GetWindow(this), $"『{name}』を今の設定で上書きします", "PadSight", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                return;
            else if (s.SaveProfile(name))
            {
                App.Current.SaveSettings();
                _profileChoice = name;
                _profileNote = $"『{name}』を保存しました";
            }
            Build();
        });
        save.IsEnabled = false;
        nameBox.TextChanged += (_, _) => save.IsEnabled = Settings.IsProfileName(nameBox.Text);
        stack.Children.Add(Ui.Row("今の設定に名前を付ける", Ui.Horizontal(nameBox, save)));
        if (_profileNote.Length > 0)
            stack.Children.Add(Ui.Row("", Ui.Weak(_profileNote)));
        return stack;
    }

    /// <summary>#RRGGBB を入れる欄と色見本。形が正しいときだけ効かせる。</summary>
    UIElement ColorRow(string label, string value, Action<string> set)
    {
        var swatch = new Border { Width = 22, Height = 22, Margin = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
        swatch.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        swatch.Background = (Brush)new BrushConverter().ConvertFromString(value)!;
        var box = new TextBox { Text = value, Width = 90, MaxLength = 7 };
        box.TextChanged += (_, _) =>
        {
            if (!Settings.IsColor(box.Text))
                return;
            set(box.Text.ToUpperInvariant());
            swatch.Background = (Brush)new BrushConverter().ConvertFromString(box.Text)!;
            Changed();
        };
        return Ui.Row(label, Ui.Horizontal(box, swatch));
    }

    /// <summary>位置を数で入れる欄。今いるモニターの作業領域の左上からの DIP。Enter か欄を離れたときに動かす。</summary>
    static UIElement PositionRow(string name)
    {
        var pos = App.Current.Panels.Position(name);
        var x = new TextBox { Width = 70, Text = pos?.X.ToString(CultureInfo.InvariantCulture) ?? "", IsEnabled = pos is not null };
        var y = new TextBox { Width = 70, Text = pos?.Y.ToString(CultureInfo.InvariantCulture) ?? "", IsEnabled = pos is not null, Margin = new Thickness(8, 0, 0, 0) };
        var note = Ui.Weak(pos is null ? "パネルを出しているときに変えられます" : "画面の左上からの位置(Enter で動かす)");
        note.Margin = new Thickness(8, 0, 0, 0);
        void Apply()
        {
            if (double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vx)
                && double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
                App.Current.Panels.SetPosition(name, vx, vy);
            if (App.Current.Panels.Position(name) is { } p)
            {
                x.Text = p.X.ToString(CultureInfo.InvariantCulture);
                y.Text = p.Y.ToString(CultureInfo.InvariantCulture);
            }
        }
        foreach (var box in new[] { x, y })
        {
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Apply(); };
            box.LostFocus += (_, _) => Apply();
        }
        return Ui.Row("位置 X・Y", Ui.Horizontal(x, y, note));
    }

    void BuildPad(StackPanel stack, Settings s)
    {
        var hex = new TextBox { Text = s.PressedColor, Width = 90, MaxLength = 7 };
        var swatches = Ui.Horizontal();
        foreach (var c in new[] { "#FFC83D", "#FF5A5A", "#4FD1FF", "#7CFC7C", "#FF8AD8", "#FFFFFF" })
        {
            var b = new Button { Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 0), Background = (Brush)new BrushConverter().ConvertFromString(c)!, ToolTip = c };
            b.Click += (_, _) => hex.Text = c;
            swatches.Children.Add(b);
        }
        hex.TextChanged += (_, _) =>
        {
            if (Regex.IsMatch(hex.Text, "^#[0-9A-Fa-f]{6}$"))
            {
                s.PressedColor = hex.Text.ToUpperInvariant();
                Changed();
            }
        };
        swatches.Children.Add(hex);
        stack.Children.Add(Ui.Row("押したときの色", swatches));
        stack.Children.Add(Ui.Row("ボタンの表記", Ui.Combo([("pad", "パッドに合わせる"), ("xbox", "Xbox 表記"), ("playstation", "PlayStation 表記")], s.LabelStyle,
            v => { s.LabelStyle = v; Changed(); })));
        stack.Children.Add(Ui.Slider("トリガーを押したとみなす値", 0.10, 0.90, 0.05, s.TriggerThreshold, "0.00", v => { s.TriggerThreshold = v; Changed(); }));
        stack.Children.Add(Ui.Section("パッドの型ごとのスキン"));
        var pads = App.Current.Bus.Latest.Pads.Where(p => p.IsMapped).GroupBy(p => p.Guid).Select(g => (g.Key, g.First().Name)).ToList();
        foreach (var guid in s.Devices.Keys.Where(k => pads.All(p => p.Key != k)))
            pads.Add((guid, "(今つながっていない型) " + guid[..Math.Min(8, guid.Length)]));
        if (pads.Count == 0)
            stack.Children.Add(Ui.Weak("パッドをつなぐと、ここで型ごとにスキンを選べます"));
        foreach (var (guid, padName) in pads)
        {
            stack.Children.Add(Ui.Row(padName, Ui.Combo(
                [("auto", "自動"), ("xbox", "Xbox 型"), ("playstation", "PlayStation 型"), ("switch_pro", "Switch Pro 型"), ("joycon_pair", "Joy-Con ペア型"), ("arcade", "アケコン型"), ("generic", "汎用型")],
                s.Devices.GetValueOrDefault(guid) ?? "auto",
                v => { if (v == "auto") s.Devices.Remove(guid); else s.Devices[guid] = v; Changed(); }), 260));
        }
    }

    void BuildHistory(StackPanel stack, Settings s)
    {
        var h = s.History;
        stack.Children.Add(Ui.Slider("行数", 5, 40, 1, h.Rows, "0", v => { h.Rows = (int)v; Changed(); }));
        stack.Children.Add(Ui.Row("フレームの基準", Ui.Combo([("60", "60"), ("120", "120"), ("144", "144"), ("240", "240"), ("0", "ミリ秒")],
            h.FrameBase.ToString(CultureInfo.InvariantCulture), v => { h.FrameBase = int.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        stack.Children.Add(Ui.Row("方向の元", Ui.Combo([("both", "十字キーと左スティック"), ("dpad", "十字キーだけ"), ("stick", "左スティックだけ")],
            h.DirectionSource, v => { h.DirectionSource = v; Changed(); })));
        stack.Children.Add(Ui.Slider("方向とみなす倒し具合", 0.20, 0.90, 0.05, h.StickThreshold, "0.00", v => { h.StickThreshold = v; Changed(); }));
        stack.Children.Add(Ui.Row("方向の出し方", Ui.Combo([("arrow", "矢印"), ("number", "数字")], h.DirectionStyle, v => { h.DirectionStyle = v; Changed(); })));
        stack.Children.Add(Ui.Section("出すボタン"));
        var wrap = new WrapPanel();
        foreach (var key in Settings.AllHistoryButtonKeys.Where(k => !k.StartsWith("Dpad", StringComparison.Ordinal)))
        {
            wrap.Children.Add(Ui.Check(Labels.For(key, Skin.Xbox, "xbox", null), s.HistoryButtonKeys.Contains(key), on =>
            {
                var list = s.HistoryButtonKeys.ToList();
                if (on && !list.Contains(key)) list.Add(key);
                if (!on) list.Remove(key);
                h.Buttons = list.OrderBy(k => Settings.AllHistoryButtonKeys.ToList().IndexOf(k)).ToList();
                Changed();
            }));
        }
        stack.Children.Add(wrap);
        stack.Children.Add(Ui.Weak("変えた設定は次の行から効きます。十字キーは方向に入るので、ボタンとしては出しません"));
    }

    void BuildStick(StackPanel stack, Settings s)
    {
        stack.Children.Add(Ui.Slider("軌跡の長さ(ミリ秒)", 0, 2000, 50, s.TrailMs, "0", v => { s.TrailMs = (int)v; Changed(); }));
        stack.Children.Add(Ui.Slider("デッドゾーンの円", 0, 0.5, 0.01, s.Deadzone, "0.00", v => { s.Deadzone = v; Changed(); }));
        stack.Children.Add(Ui.Check("生の値を出す", s.ShowRaw, v => { s.ShowRaw = v; Changed(); }));
        stack.Children.Add(Ui.Weak("デッドゾーンの円は表示だけで、値は変えません"));
    }

    void BuildStats(StackPanel stack, Settings s)
    {
        var names = new Dictionary<string, string> { ["session"] = "セッション", ["inputs"] = "入力", ["last60"] = "直近 60 秒", ["peak"] = "60 秒の最大", ["peak1s"] = "1 秒の最大(連打)", ["top5"] = "上位 5 つ" };
        foreach (var key in Settings.StatsRowNames)
        {
            stack.Children.Add(Ui.Check(names[key], s.StatsRows.Contains(key), on =>
            {
                if (on && !s.StatsRows.Contains(key)) s.StatsRows.Add(key);
                if (!on) s.StatsRows.Remove(key);
                s.StatsRows = [.. Settings.StatsRowNames.Where(s.StatsRows.Contains)];
                Changed();
            }));
        }
    }

    public void Render(PadSnapshot s, ulong nowNs)
    {
        var view = _previews[Selected];
        if (s.Version == _lastVersion && !_dirty && !view.Animating(s))
            return;
        _lastVersion = s.Version;
        _dirty = false;
        view.Render(s, App.Current.Settings, nowNs);
    }
}
