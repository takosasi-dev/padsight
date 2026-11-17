// 診断の結果の記録の置き場所(仕様書 FR-60・§9.20)。%LOCALAPPDATA%\PadSight\diagnostics\ に 1 件 1 つの JSON で置き、一覧と消すを受け持つ。
// 書くのは結果の数と判定だけ(DiagRecord)。時刻つきの入力は書かない(INV-8)。ファイルが小さいので、UI のスレッドで読み書きする。
using System.IO;
using System.Text;
using PadSight.Core;

namespace PadSight.App;

public sealed class DiagStore(string dataDir)
{
    static readonly UTF8Encoding Utf8 = new(false);

    public string Dir => Path.Combine(dataDir, "diagnostics");

    /// <summary>1 件残す。残せたらファイルの場所、残せなければ null(ログに書く)。</summary>
    public string? Save(DiagRecord r)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string stem = $"{r.AtUtc.ToLocalTime():yyyyMMdd-HHmmss}-{Safe(r.Kind)}";
            string path = Path.Combine(Dir, stem + ".json");
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(Dir, $"{stem}-{i}.json");
            File.WriteAllText(path + ".tmp", r.ToJson(), Utf8);
            File.Move(path + ".tmp", path);
            AppLog.Write("診断の結果を残した: " + r.Kind);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("診断の結果の保存", ex);
            return null;
        }
    }

    /// <summary>新しい順。読めないファイルは飛ばす。</summary>
    public IReadOnlyList<(string Path, DiagRecord Record)> List()
    {
        var list = new List<(string, DiagRecord)>();
        if (!Directory.Exists(Dir))
            return list;
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            try
            {
                if (DiagRecord.Parse(File.ReadAllText(f, Encoding.UTF8)) is { } r)
                    list.Add((f, r));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("診断の結果の読み込み", ex);
            }
        }
        return [.. list.OrderByDescending(x => x.Item2.AtUtc)];
    }

    public bool Delete(string path)
    {
        try
        {
            // 置き場所の外は消さない
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(Dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("診断の結果を消す", ex);
            return false;
        }
    }

    static string Safe(string kind) => new([.. kind.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_')]);
}
