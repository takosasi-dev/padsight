// アプリのログ(applog\padsight-yyyyMMdd.log)。8 日より古い物は起動時に消す(仕様書 §9.15)。
// 入力の中身(ボタン・方向・軸の値、押した順番)と Windows のユーザー名は書かない(INV-8・NFR-14)。
// ログの書き込みの失敗だけは、アプリを止める理由にならないので握りつぶす。
using System.IO;
using System.Text;

namespace PadSight.App;

public static class AppLog
{
    static readonly object Gate = new();
    static string? _dir;

    public static void Init(string dir, DateTime utcNow)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "padsight-*.log"))
        {
            if (utcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(8))
                TryDelete(f);
        }
    }

    public static void Write(string message)
    {
        if (_dir is null)
            return;
        var now = DateTime.UtcNow.ToLocalTime();
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {Scrub(message)}\n";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"padsight-{now:yyyyMMdd}.log"), line, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // ログの失敗では止めない
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }
    }

    /// <summary>例外の文に入るパス(C:\Users\名前\...)から、Windows のユーザー名を消す(NFR-14)。</summary>
    static string Scrub(string s)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
            s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        return user.Length > 0 ? s.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase) : s;
    }

    public static void Error(string where, Exception ex) => Write($"エラー({where}): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    static void TryDelete(string f)
    {
        try { File.Delete(f); }
        catch (IOException ex) { Write("古いログを消せなかった: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Write("古いログを消せなかった: " + ex.Message); }
    }
}
