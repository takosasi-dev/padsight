// S2 パネル(画面設計書 §4.6・仕様書 FR-5・FR-10・FR-18・FR-20・FR-22・FR-24・FR-50・FR-51)。
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
    /// <summary>真ん中の列は狭いので、ラベルの列もほかのタブより狭くする。</summary>
    const double Label = 170;

    static Grid Row(string label, UIElement control) => Ui.Row(label, control, Label);

    static ComboBox Narrow(ComboBox c)
    {
        c.MinWidth = 120;
        return c;
    }

    static Grid Slider(string label, double min, double max, double step, double value, string format, Action<double> onChange) =>
        Ui.Slider(label, min, max, step, value, format, onChange, Label, 170);
    // 左の 4 枚の一覧は地の上に置く(箱にしない)。選んだ行は塗りと左の線(App.xaml)
    readonly ListBox _list = new() { Width = 140, Margin = new Thickness(14, 20, 0, 16), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
    readonly ContentControl _settingsHost = new();
    readonly Viewbox _previewBox = new() { MaxWidth = 214, MaxHeight = 260, VerticalAlignment = VerticalAlignment.Top };
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 660 }); // 広い窓でも設定の列は広げすぎず、見本をそのすぐ右に置く
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star), MinWidth = 180, MaxWidth = 250 }); // 狭い窓では見本を縮め、真ん中の設定に幅を回す
        grid.Children.Add(_list);
        var scroll = Ui.Scroll(_settingsHost);
        scroll.Padding = new Thickness(14, 20, 14, 4);
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        // 右の見本。何の絵かを上に弱い文字で書く
        var preview = new StackPanel { Margin = new Thickness(0, 20, 20, 16) };
        var caption = Ui.Weak("見本");
        caption.Margin = new Thickness(2, 0, 0, 6);
        preview.Children.Add(caption);
        var pic = Ui.Picture(_previewBox);
        pic.HorizontalAlignment = HorizontalAlignment.Stretch;
        preview.Children.Add(pic);
        Grid.SetColumn(preview, 2);
        grid.Children.Add(preview);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 余りは右端に残す
        Content = grid;
    }

    string Selected => (_list.SelectedItem as ListBoxItem)?.Tag as string ?? "pad";

    void Changed()
    {
        App.Current.Panels.ApplyAll();
        App.Current.SaveSettings();
        _dirty = true;
        _refreshEasy?.Invoke();
    }

    Action? _refreshEasy;

    void Build()
    {
        var s = App.Current.Settings;
        string name = Selected;
        var p = s.Panels[name];
        // 箱 1: プロファイルと 4 枚に共通の配色。箱 2: このパネルの出し方。箱 3: このパネルだけの設定
        var common = new StackPanel();
        var stack = common;
        stack.Children.Add(ProfileRows());
        // FR-39: 4 枚に共通の配色テーマ。選ぶと押したときの色もおすすめの色にする
        stack.Children.Add(Row("パネルの配色", Ui.Combo(PanelTheme.Choices, s.PanelTheme, v =>
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
        var easy = Ui.Button("4 枚とも見やすくする(不透明度 100%・背景 85%)", () =>
        {
            foreach (var ps in s.Panels.Values) { ps.Opacity = 100; ps.BackgroundOpacity = 85; }
            Changed();
            Build();
        });
        // 押しても何も変わらないときは押せなくし、そう書く(2026-11-18)。つまみを動かしたら Changed で合わせ直す
        var easyNote = Ui.Weak("4 枚とも、もうこの値です");
        _refreshEasy = () =>
        {
            bool already = s.Panels.Values.All(ps => ps.Opacity == 100 && ps.BackgroundOpacity == 85);
            easy.IsEnabled = !already;
            easyNote.Visibility = already ? Visibility.Visible : Visibility.Collapsed;
        };
        _refreshEasy();
        stack.Children.Add(Ui.Horizontal(easy, easyNote));
        var look = new StackPanel();
        stack = look;
        stack.Children.Add(Ui.Check("表示する", p.Visible, v => { p.Visible = v; Changed(); }));
        stack.Children.Add(Slider("大きさ(%)", 50, 300, 10, p.Scale, "0", v => { p.Scale = (int)v; Changed(); }));
        stack.Children.Add(Slider("不透明度(%)", 20, 100, 1, p.Opacity, "0", v => { p.Opacity = (int)v; Changed(); }));
        stack.Children.Add(Slider("背景の濃さ(%)", 0, 100, 1, p.BackgroundOpacity, "0", v => { p.BackgroundOpacity = (int)v; Changed(); }));
        stack.Children.Add(Ui.Horizontal(Ui.Button("既定の位置に戻す", () => App.Current.Panels.ResetPosition(name))));
        stack.Children.Add(PositionRow(name));
        var own = new StackPanel();
        stack = own;
        switch (name)
        {
            case "pad": BuildPad(stack, s); break;
            case "history": BuildHistory(stack, s); break;
            case "stick": BuildStick(stack, s); break;
            case "stats": BuildStats(stack, s); break;
        }
        string title = App.Current.Panels.Entries.FirstOrDefault(e => e.Name == name)?.Title ?? "";
        var page = new StackPanel();
        // 見出しが無いと、左で選んだパネルだけの設定に見えた(2026-11-18)
        page.Children.Add(Ui.Card("4 枚に共通", common));
        page.Children.Add(Ui.Card(title, look));
        page.Children.Add(Ui.Card("このパネルの設定", own));
        _settingsHost.Content = page;
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
            stack.Children.Add(Row("プロファイル", Ui.Weak("まだありません")));
        else
        {
            if (_profileChoice is null || !names.Contains(_profileChoice))
                _profileChoice = names[0];
            stack.Children.Add(Row("プロファイル", Ui.Horizontal(
                Narrow(Ui.Combo([.. names.Select(n => (n, n))], _profileChoice, v => _profileChoice = v)),
                Ui.Button("当てる", () =>
                {
                    string name = _profileChoice!;
                    App.Current.ReplaceSettings(s.WithProfile(name, out var reset));
                    foreach (var key in reset)
                        AppLog.Write("プロファイルの値を既定に戻した: " + key);
                    _profileNote = $"『{name}』を当てました";
                    Build();
                }),
                Ui.Danger("消す", () =>
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
        stack.Children.Add(Row("今の設定に名前を付ける", Ui.Horizontal(nameBox, save)));
        if (_profileNote.Length > 0)
            stack.Children.Add(Row("", Ui.Weak(_profileNote)));
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
        return Row(label, Ui.Horizontal(box, swatch));
    }

    /// <summary>位置を数で入れる欄。今いるモニターの作業領域の左上からの DIP。Enter か欄を離れたときに動かす。</summary>
    static UIElement PositionRow(string name)
    {
        var pos = App.Current.Panels.Position(name);
        // 出ていない間は、覚えている位置を(変えられない欄で)見せる。空の欄だと今どこにあるのか分からなかった(2026-11-18)
        var saved = App.Current.Settings.Panels[name];
        string Show(double? v) => v is double d ? Math.Round(d).ToString(CultureInfo.InvariantCulture) : "";
        var x = new TextBox { Width = 70, Text = pos is { } p0 ? Show(p0.X) : Show(saved.X), IsEnabled = pos is not null };
        var y = new TextBox { Width = 70, Text = pos is { } p1 ? Show(p1.Y) : Show(saved.Y), IsEnabled = pos is not null, Margin = new Thickness(8, 0, 0, 0) };
        var note = Ui.Weak(pos is not null ? "画面の左上からの位置(Enter で動かす)"
            : saved.X is null ? "既定の位置(パネルを出すと変えられます)"
            : "覚えている位置(パネルを出すと変えられます)");
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
        return Row("位置 X・Y", Ui.Horizontal(x, y, note));
    }

    void BuildPad(StackPanel stack, Settings s)
    {
        var hex = new TextBox { Text = s.PressedColor, Width = 90, MaxLength = 7 };
        var swatches = Ui.Horizontal();
        foreach (var c in new[] { "#FFC83D", "#FF5A5A", "#4FD1FF", "#7CFC7C", "#FF8AD8", "#FFFFFF" })
        {
            var b = new Button { Width = 26, Height = 26, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), Background = (Brush)new BrushConverter().ConvertFromString(c)!, ToolTip = c };
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
        stack.Children.Add(Row("押したときの色", swatches));
        stack.Children.Add(Row("ボタンの表記", Ui.Combo([("pad", "パッドに合わせる"), ("xbox", "Xbox 表記"), ("playstation", "PlayStation 表記")], s.LabelStyle,
            v => { s.LabelStyle = v; Changed(); })));
        stack.Children.Add(Slider("トリガーを押したとみなす値", 0.10, 0.90, 0.05, s.TriggerThreshold, "0.00", v => { s.TriggerThreshold = v; Changed(); }));
        stack.Children.Add(Ui.SubSection("パッドの型ごとのスキン"));
        var pads = App.Current.Bus.Latest.Pads.Where(p => p.IsMapped).GroupBy(p => p.Guid).Select(g => (g.Key, g.First().Name)).ToList();
        foreach (var guid in s.Devices.Keys.Where(k => pads.All(p => p.Key != k)))
            pads.Add((guid, "(今つながっていない型) " + guid[..Math.Min(8, guid.Length)]));
        if (pads.Count == 0)
            stack.Children.Add(Ui.Weak("パッドをつなぐと、ここで型ごとにスキンを選べます"));
        foreach (var (guid, padName) in pads)
        {
            stack.Children.Add(Row(padName, Ui.Combo(
                [("auto", "自動"), ("xbox", "Xbox 型"), ("playstation", "PlayStation 型"), ("switch_pro", "Switch Pro 型"), ("joycon_pair", "Joy-Con ペア型"), ("arcade", "アケコン型"), ("generic", "汎用型")],
                s.Devices.GetValueOrDefault(guid) ?? "auto",
                v => { if (v == "auto") s.Devices.Remove(guid); else s.Devices[guid] = v; Changed(); })));
        }
    }

    void BuildHistory(StackPanel stack, Settings s)
    {
        var h = s.History;
        stack.Children.Add(Slider("行数", 5, 40, 1, h.Rows, "0", v => { h.Rows = (int)v; Changed(); }));
        stack.Children.Add(Row("フレームの基準", Ui.Combo([("60", "60"), ("120", "120"), ("144", "144"), ("240", "240"), ("0", "ミリ秒")],
            h.FrameBase.ToString(CultureInfo.InvariantCulture), v => { h.FrameBase = int.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        stack.Children.Add(Row("方向の元", Ui.Combo([("both", "十字キーと左スティック"), ("dpad", "十字キーだけ"), ("stick", "左スティックだけ")],
            h.DirectionSource, v => { h.DirectionSource = v; Changed(); })));
        stack.Children.Add(Slider("方向とみなす倒し具合", 0.20, 0.90, 0.05, h.StickThreshold, "0.00", v => { h.StickThreshold = v; Changed(); }));
        stack.Children.Add(Row("方向の出し方", Ui.Combo([("arrow", "矢印"), ("number", "数字")], h.DirectionStyle, v => { h.DirectionStyle = v; Changed(); })));
        // FR-50・FR-51。0 は「まとめない」、ほかは最初の押下からの窓
        stack.Children.Add(Row("同時押しをまとめる", Ui.Combo(
            [("0", "まとめない"), .. Enumerable.Range(1, 5).Select(i => (i.ToString(CultureInfo.InvariantCulture), $"{i}/60 秒以内"))],
            h.MergeFrames.ToString(CultureInfo.InvariantCulture), v => { h.MergeFrames = int.Parse(v, CultureInfo.InvariantCulture); Changed(); })));
        stack.Children.Add(Ui.Check("コマンドを見分ける", h.Motions, v => { h.Motions = v; Changed(); }));
        stack.Children.Add(Ui.SubSection("出すボタン"));
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
        stack.Children.Add(Ui.Weak("コマンドは、ボタンの前 20/60 秒の方向の並びから 41236・63214・623・421・236・214・22 を見分け、数字で出します"));
    }

    void BuildStick(StackPanel stack, Settings s)
    {
        stack.Children.Add(Slider("軌跡の長さ(ミリ秒)", 0, 2000, 50, s.TrailMs, "0", v => { s.TrailMs = (int)v; Changed(); }));
        stack.Children.Add(Slider("デッドゾーンの円", 0, 0.5, 0.01, s.Deadzone, "0.00", v => { s.Deadzone = v; Changed(); }));
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
