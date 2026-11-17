// メインの画面の外枠(画面設計書 §4.5〜§4.9)。5 つのタブ、上の帯(知らせ)、パネルの表示の状態を持つ。
// × は隠すだけでトレイに残る(FR-14)。Ctrl+1〜5 でタブを切り替え、Esc は測定やホットキーの受け付けを止める(§5)。
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PadSight.App.Main;
using PadSight.Core;

namespace PadSight.App;

public sealed class MainWindow : Window
{
    readonly TabControl _tabs = new();
    readonly StackPanel _banners = new();
    readonly TextBlock _state = Ui.Weak();
    readonly HomeTab _home = new();
    readonly PanelsTab _panels = new();
    readonly DiagTab _diag = new();
    readonly StatsTab _stats = new();
    readonly SettingsTab _settings = new();

    public MainWindow()
    {
        Title = "PadSight";
        Icon = App.Icon;
        Width = 960;
        Height = 680;
        MinWidth = 780;
        MinHeight = 520;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        _tabs.Items.Add(new TabItem { Header = "ホーム", Content = _home });
        _tabs.Items.Add(new TabItem { Header = "パネル", Content = _panels });
        _tabs.Items.Add(new TabItem { Header = "スティック診断", Content = _diag });
        _tabs.Items.Add(new TabItem { Header = "統計", Content = _stats });
        _tabs.Items.Add(new TabItem { Header = "設定", Content = _settings });
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs && _tabs.SelectedItem is TabItem { Content: StatsTab st }) st.Reload(); };

        _state.HorizontalAlignment = HorizontalAlignment.Right;
        _state.Margin = new Thickness(0, 4, 12, 0);
        var top = new Grid();
        top.Children.Add(_tabs);
        top.Children.Add(new Border { Child = _state, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var dock = new DockPanel();
        DockPanel.SetDock(_banners, Dock.Top);
        dock.Children.Add(_banners);
        dock.Children.Add(top);
        Content = dock;

        App.Current.Banners.CollectionChanged += (_, _) => RebuildBanners();
        App.Current.Panels.StateChanged += UpdateState;
        RebuildBanners();
        UpdateState();
        PreviewKeyDown += OnKey;
        Closing += (_, e) => { e.Cancel = true; _diag.Cancel(); Hide(); };
    }

    void UpdateState() => _state.Text = "パネル: " + (App.Current.Panels.Arranging ? "配置を直しています" : App.Current.Panels.Visible ? "表示中" : "隠しています");

    void RebuildBanners()
    {
        _banners.Children.Clear();
        foreach (var b in App.Current.Banners)
        {
            var t = Ui.Text(b.Text);
            t.Margin = new Thickness(16, 6, 16, 6);
            UIElement child = b.ActionText is { } label && b.OnAction is { } act ? Ui.Horizontal(t, Ui.Button(label, act)) : t;
            var border = new Border { Child = child, BorderThickness = new Thickness(0, 0, 0, 1) };
            border.SetResourceReference(Border.BorderBrushProperty, b.Kind == "Weak" ? "BorderBrush" : b.Kind + "Brush");
            if (b.Kind != "Weak")
                t.SetResourceReference(TextBlock.ForegroundProperty, b.Kind + "Brush");
            _banners.Children.Add(border);
        }
    }

    public void Render(PadSnapshot s, ulong nowNs)
    {
        switch (_tabs.SelectedItem)
        {
            case TabItem { Content: HomeTab h }: h.Render(s, nowNs, IsActive); break;
            case TabItem { Content: PanelsTab p }: p.Render(s, nowNs); break;
            case TabItem { Content: DiagTab d }: d.Render(s); break;
            case TabItem { Content: StatsTab st }: st.Render(s); break;
        }
        if (_tabs.SelectedItem is not TabItem { Content: HomeTab })
            _home.TrackBackground(s, IsActive); // 裏の入力の確かめは、タブを移っても続ける
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_settings.CapturingHotkey)
        {
            _settings.OnCaptureKey(e);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D5)
        {
            _tabs.SelectedIndex = e.Key - Key.D1;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _diag.Cancel();
            _home.CancelCheck();
        }
    }
}
