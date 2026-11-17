// settings.json の読み書き(仕様書 FR-33・FR-34・E-13)。書くときは一時ファイルに書いてから置き換える。
// 起動のたびに backups\ へ写しを取り、新しい 5 つだけを残す。壊れていたら名前を変えて既定値で起動する。
using System.IO;
using System.Text;
using System.Text.Json;
using PadSight.Core;

namespace PadSight.App;

public sealed class SettingsStore(string dataDir)
{
    public const int BackupGenerations = 5;
    static readonly UTF8Encoding Utf8 = new(false);

    public string Path => System.IO.Path.Combine(dataDir, "settings.json");
    public string BackupDir => System.IO.Path.Combine(dataDir, "backups");

    /// <summary>新しい版の設定なので保存しない(§9.14)。</summary>
    public bool ReadOnly { get; private set; }

    public enum LoadResult { Loaded, Missing, Broken }

    public (Settings Settings, LoadResult Result) Load(DateTime utcNow)
    {
        if (!File.Exists(Path))
            return (new Settings(), LoadResult.Missing);
        try
        {
            var s = Settings.Parse(File.ReadAllText(Path, Encoding.UTF8), out var reset, out bool newer);
            foreach (var key in reset)
                AppLog.Write("設定の値を既定に戻した: " + key);
            ReadOnly = newer;
            return (s, LoadResult.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            // ArgumentException は同じキーが 2 つある JSON(.NET 10 の JsonNode が読むときに出す)
            AppLog.Write("settings.json を読めなかった: " + ex.Message);
            File.Move(Path, $"{Path}.broken-{utcNow.ToLocalTime():yyyyMMdd-HHmmss}");
            return (new Settings(), LoadResult.Broken);
        }
    }

    public void Save(Settings s)
    {
        if (ReadOnly)
            return;
        try
        {
            Directory.CreateDirectory(dataDir);
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, s.ToJson(), Utf8);
            File.Move(tmp, Path, overwrite: true);
        }
        catch (IOException ex) { AppLog.Error("設定の保存", ex); }
        catch (UnauthorizedAccessException ex) { AppLog.Error("設定の保存", ex); }
    }

    /// <summary>起動時に写しを取り、6 世代目から古い順に消す。</summary>
    public void Backup(DateTime utcNow)
    {
        if (!File.Exists(Path))
            return;
        try
        {
            Directory.CreateDirectory(BackupDir);
            File.Copy(Path, System.IO.Path.Combine(BackupDir, $"settings-{utcNow.ToLocalTime():yyyyMMdd-HHmmss-fff}.json"), overwrite: true);
            foreach (var old in ListBackups().Skip(BackupGenerations))
                File.Delete(old);
        }
        catch (IOException ex) { AppLog.Error("設定の写し", ex); }
        catch (UnauthorizedAccessException ex) { AppLog.Error("設定の写し", ex); }
    }

    /// <summary>新しい順。</summary>
    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(BackupDir)
            ? [.. Directory.GetFiles(BackupDir, "settings-*.json").OrderByDescending(f => f, StringComparer.Ordinal)]
            : [];

    /// <summary>写しを読む。読めなければ null。</summary>
    public static Settings? ReadBackup(string file)
    {
        try { return Settings.Parse(File.ReadAllText(file, Encoding.UTF8), out _, out _); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException) { AppLog.Write("写しを読めなかった: " + ex.Message); return null; }
        catch (IOException ex) { AppLog.Write("写しを読めなかった: " + ex.Message); return null; }
    }
}
