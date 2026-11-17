// 自分の窓の拡張スタイルと最前面の付け直し、位置の移動(P/Invoke はここ。仕様書 §9.10.1・INV-11)。
// どのメソッドも WPF の Window を受け、そこから取った自分の HWND にだけ呼ぶ。他の HWND を受け取る口は作らない(INV-6)。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OverlayCore;

public static partial class Win32Window
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    static readonly nint HWND_TOPMOST = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out Rect32 rect);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect32 { public int Left, Top, Right, Bottom; }

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>自分の窓の題名の帯を暗くする・戻す。窓ができる前(HWND が 0)と、対応していない古い Windows では何もしない。</summary>
    public static void SetDarkTitleBar(Window w, bool dark)
    {
        nint h = Handle(w);
        if (h == 0)
            return;
        int v = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    static nint Handle(Window w) => new WindowInteropHelper(w).Handle;

    /// <summary>SourceInitialized で呼ぶ。タスクバーに出さない・前面にならない・クリックを通す。WS_EX_LAYERED は WPF に任せる。</summary>
    public static void AddOverlayStyles(Window w)
    {
        nint h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE);
        SetWindowLongPtr(h, GWL_EXSTYLE, (nint)(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
    }

    /// <summary>配置を直す間だけ WS_EX_TRANSPARENT を外す(D-21)。WS_EX_NOACTIVATE は外さない。</summary>
    public static void SetClickThrough(Window w, bool clickThrough)
    {
        nint h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE);
        ex = clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(h, GWL_EXSTYLE, (nint)ex);
    }

    public static void ReassertTopmost(Window w) =>
        SetWindowPos(Handle(w), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>物理ピクセルで左上を動かす。</summary>
    public static void MoveTo(Window w, int x, int y) =>
        SetWindowPos(Handle(w), 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>物理ピクセルの矩形。</summary>
    public static Int32Rect GetRect(Window w)
    {
        GetWindowRect(Handle(w), out var r);
        return new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
