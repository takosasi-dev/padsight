// 通信で取ってきた物の確かめ(仕様書 FR-43・FR-44・§9.17)。通信そのものは App の WebFetch が行い、ここは文字列だけを扱う。
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PadSight.Core;

public static partial class MappingDb
{
    public const int MaxBytes = 1024 * 1024;
    public const int MinWindowsLines = 100;

    [GeneratedRegex("^([0-9a-fA-F]{32}|xinput),[^,]+,.*platform:[^,]+,")]
    private static partial Regex Line();

    /// <summary>gamecontrollerdb.txt の形なら Windows の行の数、違えば null(置き換えない)。</summary>
    public static int? Check(string text)
    {
        if (text.Length == 0 || text.Contains('\0'))
            return null;
        int windows = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#'))
                continue;
            if (!Line().IsMatch(line))
                return null;
            if (line.Contains("platform:Windows,", StringComparison.Ordinal))
                windows++;
        }
        return windows >= MinWindowsLines ? windows : null;
    }
}

public static class ReleaseCheck
{
    public const string PageUrlPrefix = "https://github.com/takosasi-dev/padsight/releases/";

    /// <summary>releases/latest の答えを読む。今の版より新しければ (tag, ページ)。同じか古い・読めない・宛先が違えば null。</summary>
    public static (string Tag, string Url)? Newer(string json, string currentVersion)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o
                || o["tag_name"]?.GetValue<string>() is not { } tag
                || o["html_url"]?.GetValue<string>() is not { } url
                || !url.StartsWith(PageUrlPrefix, StringComparison.Ordinal)
                || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
                || !Version.TryParse(currentVersion, out var current))
                return null;
            return Normalize(latest) > Normalize(current) ? (tag, url) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // "0.2" と "0.2.0" を同じに扱う(未指定の桁は -1 になるため)
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
