// GrindLog 向けの CSV(仕様書 §9.6.3・AC-32・E-30)を確かめる。
// ヘッダー、17 列、ID・日付・時刻・分の丸め・固定の列、引用符の付け方、メモの上位 3 つ。
using System.Text;
using PadSight.Core;

namespace PadSight.Tests;

public class GrindLogCsvTests
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    static SessionRecord Rec(long seconds, DateTime? startUtc = null, Guid? id = null, string pad = "Xbox Wireless Controller",
        Dictionary<string, long>? buttons = null, long inputs = 100, int peak = 50, string kind = "Xbox")
    {
        var start = startUtc ?? new DateTime(2026, 11, 17, 11, 15, 3, DateTimeKind.Utc);
        return new SessionRecord(id ?? Guid.NewGuid(), start, start.AddSeconds(seconds), seconds, kind, pad, inputs, peak,
            buttons ?? new Dictionary<string, long> { ["South"] = 100 });
    }

    /// <summary>引用符を考えて 1 行を列に分ける(GrindLog の取り込み側の読み方)。</summary>
    static List<string> SplitRow(string line)
    {
        var cols = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') q = false;
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { cols.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cols.Add(sb.ToString());
        return cols;
    }

    static string[] Lines(string csv)
    {
        Assert.EndsWith("\r\n", csv);
        return csv[..^2].Split("\r\n");
    }

    [Fact]
    public void Header_Exact()
    {
        var lines = Lines(GrindLogCsv.Build([], "G", "T", Jst));
        Assert.Equal("ID,日付,開始時刻,ゲーム,種別,時間(分),キル,デス,アシスト,結果,ランク表記,レート,調子,メモ,タグ,出所,取込元ID", Assert.Single(lines));
    }

    [Fact]
    public void Row_AllColumns()
    {
        var id = Guid.Parse("3F2B8C1E-5D4A-4E7B-9C0D-1A2B3C4D5E6F");
        var r = Rec(2861, new DateTime(2026, 11, 17, 15, 30, 0, DateTimeKind.Utc), id); // +09:00 では翌日 00:30
        var lines = Lines(GrindLogCsv.Build([r], "スト6", "パッド練習", Jst));
        Assert.Equal(2, lines.Length);
        var c = SplitRow(lines[1]);
        Assert.Equal(17, c.Count);
        Assert.Equal("3f2b8c1e-5d4a-4e7b-9c0d-1a2b3c4d5e6f", c[0]);
        Assert.Equal("2026-11-18", c[1]);
        Assert.Equal("00:30", c[2]);
        Assert.Equal("スト6", c[3]);
        Assert.Equal("パッド練習", c[4]);
        Assert.Equal("48", c[5]);   // 47.68 分
        Assert.Equal(["", "", ""], c[6..9]);
        Assert.Equal("なし", c[9]);
        Assert.Equal(["", "", ""], c[10..13]);
        Assert.StartsWith("PadSight: 入力 100 回", c[13]);
        Assert.Equal("PadSight", c[14]);
        Assert.Equal("手入力", c[15]);
        Assert.Equal("", c[16]);
    }

    [Theory]
    [InlineData(59, null)]
    [InlineData(0, null)]
    [InlineData(60, "1")]
    [InlineData(89, "1")]
    [InlineData(90, "2")]
    [InlineData(149, "2")]
    [InlineData(150, "3")]
    [InlineData(86400, "1440")]
    public void Minutes_RoundingAndUnder60Excluded(long seconds, string? expected)
    {
        var lines = Lines(GrindLogCsv.Build([Rec(seconds)], "G", "T", Jst));
        if (expected is null)
            Assert.Single(lines);
        else
            Assert.Equal(expected, SplitRow(lines[1])[5]);
    }

    [Fact]
    public void Build_OrderedOldestFirst_ShortOnesDropped()
    {
        var t0 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = Rec(120, t0.AddDays(2));
        var b = Rec(120, t0);
        var c = Rec(30, t0.AddDays(1));
        var lines = Lines(GrindLogCsv.Build([a, b, c], "G", "T", Jst));
        Assert.Equal(3, lines.Length);
        Assert.Equal(b.Id.ToString(), SplitRow(lines[1])[0]);
        Assert.Equal(a.Id.ToString(), SplitRow(lines[2])[0]);
    }

    [Fact]
    public void Build_SameSessionTwice_SameId()
    {
        var r = Rec(600);
        var first = SplitRow(Lines(GrindLogCsv.Build([r], "G", "T", Jst))[1])[0];
        var second = SplitRow(Lines(GrindLogCsv.Build([r], "G", "T", Jst))[1])[0];
        Assert.Equal(r.Id.ToString("D"), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_GameWithSurroundingSpaces_WrittenTrimmed()
    {
        // AC-32: 前後に空白のあるゲームの名前は、除いた値で書かれる
        var c = SplitRow(Lines(GrindLogCsv.Build([Rec(600)], "  Apex  ", " ランク ", Jst))[1]);
        Assert.Equal("Apex", c[3]);
        Assert.Equal("ランク", c[4]);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData(" lead", "\" lead\"")]
    [InlineData("trail ", "\"trail \"")]
    [InlineData("two\r\nlines", "\"two\r\nlines\"")]
    [InlineData("", "")]
    public void Quote_Rules(string v, string expected) => Assert.Equal(expected, GrindLogCsv.Quote(v));

    [Fact]
    public void Build_PadNameWithCommaAndQuote_RoundTripsThroughSplit()
    {
        var r = Rec(600, pad: "Pad, \"Pro\"");
        var line = Lines(GrindLogCsv.Build([r], "G", "T", Jst))[1];
        var c = SplitRow(line);
        Assert.Equal(17, c.Count);
        Assert.Contains("パッド Pad, \"Pro\"、", c[13]);
    }

    [Fact]
    public void Memo_Top3_XboxLabels()
    {
        var r = Rec(2861, inputs: 5123, peak: 182, pad: "Xbox Wireless Controller",
            buttons: new() { ["South"] = 1201, ["East"] = 640, ["DpadDown"] = 312, ["RightTrigger"] = 95 });
        Assert.Equal("PadSight: 入力 5123 回、1 分あたり 107.4、60 秒の最大 182、パッド Xbox Wireless Controller、多いボタン A 1201 / B 640 / ↓ 312",
            GrindLogCsv.Memo(r));
    }

    [Fact]
    public void Memo_TieByKeyOrdinal_FewerThanThree()
    {
        var r = Rec(60, buttons: new() { ["South"] = 5, ["East"] = 5 });
        Assert.EndsWith("多いボタン B 5 / A 5", GrindLogCsv.Memo(r));
    }

    [Fact]
    public void Memo_NoButtons_Nashi()
    {
        var r = Rec(60, buttons: [], inputs: 0);
        Assert.EndsWith("多いボタン なし", GrindLogCsv.Memo(r));
        Assert.Contains("1 分あたり 0.0", GrindLogCsv.Memo(r));
    }

    [Fact]
    public void Memo_PlayStationTrigger()
    {
        var r = Rec(60, kind: "PlayStation", buttons: new() { ["RightTrigger"] = 3, ["West"] = 2 });
        Assert.EndsWith("多いボタン R2 3 / □ 2", GrindLogCsv.Memo(r));
    }

    [Fact]
    public void Memo_CappedAt2000Chars()
    {
        var r = Rec(60, pad: new string('x', 3000));
        Assert.Equal(2000, GrindLogCsv.Memo(r).Length);
    }
}
