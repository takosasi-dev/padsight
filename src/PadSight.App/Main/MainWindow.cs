// メインの画面の外枠(画面設計書 §4.5〜§4.9・§4.8b)。診断のタブは PadLab を統合した物(§4.7)。6 つのタブ、上の帯(知らせ)、パネルの表示の状態を持つ。
// × は隠すだけでトレイに残る(FR-14)。Ctrl+1〜6 でタブを切り替え、Esc は測定やホットキーの受け付けを止める(§5)。
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
    readonly System.Windows.Shapes.Ellipse _stateDot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
    readonly HomeTab _home = new();
    readonly PanelsTab _panels = new();
    readonly DiagnosticsTab _diag = new();
    readonly StatsTab _stats = new();
    readonly SettingsTab _settings = new();
    readonly MeasureTab _measure = new();

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
        _tabs.Items.Add(new TabItem { Header = "診断", Content = _diag });
        _tabs.Items.Add(new TabItem { Header = "統計", Content = _stats });
        _tabs.Items.Add(new TabItem { Header = "設定", Content = _settings });
        _tabs.Items.Add(new TabItem { Header = "計測", Content = _measure });
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != _tabs) return;
            if (_tabs.SelectedItem is TabItem { Content: StatsTab st }) st.Reload();
            if (_tabs.SelectedItem is not TabItem { Content: MeasureTab }) _measure.Cancel(); // 描画が止まると時間を測れないので、離れたら止める
            if (_tabs.SelectedItem is not TabItem { Content: DiagnosticsTab }) _diag.Cancel(); // 離れたら測定と細かい値の積み上げを止める
            if (_tabs.SelectedItem is not TabItem { Content: SettingsTab }) _settings.CancelCapture(); // 受け付けたまま離れると、Ctrl+数字がホットキーになってしまう
        };

        // パネルの状態はタブの帯の右端に、点と文字で出す(色だけにしない。§6)
        _state.Margin = new Thickness(8, 0, 0, 0);
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stateRow.Children.Add(_stateDot);
        stateRow.Children.Add(_state);
        var top = new Grid();
        top.Children.Add(_tabs);
        top.Children.Add(new Border { Child = stateRow, Height = 42, Padding = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
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
        Closing += (_, e) => { e.Cancel = true; _diag.Cancel(); _measure.Cancel(); _settings.CancelCapture(); Hide(); };
        // 受け付けの途中でゲームへ切り替えると、その間ホットキーが全部外れたままになるので取り消す
        Deactivated += (_, _) => _settings.CancelCapture();
    }

    /// <summary>診断のタブを開き、そのページを選ぶ(0 から。1 はスティック)。帯のボタンから使う。</summary>
    public void SelectDiag(int page)
    {
        _tabs.SelectedItem = _tabs.Items.Cast<TabItem>().First(t => t.Content == _diag);
        _diag.SelectPage(page);
    }

    /// <summary>タブを選ぶ(0 から。2 は診断)。帯のボタンから使う。</summary>
    public void SelectTab(int index) => _tabs.SelectedIndex = index;

    void UpdateState()
    {
        var p = App.Current.Panels;
        _state.Text = "パネル: " + (p.Arranging ? "配置を直しています" : p.Visible ? "表示中" : "隠しています");
        _stateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, p.Arranging ? "WarningBrush" : p.Visible ? "SuccessBrush" : "SubTextBrush");
    }

    /// <summary>帯。左の太い線を種類の色(危険・警告・弱い)にし、文字は地との差が足りる本文の色で書く。ボタンは右端。</summary>
    void RebuildBanners()
    {
        _banners.Children.Clear();
        foreach (var b in App.Current.Banners)
        {
            var t = Ui.Text(b.Text);
            t.Margin = new Thickness(0, 4, 12, 4);
            var row = new DockPanel { LastChildFill = true };
            if (b.ActionText is { } label && b.OnAction is { } act)
            {
                var btn = Ui.Button(label, act);
                btn.Margin = new Thickness(0);
                btn.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(btn, Dock.Right);
                row.Children.Add(btn);
            }
            row.Children.Add(t);
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 12, 2) };
            bar.SetResourceReference(Border.BackgroundProperty, b.Kind == "Weak" ? "SubTextBrush" : b.Kind + "Brush");
            var inner = new DockPanel();
            DockPanel.SetDock(bar, Dock.Left);
            inner.Children.Add(bar);
            inner.Children.Add(row);
            var border = new Border { Child = inner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8, 20, 8) };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            _banners.Children.Add(border);
        }
    }

    public void Render(PadSnapshot s, ulong nowNs)
    {
        switch (_tabs.SelectedItem)
        {
            case TabItem { Content: HomeTab h }: h.Render(s, nowNs, IsActive); break;
            case TabItem { Content: PanelsTab p }: p.Render(s, nowNs); break;
            case TabItem { Content: DiagnosticsTab d }: d.Render(s, nowNs); break;
            case TabItem { Content: StatsTab st }: st.Render(s); break;
            case TabItem { Content: MeasureTab m }: m.Render(s, nowNs); break;
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
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D6)
        {
            _tabs.SelectedIndex = e.Key - Key.D1;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _diag.Cancel();
            _measure.Cancel();
            _home.CancelCheck();
        }
    }
}
