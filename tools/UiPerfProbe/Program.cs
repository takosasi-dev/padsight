// UiPerfProbe: パネル 4 枚とメインの画面のタブ(診断は 6 ページ)を窓なしで 1 フレームずつ描かせ、
// 1 フレームの「Render + レイアウト」の時間と割り当てを測る。入力は InputEngine に合成の入力を流して作る(PerfProbe と同じ形)。
// 場面: idle = 入力なし(SDL のスレッドと同じく 250 ミリ秒ごとに Tick とスナップショット)、spin = 左スティックを回し続け・ボタン 10 回/秒。
// 同じ画面で 120 フレーム慣らしてから 600 フレーム(60 Hz で 10 秒)測る。
// 窓は出さない(Show しない)。記録の置き場所は一時フォルダに差し替える。
// 実行: dotnet run -c Release --project tools\UiPerfProbe            … 表を出す
//       dotnet run -c Release --project tools\UiPerfProbe -- shots <dir> … 決まった入力の流れでパネル 4 枚とトリガーのページを PNG にする
//       (直す前後で PNG のハッシュが同じなら、見た目は変わっていない)
// WPF が実際に画面へ描く時間(合成のスレッド)は測れない。測るのは UI のスレッドの仕事だけ。
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PadSight.App;
using PadSight.App.Main;
using PadSight.App.Panels;
using PadSight.Core;

const int Warm = 120, Frames = 600;
const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
string dataDir = Path.Combine(Path.GetTempPath(), "PadSightUiPerf");
int exit = 0;

var th = new Thread(() =>
{
    try
    {
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
        Directory.CreateDirectory(dataDir);
        var app = new App();
        app.InitializeComponent();
        Set(app, "Sessions", new SessionStore(dataDir));
        Set(app, "Diagnostics", new DiagStore(dataDir));
        Set(app, "Panels", new PanelManager(() => app.Settings, () => { }));
        Theme.Apply("dark");

        var targets = new List<Target>
        {
            new("panel:pad", () => new PadView(), PanelRender),
            new("panel:stick", () => new StickView(), PanelRender),
            new("panel:history", () => new HistoryView(), PanelRender),
            new("panel:stats", () => new StatsView(), PanelRender),
            new("tab:home", () => new HomeTab(), (e, s, n) => { ((HomeTab)e).Render(s, n, true); return true; }),
            new("tab:panels", () => new PanelsTab(), (e, s, n) => { ((PanelsTab)e).Render(s, n); return true; }),
            new("tab:stats", () => new StatsTab(), (e, s, n) => { ((StatsTab)e).Render(s); return true; }),
            new("tab:measure", () => new MeasureTab(), (e, s, n) => { ((MeasureTab)e).Render(s, n); return true; }),
        };
        // 縁取り文字 1 個を毎フレーム違う数に書き換える(文字 1 個の書き換えの値段)
        targets.Insert(0, new("text:1", () => Draw.Text("", 13, mono: true), (e, s, n) => { ((OutlineText)e).Text = "X " + (n / 1000 % 100000).ToString(); return true; }));
        string[] pages = ["buttons", "stick", "trigger", "vibration", "idle", "history"];
        for (int i = 0; i < pages.Length; i++)
        {
            int page = i;
            targets.Add(new($"diag:{pages[i]}", () => { var d = new DiagnosticsTab(); d.SelectPage(page); return d; }, (e, s, n) => { ((DiagnosticsTab)e).Render(s, n); return true; }));
        }

        if (args.Length == 3 && args[0] == "diff")
        {
            exit = Diff(args[1], args[2]);
            return;
        }
        if (args.Length == 2 && args[0] == "shots")
        {
            Shots(targets.Where(t => t.Name.StartsWith("panel:") || t.Name == "diag:trigger"), args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "alloc")
        {
            // 割り当ての多い型: GC の割り当ての標本(約 100 KB ごとに 1 件)を型の名前で数える
            var t = targets.Single(x => x.Name == args[1]);
            Measure(t, spin: true);
            using var listener = new AllocListener();
            Measure(t, spin: true);
            foreach (var (name, n) in listener.Counts.OrderByDescending(kv => kv.Value).Take(25))
                Console.WriteLine($"{n,6}  {name}");
            return;
        }
        // B/frame は Render とレイアウトの合計。そのうち Render の中の分が render B
        Console.WriteLine($"{"target",-16} {"scene",-5} {"renders",7} {"B/frame",9} {"render B",9} {"us/frame(median)",17} {"us/frame(p99)",14}");
        foreach (var t in targets.Where(t => args.Length == 0 || t.Name.StartsWith(args[0])))
            foreach (bool spin in new[] { false, true })
            {
                var r = Measure(t, spin);
                Console.WriteLine($"{t.Name,-16} {(spin ? "spin" : "idle"),-5} {r.Renders,7} {r.Bytes / Frames,9} {r.RenderBytes / Frames,9} {r.Median,17:F1} {r.P99,14:F1}");
            }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        exit = 1;
    }
});
th.SetApartmentState(ApartmentState.STA);
th.Start();
th.Join();
return exit;

static void Set(App app, string prop, object value) => typeof(App).GetField($"<{prop}>k__BackingField", F)!.SetValue(app, value);

static bool PanelRender(FrameworkElement e, PadSnapshot s, ulong n)
{
    var v = (PanelView)e;
    // PanelManager.Render と同じく、版が変わったか、時間で変わる表示があるときだけ描く
    if (s.Version == (long)(v.Tag ?? -1L) && !v.Animating(s))
        return false;
    v.Tag = s.Version;
    v.Render(s, App.Current.Settings, n);
    return true;
}

static Border Host(FrameworkElement view)
{
    var host = new Border { Child = view, Width = 940, Height = 760 };
    host.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
    TextElement.SetFontFamily(host, new FontFamily("Yu Gothic UI"));
    TextElement.SetFontSize(host, 14);
    host.Measure(new Size(940, 760));
    host.Arrange(new Rect(0, 0, 940, 760));
    host.UpdateLayout();
    return host;
}

static (int Renders, long Bytes, long RenderBytes, double Median, double P99) Measure(Target t, bool spin)
{
    var view = t.Make();
    var host = Host(view);
    var feed = new Feed();
    for (int f = 0; f < Warm; f++)
    {
        var (s, now) = feed.Next(spin);
        App.Current.Bus.Publish(s);
        t.Render(view, s, now);
        host.UpdateLayout();
    }
    var times = new List<double>(Frames);
    long bytes = 0, renderBytes = 0;
    int renders = 0;
    var sw = new Stopwatch();
    for (int f = 0; f < Frames; f++)
    {
        var (s, now) = feed.Next(spin);
        App.Current.Bus.Publish(s);
        long b = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        if (t.Render(view, s, now)) renders++;
        renderBytes += GC.GetAllocatedBytesForCurrentThread() - b;
        host.UpdateLayout(); // 文字の組み立てと OnRender もここで走る
        sw.Stop();
        bytes += GC.GetAllocatedBytesForCurrentThread() - b;
        times.Add(sw.Elapsed.TotalMicroseconds);
    }
    times.Sort();
    return (renders, bytes, renderBytes, times[times.Count / 2], times[(int)(times.Count * 0.99)]);
}

// 決まった流れ: 0〜299 は spin(ボタンの押下を含む)、300〜419 は入力なし。150・203・299・330・419 のフレームを撮る
static void Shots(IEnumerable<Target> targets, string dir)
{
    Directory.CreateDirectory(dir);
    int[] at = [150, 203, 299, 330, 419];
    foreach (var t in targets)
    {
        var view = t.Make();
        var host = Host(view);
        var feed = new Feed();
        for (int f = 0; f < 420; f++)
        {
            var (s, now) = feed.Next(spin: f < 300);
            App.Current.Bus.Publish(s);
            t.Render(view, s, now);
            host.UpdateLayout();
            if (!at.Contains(f))
                continue;
            var bmp = new RenderTargetBitmap(940, 760, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(dir, $"{t.Name.Replace(':', '_')}_{f:000}.png"));
            enc.Save(file);
        }
    }
    Console.WriteLine("shots: " + dir);
}

// 2 つのフォルダの同じ名前の PNG を画素で比べ、違う画素の数と、色の差の最大(0〜255)を出す。全部同じなら 0 を返す
static int Diff(string a, string b)
{
    int bad = 0;
    foreach (var fa in Directory.GetFiles(a, "*.png").Order())
    {
        var fb = Path.Combine(b, Path.GetFileName(fa));
        if (!File.Exists(fb)) { Console.WriteLine($"{Path.GetFileName(fa)}: 相手が無い"); bad++; continue; }
        byte[] pa = Pixels(fa), pb = Pixels(fb);
        int n = 0, max = 0;
        for (int i = 0; i < pa.Length; i += 4)
        {
            int d = 0;
            for (int c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(pa[i + c] - pb[i + c]));
            if (d > 0) { n++; max = Math.Max(max, d); }
        }
        if (n > 0) { Console.WriteLine($"{Path.GetFileName(fa)}: {n} 画素 最大 {max}"); bad++; }
    }
    Console.WriteLine(bad == 0 ? "diff: 全部同じ" : $"diff: {bad} 枚が違う");
    return bad == 0 ? 0 : 2;

    static byte[] Pixels(string path)
    {
        var src = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var px = new byte[src.PixelWidth * src.PixelHeight * 4];
        new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0).CopyPixels(px, src.PixelWidth * 4, 0);
        return px;
    }
}

sealed class AllocListener : System.Diagnostics.Tracing.EventListener
{
    public Dictionary<string, int> Counts { get; } = [];

    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1); // GC
    }

    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName?.StartsWith("GCAllocationTick") != true || e.Payload is null)
            return;
        int i = e.PayloadNames!.IndexOf("TypeName");
        string name = i >= 0 ? e.Payload[i]?.ToString() ?? "?" : "?";
        lock (Counts)
            Counts[name] = Counts.GetValueOrDefault(name) + 1;
    }
}

sealed record Target(string Name, Func<FrameworkElement> Make, Func<FrameworkElement, PadSnapshot, ulong, bool> Render);

/// <summary>InputEngine に 1 ミリ秒刻みの合成の入力を流し、60 Hz の 1 フレームごとに今のスナップショットと時刻を返す。</summary>
sealed class Feed
{
    const ulong Frame = 16_666_667;
    static readonly PadButton[] Taps = [PadButton.South, PadButton.East, PadButton.West, PadButton.North];
    readonly SimClock _clock = new();
    readonly InputEngine _engine;
    PadSnapshot _snap;
    ulong _ms;
    int _frame;

    public Feed()
    {
        _engine = new InputEngine(_clock, EngineConfig.From(App.Current.Settings));
        _engine.Apply(new PadAdded(0, 1, new PadInfo(1, "030000005e040000ea02000000007801", "Probe Pad", PadKind.Xbox, new HashSet<PadButton>(Enum.GetValues<PadButton>()), true, "XBOXONE")));
        _snap = _engine.Snapshot(0, 0);
    }

    public (PadSnapshot Snap, ulong Now) Next(bool spin)
    {
        _frame++;
        ulong frameEnd = (ulong)_frame * Frame / 1_000_000;
        for (; _ms < frameEnd; _ms++)
        {
            ulong ns = _ms * 1_000_000;
            _clock.MonotonicNs = ns;
            bool dirty = false;
            if (spin && _ms % 4 == 0)
            {
                double a = _ms / 1000.0 * 2 * Math.PI;
                bool x = _ms / 4 % 2 == 0;
                _engine.Apply(new AxisChanged(ns, 1, x ? PadAxis.LeftX : PadAxis.LeftY, (short)((x ? Math.Cos(a) : -Math.Sin(a)) * 30000)));
                dirty = true;
            }
            if (spin && _ms % 100 == 0) { _engine.Apply(new ButtonChanged(ns, 1, Taps[_ms / 100 % 4], true)); dirty = true; }
            if (spin && _ms % 100 == 30) { _engine.Apply(new ButtonChanged(ns, 1, Taps[_ms / 100 % 4], false)); dirty = true; }
            if (_ms % 250 == 0) { _engine.Tick(ns); dirty = true; } // SdlInputThread は Tick の後に必ず出し直す
            if (dirty && (!spin || _ms % 4 == 2 || _ms % 250 == 0)) // 回している間は 4 ミリ秒ごとにまとめて出す
                _snap = _engine.Snapshot(ns, 0);
        }
        return (_snap, (ulong)_frame * Frame);
    }
}

sealed class SimClock : IClock
{
    public DateTime UtcNow { get; } = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    public ulong MonotonicNs { get; set; }
}
