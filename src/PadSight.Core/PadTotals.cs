// パッドの型(GUID)ごとの通算(仕様書 FR-38・§9.6.5)。確定して保存したセッションの回数を足していく。
// 集計だけで、押した順番と時刻は持たない(INV-8)。ファイルの読み書きは App が行い、ここは文字列だけを扱う。
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PadSight.Core;

public sealed class PadTotal
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public long Inputs { get; set; }
    public Dictionary<string, long> Buttons { get; } = [];
    public DateTime FirstUsedUtc { get; set; }
    public DateTime LastUsedUtc { get; set; }
}

public sealed class PadTotals
{
    public Dictionary<string, PadTotal> Pads { get; } = [];

    /// <summary>確定したセッションの GUID ごとの回数を足す。</summary>
    public void Add(SessionRecord r)
    {
        foreach (var (guid, t) in r.PerPad)
        {
            if (!Pads.TryGetValue(guid, out var p))
                Pads[guid] = p = new PadTotal { FirstUsedUtc = r.StartUtc };
            p.Name = t.Name;
            p.Kind = t.Kind;
            p.Inputs += t.Inputs;
            foreach (var (k, v) in t.Buttons)
                p.Buttons[k] = p.Buttons.GetValueOrDefault(k) + v;
            if (r.StartUtc < p.FirstUsedUtc) p.FirstUsedUtc = r.StartUtc;
            if (r.EndUtc > p.LastUsedUtc) p.LastUsedUtc = r.EndUtc;
        }
    }

    public string ToJson(TimeZoneInfo tz)
    {
        var pads = new JsonObject();
        foreach (var (guid, p) in Pads)
        {
            var buttons = new JsonObject();
            foreach (var (k, v) in p.Buttons.Where(kv => kv.Value > 0))
                buttons[k] = v;
            pads[guid] = new JsonObject
            {
                ["name"] = p.Name, ["kind"] = p.Kind, ["inputs"] = p.Inputs, ["buttons"] = buttons,
                ["firstUsed"] = SessionJson.Format(p.FirstUsedUtc, tz), ["lastUsed"] = SessionJson.Format(p.LastUsedUtc, tz),
            };
        }
        return new JsonObject { ["schemaVersion"] = 1, ["pads"] = pads }
            .ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>読む。形が違えば JsonException など(呼ぶ側で名前を変えて空から始める)。</summary>
    public static PadTotals Parse(string json)
    {
        var totals = new PadTotals();
        if (JsonNode.Parse(json)?["pads"] is not JsonObject pads)
            throw new JsonException("pads がありません");
        foreach (var (guid, node) in pads)
        {
            var o = node as JsonObject ?? throw new JsonException("パッドの形が違います");
            var p = new PadTotal
            {
                Name = o["name"]?.GetValue<string>() ?? "",
                Kind = o["kind"]?.GetValue<string>() ?? "",
                Inputs = o["inputs"]?.GetValue<long>() ?? 0,
                FirstUsedUtc = ParseUtc(o["firstUsed"]?.GetValue<string>()),
                LastUsedUtc = ParseUtc(o["lastUsed"]?.GetValue<string>()),
            };
            if (o["buttons"] is JsonObject b)
                foreach (var (k, v) in b)
                    p.Buttons[k] = v!.GetValue<long>();
            totals.Pads[guid] = p;
        }
        return totals;
    }

    static DateTime ParseUtc(string? s) => s is null ? default
        : DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
