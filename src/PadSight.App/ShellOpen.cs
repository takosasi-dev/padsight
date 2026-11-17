// データのフォルダをエクスプローラーで、公開のページを既定のブラウザで開く(ShellExecuteW。P/Invoke はここ。仕様書 INV-11)。
// 開くのはフォルダのパスと PadSight の公開のページだけで、他のアプリの窓やプロセスには触らない。
using System.IO;
using System.Runtime.InteropServices;

namespace PadSight.App;

public static partial class ShellOpen
{
    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ShellExecute(nint hwnd, string? operation, string file, string? parameters, string? directory, int showCmd);

    public static void Folder(string path)
    {
        Directory.CreateDirectory(path);
        nint r = ShellExecute(0, "open", path, null, null, 1);
        if (r <= 32)
            AppLog.Write($"フォルダを開けなかった(ShellExecute {r})");
    }

    /// <summary>PadSight の公開のページだけを既定のブラウザで開く(FR-44)。ほかの URL は開かない。</summary>
    public static void ReleasePage(string url)
    {
        if (!url.StartsWith(PadSight.Core.ReleaseCheck.PageUrlPrefix, StringComparison.Ordinal))
            return;
        nint r = ShellExecute(0, "open", url, null, null, 1);
        if (r <= 32)
            AppLog.Write($"公開のページを開けなかった(ShellExecute {r})");
    }
}
