// 起動から終了までのまとめ役。単一インスタンス、設定とセッションの読み書き、SDL のスレッド、パネル、ホットキー、トレイ、描画のループを持つ。
// 画面(メインの画面・初回の案内)から呼ばれる操作もここに集める。
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using OverlayCore;
using PadSight.App.Panels;
using PadSight.Core;
using PadSight.Input;

namespace PadSight.App;

public sealed record Banner(string Key, string Text, string Kind);

public partial class App : Application
{
    public const int HotkeyToggle = 1, HotkeyArrange = 2, HotkeyClear = 3;

    public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadSight");
    public static string Version { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static new App Current => (App)Application.Current;

    Mutex? _single;
    SettingsStore _store = null!;
    SdlInputThread _input = null!;
    HotkeyHost _hotkeys = null!;
    TaskbarIcon _tray = null!;
    MainWindow? _main;
    MenuItem _menuToggle = null!, _menuArrange = null!;
    string _tooltip = "";
    Measure? _measure;

    public Settings Settings { get; private set; } = new();
    public SessionStore Sessions { get; private set; } = null!;
    public SnapshotBus Bus { get; } = new();
    public PanelManager Panels { get; private set; } = null!;
    public ObservableCollection<Banner> Banners { get; } = [];
    public Dictionary<int, string> HotkeyStatus { get; } = [];
    public SettingsStore Store => _store;
    public string MappingFile => Path.Combine(DataDir, "gamecontrollerdb.txt");

    /// <summary>トレイとメインの画面のアイコン(自分で描いたパッドの形。メーカーのロゴは使わない)。</summary>
    public static ImageSource Icon { get; } = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/padsight.ico"));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, @"Local\PadSight.SingleInstance", out bool created);
        if (!created)
        {
            // D-18: 1 つ目の窓は前に出さない(他のプロセスの窓に触ることになる)
            MessageBox.Show("PadSight はすでに起動しています。トレイのアイコンから開けます", "PadSight");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, ex) => AppLog.Error("UI", ex.Exception); // 最上位のハンドラ。ログに残す
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { if (ex.ExceptionObject is Exception x) AppLog.Error("全体", x); };

        AppLog.Init(Path.Combine(DataDir, "applog"), DateTime.UtcNow);
        AppLog.Write($"起動 PadSight {Version}");
        _store = new SettingsStore(DataDir);
        _store.Backup(DateTime.UtcNow);
        var (settings, result) = _store.Load(DateTime.UtcNow);
        Settings = settings;
        Theme.Apply(Settings.Theme);
        if (result == SettingsStore.LoadResult.Broken)
            SetBanner("settings", "設定を読めなかったので、初めの値で起動しました", "Warning");
        if (_store.ReadOnly)
            SetBanner("newer", "新しい版の PadSight で保存された設定です。読み取りだけで開いています", "Warning");

        Sessions = new SessionStore(DataDir);
        Sessions.SaveFailedChanged += failed => Dispatcher.BeginInvoke(() =>
        {
            if (failed) SetBanner("save", "統計を保存できませんでした。ディスクの空きを確かめてください", "Warning");
            else ClearBanner("save");
        });
        Sessions.RecoverCurrent();

        AppLog.Write("起動: 設定とセッションを読んだ");
        Panels = new PanelManager(() => Settings, SaveSettings);
        Panels.StateChanged += UpdateTrayMenu;
        _input = new SdlInputThread(EngineConfig.From(Settings), InputOptionsFrom(Settings), MappingFile,
            Bus.Publish, Sessions.Append, Sessions.WriteCurrent, AppLog.Write);
        _input.Start();
        AppLog.Write("起動: パネルを作った");

        _hotkeys = new HotkeyHost();
        _hotkeys.Pressed += OnHotkey;
        CreateTray();
        AppLog.Write("起動: トレイを出した");
        RegisterHotkeys();
        if (result == SettingsStore.LoadResult.Broken)
            _tray.ShowNotification("PadSight", "設定を読めなかったので、初めの値で起動しました");

        if (Environment.GetEnvironmentVariable("PADSIGHT_MEASURE") == "1")
            _measure = new Measure();
        System.Windows.Media.CompositionTarget.Rendering += OnRender;

        if (result == SettingsStore.LoadResult.Missing)
        {
            var first = new FirstRunWindow();
            first.Started += () =>
            {
                SaveSettings();
                Panels.SetVisible(true);
            };
            first.Show();
        }
        else
            Panels.SetVisible(Settings.VisibleOnStart);
        AppLog.Write("起動: パネルを出した");
        if (!e.Args.Contains("--tray"))
            OpenMain();
        AppLog.Write("起動を終えた");
    }

    static InputOptions InputOptionsFrom(Settings s) => new(s.InputWgi, s.InputHidapiPlayStation, s.InputHidapiSwitch);

    /// <summary>設定を変えた時点で保存し、SDL のスレッドにも渡す(FR-33)。</summary>
    public void SaveSettings()
    {
        _store.Save(Settings);
        var cfg = EngineConfig.From(Settings);
        _input.Post(engine => engine.Configure(cfg));
    }

    public void PostToInput(Action<InputEngine> command) => _input.Post(command);

    /// <summary>入力の取り方を変えてつなぎ直す(FR-8)。</summary>
    public void ReconnectInput()
    {
        SetBanner("reconnect", "入力の取り方を変えたので、パッドをつなぎ直しています", "Weak");
        _input.Reconnect(InputOptionsFrom(Settings));
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += (_, _) => { t.Stop(); ClearBanner("reconnect"); };
        t.Start();
    }

    public void ClearHistory() => _input.Post(engine => engine.ClearHistory());

    public void SetBanner(string key, string text, string kind)
    {
        ClearBanner(key);
        Banners.Add(new Banner(key, text, kind));
    }

    public void ClearBanner(string key)
    {
        foreach (var b in Banners.Where(b => b.Key == key).ToList())
            Banners.Remove(b);
    }

    public void ApplyTheme() => Theme.Apply(Settings.Theme);

    /// <summary>3 つのホットキーを登録し直す。失敗しても別の組み合わせへ振り替えない(§9.11)。</summary>
    public void RegisterHotkeys()
    {
        foreach (var (id, combo) in new[] { (HotkeyToggle, Settings.HotkeyToggle), (HotkeyArrange, Settings.HotkeyArrange), (HotkeyClear, Settings.HotkeyClearHistory) })
        {
            ClearBanner("hotkey" + id);
            if (combo is null)
            {
                _hotkeys.Unregister(id);
                HotkeyStatus[id] = "使っていません";
                continue;
            }
            bool ok = _hotkeys.Register(id, combo);
            HotkeyStatus[id] = ok ? "登録済み" : "他のアプリが使っています";
            AppLog.Write($"ホットキー {id} {combo}: {(ok ? "登録した" : "登録できなかった")}");
            if (!ok)
            {
                string msg = $"{combo} は他のアプリが使っています";
                SetBanner("hotkey" + id, msg, "Warning");
                _tray.ShowNotification("PadSight", msg);
            }
        }
    }

    public void UnregisterHotkeys()
    {
        foreach (var id in new[] { HotkeyToggle, HotkeyArrange, HotkeyClear })
            _hotkeys.Unregister(id);
    }

    void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyToggle: Panels.Toggle(); break;
            case HotkeyArrange: Panels.SetArranging(!Panels.Arranging); break;
            case HotkeyClear: ClearHistory(); break;
        }
    }

    public void OpenMain()
    {
        _main ??= new MainWindow();
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void Quit()
    {
        AppLog.Write("終了");
        System.Windows.Media.CompositionTarget.Rendering -= OnRender;
        _input.Stop();      // 今のセッションを確定して SDL を止める
        Sessions.Dispose(); // 残りの書き込みを終える
        _hotkeys.Dispose();
        _tray.Dispose();
        _single?.ReleaseMutex();
        Shutdown();
    }

    void CreateTray()
    {
        _menuToggle = new MenuItem { Header = "パネルを表示" };
        _menuToggle.Click += (_, _) => Panels.Toggle();
        _menuArrange = new MenuItem { Header = "配置を直す" };
        _menuArrange.Click += (_, _) => Panels.SetArranging(!Panels.Arranging);
        var clear = new MenuItem { Header = "入力履歴を消す" };
        clear.Click += (_, _) => ClearHistory();
        var open = new MenuItem { Header = "メインの画面を開く" };
        open.Click += (_, _) => OpenMain();
        var exit = new MenuItem { Header = "終了" };
        exit.Click += (_, _) => Quit();
        var menu = new ContextMenu();
        menu.Opened += (_, _) => Panels.Suspend(true);
        menu.Closed += (_, _) => Panels.Suspend(false);
        foreach (var item in new[] { _menuToggle, _menuArrange, clear, open, exit })
            menu.Items.Add(item);
        _tray = new TaskbarIcon
        {
            ToolTipText = "PadSight",
            ContextMenu = menu,
            IconSource = Icon,
        };
        _tray.TrayMouseDoubleClick += (_, _) => OpenMain();
        _tray.ForceCreate();
        UpdateTrayMenu();
    }

    void UpdateTrayMenu()
    {
        _menuToggle.Header = Panels.Visible ? "パネルを隠す" : "パネルを表示";
        _menuArrange.Header = Panels.Arranging ? "配置を終える" : "配置を直す";
    }

    void OnRender(object? sender, EventArgs e)
    {
        var s = Bus.Latest;
        ulong now = SnapshotBus.NowNs(s);
        Panels.Render(s, now, force: false);
        if (_main is { IsVisible: true })
            _main.Render(s, now);
        string tip = !Panels.Visible ? "PadSight  パネルを隠しています" : s.Pad is { } p ? $"PadSight  {p.Name}" : "PadSight  未接続";
        if (tip != _tooltip)
        {
            _tooltip = tip;
            _tray.ToolTipText = tip;
        }
        if (s.Status == InputStatus.Failed && !Banners.Any(b => b.Key == "sdl"))
        {
            string msg = $"入力の部品を準備できませんでした: {s.Error}";
            SetBanner("sdl", msg, "Error");
            _tray.ShowNotification("PadSight", msg);
        }
        else if (s.Status != InputStatus.Failed)
            ClearBanner("sdl");
        if (s.DuplicateWarning) SetBannerOnce("dup", "同じ操作で 2 台が動いています。表示するパッドを固定してください", "Warning");
        else ClearBanner("dup");
        _measure?.OnRender(s, now);
    }

    void SetBannerOnce(string key, string text, string kind)
    {
        if (!Banners.Any(b => b.Key == key))
            SetBanner(key, text, kind);
    }

    /// <summary>計測の設定(PADSIGHT_MEASURE=1)。NFR-1 の遅れと NFR-3 の描画の回数をログに書く。個々の押下の時刻とボタンは書かない(§9.15)。</summary>
    sealed class Measure
    {
        readonly List<double> _latencies = [];
        long _lastVersion;
        ulong _lastPress;
        int _draws;
        long _secondStart = System.Diagnostics.Stopwatch.GetTimestamp();
        readonly long _skipUntil = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 5;

        public void OnRender(PadSnapshot s, ulong now)
        {
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            if (s.Version != _lastVersion)
            {
                _lastVersion = s.Version;
                _draws++;
                if (s.LastPressNs != _lastPress && s.LastPressNs != 0)
                {
                    _lastPress = s.LastPressNs;
                    if (t > _skipUntil && now >= s.LastPressNs)
                        _latencies.Add((now - s.LastPressNs) / 1e6);
                    if (_latencies.Count >= 1000)
                    {
                        _latencies.Sort();
                        AppLog.Write($"計測 遅れ(ms) p50={_latencies[499]:0.0} p95={_latencies[949]:0.0} max={_latencies[^1]:0.0}");
                        _latencies.Clear();
                    }
                }
            }
            if (t - _secondStart >= System.Diagnostics.Stopwatch.Frequency)
            {
                AppLog.Write($"計測 描画 {_draws} 回/秒");
                _draws = 0;
                _secondStart = t;
            }
        }
    }
}
