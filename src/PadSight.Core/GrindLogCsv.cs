// セッションの集計を、GrindLog の取り込みの 17 列の形の CSV にする(仕様書 §9.6.3・D-27)。
// 60 秒未満のセッションは除く(E-30)。BOM と CRLF は App が書くときに付ける(ここは本文の文字列だけ)。
using System.Globalization;
using System.Text;

namespace PadSight.Core;

public static class GrindLogCsv
{
    public const string Header = "ID,日付,開始時刻,ゲーム,種別,時間(分),キル,デス,アシスト,結果,ランク表記,レート,調子,メモ,タグ,出所,取込元ID";

    public static bool Exportable(SessionRecord r) => r.DurationSeconds >= 60;

    /// <summary>CSV の本文(ヘッダーを含む。改行は CRLF)。ゲームと種別は前後の空白を除いて書く。</summary>
    public static string Build(IEnumerable<SessionRecord> sessions, string game, string type, TimeZoneInfo tz)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        foreach (var r in sessions.Where(Exportable).OrderBy(r => r.StartUtc))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartUtc, DateTimeKind.Utc), tz);
            string[] cols =
            [
                r.Id.ToString("D"),
                local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                local.ToString("HH:mm", CultureInfo.InvariantCulture),
                game.Trim(), type.Trim(),
                Minutes(r.DurationSeconds).ToString(CultureInfo.InvariantCulture),
                "", "", "", "なし", "", "", "",
                Memo(r), "PadSight", "手入力", "",
            ];
            sb.Append(string.Join(",", cols.Select(Quote))).Append("\r\n");
        }
        return sb.ToString();
    }

    public static long Minutes(long durationSeconds) => (long)Math.Round(durationSeconds / 60.0, MidpointRounding.AwayFromZero);

    public static string Memo(SessionRecord r)
    {
        double perMin = r.DurationSeconds > 0 ? Math.Round(r.Inputs / (r.DurationSeconds / 60.0), 1, MidpointRounding.AwayFromZero) : 0;
        var top = r.Buttons.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(3)
            .Select(kv => $"{Labels.ForRecord(kv.Key, r.PadKind)} {kv.Value}").ToList();
        string memo = string.Format(CultureInfo.InvariantCulture,
            "PadSight: 入力 {0} 回、1 分あたり {1:0.0}、60 秒の最大 {2}、パッド {3}、多いボタン {4}",
            r.Inputs, perMin, r.PeakPerMinute, r.PadName, top.Count > 0 ? string.Join(" / ", top) : "なし");
        return memo.Length > 2000 ? memo[..2000] : memo;
    }

    public static string Quote(string v)
    {
        bool need = v.IndexOfAny([',', '"', '\r', '\n']) >= 0 || (v.Length > 0 && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1])));
        return need ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
