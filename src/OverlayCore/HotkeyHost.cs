// RegisterHotKey で登録した組み合わせだけを、隠しウィンドウで受ける(P/Invoke はここ。仕様書 D-13・§9.11)。
// キーボードのフックは使わない(INV-5)。登録に失敗しても別の組み合わせへ振り替えず、失敗を返す。
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace OverlayCore;

public sealed class HotkeyHost : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(nint hWnd, int id);

    readonly HwndSource _source;
    readonly HashSet<int> _registered = [];

    public event Action<int>? Pressed;

    public HotkeyHost()
    {
        // メインの画面と別の、見えない窓で受ける
        _source = new HwndSource(new HwndSourceParameters("HotkeyHost") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>"Ctrl+Alt+G" の形の組み合わせを登録する。失敗したら偽。</summary>
    public bool Register(int id, string combo)
    {
        Unregister(id);
        if (!TryParse(combo, out uint mods, out uint vk))
            return false;
        if (!RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, vk))
            return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(_source.Handle, id);
    }

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        var parts = combo.Split('+');
        if (parts.Length < 2)
            return false; // 修飾キーの無い組み合わせは不可
        foreach (var m in parts[..^1])
        {
            uint bit = m switch { "Ctrl" => MOD_CONTROL, "Alt" => MOD_ALT, "Shift" => MOD_SHIFT, "Win" => MOD_WIN, _ => 0 };
            if (bit == 0)
                return false;
            mods |= bit;
        }
        string k = parts[^1];
        if (k.Length == 1 && char.IsAsciiDigit(k[0]))
            k = "D" + k;
        if (!Enum.TryParse<Key>(k, out var key))
            return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    /// <summary>WPF のキーの組から "Ctrl+Alt+G" の形を作る(設定の画面で押した組み合わせを受けるため)。修飾キーが無ければ null。</summary>
    public static string? Format(ModifierKeys mods, Key key)
    {
        if (mods == ModifierKeys.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return null;
        string name = key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : key is >= Key.A and <= Key.Z or >= Key.F1 and <= Key.F24 ? key.ToString() : "";
        if (name.Length == 0)
            return null;
        var list = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) list.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) list.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) list.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) list.Add("Win");
        list.Add(name);
        return string.Join("+", list);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            Pressed?.Invoke((int)wParam);
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList())
            Unregister(id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
