// モニターの作業領域と DPI を読み、窓が画面の外かを判定する(P/Invoke はここ。仕様書 FR-13・§9.10.3)。
// 作業領域は物理ピクセルで持ち、DIP との換算は各モニターの DPI で行う。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OverlayCore;

/// <param name="Work">作業領域(物理ピクセル)</param>
public sealed record MonitorArea(string DeviceName, Int32Rect Work, double Dpi, bool Primary)
{
    public double Scale => Dpi / 96.0;
    public double WorkWidthDip => Work.Width / Scale;
    public double WorkHeightDip => Work.Height / Scale;
}

public static class MonitorInfo
{
    const uint MONITORINFOF_PRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size;
        public Rect32 Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static IReadOnlyList<MonitorArea> All()
    {
        var list = new List<MonitorArea>();
        EnumDisplayMonitors(0, 0, (h, _, _, _) => { if (Read(h) is { } m) list.Add(m); return true; }, 0);
        return list;
    }

    public static MonitorArea Primary() => All().FirstOrDefault(m => m.Primary) ?? All().First();

    /// <summary>自分の窓がいちばん重なっているモニター。</summary>
    public static MonitorArea ForWindow(Window w) =>
        Read(MonitorFromWindow(new WindowInteropHelper(w).Handle, MONITOR_DEFAULTTONEAREST)) ?? Primary();

    /// <summary>矩形(物理ピクセル)と、どれかのモニターの作業領域の重なりが、面積の 50% に満たなければ真。</summary>
    public static bool IsMostlyOffscreen(Int32Rect r, IReadOnlyList<MonitorArea> monitors)
    {
        double area = (double)r.Width * r.Height;
        if (area <= 0)
            return true;
        double best = monitors.Max(m =>
        {
            double w = Math.Min(r.X + r.Width, m.Work.X + m.Work.Width) - Math.Max(r.X, m.Work.X);
            double h = Math.Min(r.Y + r.Height, m.Work.Y + m.Work.Height) - Math.Max(r.Y, m.Work.Y);
            return w > 0 && h > 0 ? w * h : 0;
        });
        return best < area * 0.5;
    }

    static MonitorArea? Read(nint h)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(h, ref info))
            return null;
        uint dpi = GetDpiForMonitor(h, 0, out var dx, out _) == 0 ? dx : 96;
        var w = info.Work;
        return new MonitorArea(info.Device, new Int32Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), dpi,
            (info.Flags & MONITORINFOF_PRIMARY) != 0);
    }
}
