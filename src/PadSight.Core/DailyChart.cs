// 統計の画面の棒グラフ(仕様書 FR-41)の値。直近 N 日(今日を含む)の日ごとの値を古い順に返す。
// 日の境目はローカルの 0 時で、0 時をまたぐセッションは始まった日に数える(統計の期間と同じ)。
namespace PadSight.Core;

public static class DailyChart
{
    /// <summary>"inputs"(合計)・"minutes"(合計の分)・"peak1s"(その日の最大)。</summary>
    public static readonly string[] Metrics = ["inputs", "minutes", "peak1s"];

    public static IReadOnlyList<(DateTime Day, double Value)> Build(
        IEnumerable<SessionRecord> records, string metric, DateTime todayLocal, TimeZoneInfo tz, int days = 30)
    {
        var first = todayLocal.Date.AddDays(1 - days);
        var values = new double[days];
        foreach (var r in records)
        {
            var day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartUtc, DateTimeKind.Utc), tz).Date;
            int i = (day - first).Days;
            if (i < 0 || i >= days)
                continue;
            values[i] = metric switch
            {
                "minutes" => values[i] + r.DurationSeconds / 60.0,
                "peak1s" => Math.Max(values[i], r.PeakPerSecond),
                _ => values[i] + r.Inputs,
            };
        }
        return [.. values.Select((v, i) => (first.AddDays(i), v))];
    }
}
