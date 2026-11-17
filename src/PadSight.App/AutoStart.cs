// ログオン時の自動起動(仕様書 FR-29)。HKCU の Run に値 PadSight を足す・消す。値は exe のフルパスと --tray。
using Microsoft.Win32;

namespace PadSight.App;

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "PadSight";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(Name, Command);
        else k.DeleteValue(Name, throwOnMissingValue: false);
        AppLog.Write("自動起動: " + (on ? "オン" : "オフ"));
    }
}
