// 診断の結果の記録(仕様書 FR-60・§9.20)。テストの種類・日時・パッド・結果の数と判定だけを持ち、時刻つきの入力は持たない(INV-8)。
// JSON の読み書きもここで行い、ファイルの置き場所と読み書きは App の DiagStore が受け持つ。
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PadSight.Core;

/// <param name="Kind">"buttons"・"paddles"・"stick_drift"・"stick_outer"・"snapback"・"resolution"・"linearity"・"trigger"・"chatter"・"vibration"・"idle" など</param>
/// <param name="Title">一覧に出す日本語の名前(例「スナップバック(左)」)</param>
/// <param name="Values">名前 → 数(画面に出す名前で。例「最大の振れ」→ 0.12)</param>
/// <param name="Verdict">判定の文(断定しない)。無ければ空</param>
/// <param name="Shape">外周の形など、レポートに描く数の列(0〜1)。無ければ空</param>
public sealed record DiagRecord(
    string Kind, string Title, DateTime AtUtc, string PadName, string PadKind,
    IReadOnlyList<KeyValuePair<string, double>> Values, string Verdict, IReadOnlyList<double> Shape, string AppVersion)
{
    public const int FormatVersion = 1;

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["format"] = FormatVersion,
            ["kind"] = Kind,
            ["title"] = Title,
            ["at"] = AtUtc.ToString("o"),
            ["pad"] = PadName,
            ["padKind"] = PadKind,
            // JSON は NaN・無限大を書けない(書こうとすると例外)。値は数でない物を外し、形は「データ無し」として 0 にする
            ["values"] = new JsonArray([.. Values.Where(kv => double.IsFinite(kv.Value)).Select(kv => (JsonNode)new JsonObject { ["name"] = kv.Key, ["value"] = kv.Value })]),
            ["verdict"] = Verdict,
            ["shape"] = new JsonArray([.. Shape.Select(v => (JsonNode)JsonValue.Create(double.IsFinite(v) ? v : 0))]),
            ["app"] = AppVersion,
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>読めなければ null(壊れた・形の違うファイルは一覧から外す)。</summary>
    public static DiagRecord? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
                return null;
            var values = (o["values"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(v => new KeyValuePair<string, double>(v["name"]!.GetValue<string>(), v["value"]!.GetValue<double>()))
                .ToList();
            var shape = (o["shape"] as JsonArray ?? []).Select(v => v!.GetValue<double>()).ToList();
            return new DiagRecord(
                o["kind"]!.GetValue<string>(), o["title"]!.GetValue<string>(),
                DateTime.Parse(o["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                o["pad"]?.GetValue<string>() ?? "", o["padKind"]?.GetValue<string>() ?? "",
                values, o["verdict"]?.GetValue<string>() ?? "", shape, o["app"]?.GetValue<string>() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NullReferenceException or ArgumentException)
        {
            return null;
        }
    }
}
