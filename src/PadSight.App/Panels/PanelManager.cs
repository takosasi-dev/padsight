// パネル 4 枚の窓(OverlayWindow)を持ち、出し入れ・配置を直す・位置と大きさの記憶・画面外からの復帰を行う(仕様書 FR-9〜FR-13)。
// 描くのは App の描画のループから Render を呼ばれたときだけ(D-9)。
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OverlayCore;
using PadSight.Core;

namespace PadSight.App.Panels;

public sealed class PanelManager
{
    public const double MaxFraction = 0.6; // INV-15
    const double Margin = 24;

    public sealed record Entry(string Name, string Title, PanelView View, OverlayWindow Window);

    readonly Func<Settings> _settings;
    readonly Action _save;
    readonly DispatcherTimer _arrangeTimeout;
    readonly List<Entry> _entries = [];
    long _lastVersion = -1;
    bool _placed;

    public PanelManager(Func<Settings> settings, Action save)
    {
        _settings = settings;
        _save = save;
        var accent = Draw.Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)));
        Add("pad", "パッド表示", new PadView(), accent);
        Add("history", "入力履歴", new HistoryView(), accent);
        Add("stick", "スティック", new StickView(), accent);
        Add("stats", "統計", new StatsView(), accent);
        _arrangeTimeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _arrangeTimeout.Tick += (_, _) => SetArranging(false); // 5 分たったら自動で終わる(FR-11)
        SystemEvents.DisplaySettingsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RescueAll);
    }

    public IReadOnlyList<Entry> Entries => _entries;
    public bool Visible { get; private set; }
    public bool Arranging { get; private set; }
    public event Action? StateChanged;

    void Add(string name, string title, PanelView view, Brush accent)
    {
        var w = new OverlayWindow(title, view, new Size(view.Width, view.Height), "完了", accent);
        w.ArrangeDoneRequested += (_, _) => SetArranging(false);
        w.Moved += win => SavePlacement(name, win);
        w.ScaleRequested += (win, percent) =>
        {
            var p = _settings().Panels[name];
            p.Scale = Math.Clamp(OverlayWindow.ClampScale(percent, win.BaseSize, MonitorInfo.ForWindow(win), MaxFraction), 50, 300);
            Apply(name);
            _save();
        };
        w.DpiChanged += (_, _) => Apply(name);
        _entries.Add(new Entry(name, title, view, w));
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        if (!visible && Arranging)
            SetArranging(false);
        ApplyAll();
        StateChanged?.Invoke();
    }

    public void Toggle() => SetVisible(!Visible);

    bool _suspended;

    /// <summary>トレイのメニューを開いている間だけ、表示の状態を変えずにパネルを隠す(メニューにパネルが被らないように)。</summary>
    public void Suspend(bool on)
    {
        _suspended = on;
        ApplyAll();
    }

    public void SetArranging(bool on)
    {
        if (on && !Visible)
            SetVisible(true);
        Arranging = on;
        foreach (var e in _entries)
            e.Window.Arranging = on;
        if (on) _arrangeTimeout.Start(); else _arrangeTimeout.Stop();
        StateChanged?.Invoke();
    }

    /// <summary>パネルごとの表示・大きさ・不透明度・背景の濃さを今の設定に合わせる(FR-10)。変えたらすぐ効く。</summary>
    public void ApplyAll()
    {
        foreach (var e in _entries)
            Apply(e.Name);
        if (Visible && !_placed)
        {
            _placed = true;
            foreach (var e in _entries)
                Restore(e);
        }
    }

    void Apply(string name)
    {
        var e = _entries.First(en => en.Name == name);
        var s = _settings();
        var p = s.Panels[name];
        if (name == "history")
            e.Window.BaseSize = new Size(e.View.Width, HistoryView.HeightFor(s.History.Rows));
        if (e.Window.IsLoaded)
        {
            // E-25: 上限を超えるなら、収まる一番大きい倍率に下げて設定も書き換える
            int clamped = Math.Clamp(OverlayWindow.ClampScale(p.Scale, e.Window.BaseSize, MonitorInfo.ForWindow(e.Window), MaxFraction), 50, 300);
            if (clamped != p.Scale)
            {
                AppLog.Write($"パネルの大きさを上限に合わせた: {name}");
                p.Scale = clamped;
                _save();
            }
        }
        e.Window.PanelScale = p.Scale / 100.0;
        e.Window.Opacity = p.Opacity / 100.0;
        e.Window.BackgroundOpacity = p.BackgroundOpacity / 100.0;
        bool show = Visible && p.Visible && !_suspended;
        if (show && !e.Window.IsVisible)
        {
            e.Window.ShowFaded();
            e.Window.Arranging = Arranging;
            if (_placed)
                Restore(e);
        }
        else if (show)
            e.Window.ShowFaded();
        else if (e.Window.IsVisible)
            e.Window.HideFaded();
    }

    /// <summary>覚えた位置に出す。モニターが無ければ主モニターの同じ相対位置、画面の外なら既定の位置(§9.10.3)。</summary>
    void Restore(Entry e)
    {
        if (!e.Window.IsVisible)
            return;
        var p = _settings().Panels[e.Name];
        if (p.X is double x && p.Y is double y)
        {
            var m = MonitorInfo.All().FirstOrDefault(m => m.DeviceName == p.Monitor) ?? MonitorInfo.Primary();
            e.Window.PlaceAt(m, x, y);
            e.Window.UpdateLayout();
            if (!e.Window.IsMostlyOffscreen())
                return;
            AppLog.Write($"画面の外にあったパネルを既定の位置に戻した: {e.Name}");
        }
        PlaceDefault(e);
    }

    void RescueAll()
    {
        foreach (var e in _entries.Where(e => e.Window.IsVisible))
        {
            Apply(e.Name);
            if (e.Window.IsMostlyOffscreen())
            {
                AppLog.Write($"画面の外にあったパネルを既定の位置に戻した: {e.Name}");
                PlaceDefault(e);
                SavePlacement(e.Name, e.Window);
            }
        }
    }

    public void ResetPosition(string name)
    {
        var p = _settings().Panels[name];
        p.Monitor = null;
        p.X = p.Y = null;
        _save();
        var e = _entries.First(en => en.Name == name);
        if (e.Window.IsVisible)
            PlaceDefault(e);
    }

    /// <summary>出ているパネルをまとめて置き直す。"corners" は既定の四隅、"left"・"right" は主モニターの左右の端に上から縦に並べる。</summary>
    public void ArrangePreset(string preset)
    {
        var m = MonitorInfo.Primary();
        double y = Margin, column = Margin, columnWidth = 0;
        foreach (var e in _entries.Where(e => e.Window.IsVisible))
        {
            if (preset == "corners")
                PlaceDefault(e);
            else
            {
                var p = _settings().Panels[e.Name];
                double w = e.Window.BaseSize.Width * p.Scale / 100.0, h = e.Window.BaseSize.Height * p.Scale / 100.0;
                if (y > Margin && y + h > m.WorkHeightDip - Margin)
                {
                    // 下に収まらなければ、内側に 2 列目を作る
                    column += columnWidth + 12;
                    columnWidth = 0;
                    y = Margin;
                }
                columnWidth = Math.Max(columnWidth, w);
                double x = preset == "left" ? column : m.WorkWidthDip - column - w;
                e.Window.PlaceAt(m, x, y);
                y += h + 12;
            }
            SavePlacement(e.Name, e.Window);
        }
    }

    /// <summary>今いるモニターの作業領域の左上からの位置(DIP)で置く。</summary>
    public void SetPosition(string name, double x, double y)
    {
        var e = _entries.First(en => en.Name == name);
        if (!e.Window.IsVisible)
            return;
        e.Window.PlaceAt(MonitorInfo.ForWindow(e.Window), x, y);
        if (e.Window.IsMostlyOffscreen())
            PlaceDefault(e);
        SavePlacement(name, e.Window);
    }

    /// <summary>出ているパネルの、今の位置(DIP)。出ていなければ null。</summary>
    public (double X, double Y)? Position(string name)
    {
        var e = _entries.First(en => en.Name == name);
        if (!e.Window.IsVisible)
            return null;
        var (_, x, y) = e.Window.Placement();
        return (Math.Round(x), Math.Round(y));
    }

    void PlaceDefault(Entry e)
    {
        var m = MonitorInfo.Primary();
        var p = _settings().Panels[e.Name];
        double w = e.Window.BaseSize.Width * p.Scale / 100.0, h = e.Window.BaseSize.Height * p.Scale / 100.0;
        double W = m.WorkWidthDip, H = m.WorkHeightDip;
        var (x, y) = e.Name switch
        {
            "pad" => (Margin, H - h - Margin),
            "history" => (Margin, (H - h) / 2),
            "stick" => (W - w - Margin, H - h - Margin),
            _ => (W - w - Margin, Margin),
        };
        e.Window.PlaceAt(m, x, y);
    }

    void SavePlacement(string name, OverlayWindow w)
    {
        var (m, x, y) = w.Placement();
        var p = _settings().Panels[name];
        p.Monitor = m.DeviceName;
        p.X = Math.Round(x, 1);
        p.Y = Math.Round(y, 1);
        _save();
    }

    /// <summary>描画のループから毎フレーム呼ぶ。版が変わったか、時間で変わる表示があるパネルだけ描き直す。</summary>
    public void Render(PadSnapshot s, ulong nowNs, bool force)
    {
        bool changed = s.Version != _lastVersion || force;
        _lastVersion = s.Version;
        var settings = _settings();
        foreach (var e in _entries)
        {
            if (e.Window.IsVisible && (changed || e.View.Animating(s)))
                e.View.Render(s, settings, nowNs);
        }
    }
}
