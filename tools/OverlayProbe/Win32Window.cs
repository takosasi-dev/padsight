// OverlayProbe の Win32 呼び出しをここ 1 か所にまとめる(仕様書 §9.10.1・INV-11)。
// どの関数も WPF の Window を受け、そこから取った自分の HWND にだけ呼ぶ(他の窓を受け取る口は作らない。INV-6)。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OverlayProbe;

static class Win32Window
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x00000020;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_NOACTIVATE = 0x08000000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    /// <summary>SourceInitialized で呼ぶ。WS_EX_LAYERED は WPF に任せて触らない。</summary>
    public static void AddOverlayStyles(Window window) =>
        SetExStyle(window, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT, true);

    public static void SetClickThrough(Window window, bool on) => SetExStyle(window, WS_EX_TRANSPARENT, on);

    public static void BringToTop(Window window) =>
        SetWindowPos(Handle(window), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    static void SetExStyle(Window window, long bits, bool on)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return; // まだ窓ができていない
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(on ? style | bits : style & ~bits));
    }

    // shortcut: ...PtrW は 64 ビットの user32 にしか無い。x86 で動かすことになったら GetWindowLongW に振り分ける。
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
