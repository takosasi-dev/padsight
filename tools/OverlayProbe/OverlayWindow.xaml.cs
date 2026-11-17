// OverlayProbe の重ねる窓。窓ができたら拡張スタイルを足し、2 秒ごとに最前面を付け直す(仕様書 §9.10.1)。
// 時計は 1 秒ごとに書き換える。
using System.Windows;
using System.Windows.Threading;

namespace OverlayProbe;

public partial class OverlayWindow : Window
{
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer topmost = new() { Interval = TimeSpan.FromSeconds(2) };

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32Window.AddOverlayStyles(this);
        clock.Tick += (_, _) => ShowTime();
        topmost.Tick += (_, _) => Win32Window.BringToTop(this);
        Loaded += (_, _) => { ShowTime(); clock.Start(); topmost.Start(); };
        Closed += (_, _) => { clock.Stop(); topmost.Stop(); };
    }

    void ShowTime() => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
}
