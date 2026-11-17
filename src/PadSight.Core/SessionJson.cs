// sessions.jsonl の 1 行と current_session.json の形(仕様書 §9.6.2・§9.6.4)を、文字列と SessionRecord の間で直す。
// ファイルの読み書きは App の SessionStore が行い、ここは文字列だけを扱う(INV-13)。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PadSight.Core;

public static class SessionJson
{
    public const int SchemaVersion = 1;

    public static string ToLine(SessionRecord r, TimeZoneInfo tz)
    {
        var buttons = new JsonObject();
        foreach (var kv in r.Buttons.Where(kv => kv.Value > 0))
            buttons[kv.Key] = kv.Value;
        var o = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["id"] = r.Id.ToString("D"),
            ["start"] = Format(r.StartUtc, tz),
            ["end"] = Format(r.EndUtc, tz),
            ["durationSeconds"] = r.DurationSeconds,
            ["pad"] = new JsonObject { ["kind"] = r.PadKind, ["name"] = r.PadName },
            ["inputs"] = r.Inputs,
            ["peakPerMinute"] = r.PeakPerMinute,
            ["peakPerSecond"] = r.PeakPerSecond,
            ["buttons"] = buttons,
        };
        if (r.LastOperationUtc is { } last)
            o["lastOperation"] = Format(last, tz);
        return o.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>1 行を読む。形が違えば null(E-14)。</summary>
    public static SessionRecord? Parse(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o)
                return null;
            var buttons = new Dictionary<string, long>();
            if (o["buttons"] is JsonObject b)
                foreach (var kv in b)
                    buttons[kv.Key] = kv.Value!.GetValue<long>();
            return new SessionRecord(
                Guid.Parse(o["id"]!.GetValue<string>()),
                ParseUtc(o["start"]!.GetValue<string>()),
                ParseUtc(o["end"]!.GetValue<string>()),
                o["durationSeconds"]!.GetValue<long>(),
                o["pad"]?["kind"]?.GetValue<string>() ?? "",
                o["pad"]?["name"]?.GetValue<string>() ?? "",
                o["inputs"]!.GetValue<long>(),
                o["peakPerMinute"]?.GetValue<int>() ?? 0,
                buttons,
                o["lastOperation"] is JsonNode lo ? ParseUtc(lo.GetValue<string>()) : null)
            {
                PeakPerSecond = o["peakPerSecond"]?.GetValue<int>() ?? 0, // FR-37 より前の行は 0
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"2026-11-17T20:15:03+09:00" の形。</summary>
    public static string Format(DateTime utc, TimeZoneInfo tz)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        var off = tz.GetUtcOffset(utc);
        return local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            + (off < TimeSpan.Zero ? "-" : "+") + off.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
