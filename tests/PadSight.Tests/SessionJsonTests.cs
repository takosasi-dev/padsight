// sessions.jsonl の 1 行と current_session.json の形(仕様書 §9.6.2・§9.6.4・E-14)の書き出しと読み取りを確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class SessionJsonTests
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    const string SpecSample = """{"schemaVersion":1,"id":"3f2b8c1e-5d4a-4e7b-9c0d-1a2b3c4d5e6f","start":"2026-11-17T20:15:03+09:00","end":"2026-11-17T21:02:44+09:00","durationSeconds":2861,"pad":{"kind":"PlayStation","name":"DualSense Wireless Controller"},"inputs":5123,"peakPerMinute":182,"buttons":{"South":1201,"East":640,"DpadDown":312,"RightTrigger":95}}""";

    [Fact]
    public void Parse_SpecSample_ThenToLine_Identical()
    {
        var r = SessionJson.Parse(SpecSample);
        Assert.NotNull(r);
        Assert.Equal(new DateTime(2026, 11, 17, 11, 15, 3, DateTimeKind.Utc), r.StartUtc);
        Assert.Equal(2861, r.DurationSeconds);
        Assert.Equal("PlayStation", r.PadKind);
        Assert.Equal(1201, r.Buttons["South"]);
        Assert.Null(r.LastOperationUtc);
        // FR-37 より前の行(peakPerSecond が無い)は 0 で読み、書くときは peakPerSecond が加わる(AC-45)
        Assert.Equal(0, r.PeakPerSecond);
        Assert.Equal(SpecSample.Replace("\"peakPerMinute\":182,", "\"peakPerMinute\":182,\"peakPerSecond\":0,"), SessionJson.ToLine(r, Jst));
    }

    [Fact]
    public void ToLine_Parse_RoundTrip_WithLastOperation()
    {
        var start = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var r = new SessionRecord(Guid.NewGuid(), start, start.AddSeconds(90), 90, "Xbox", "Pad \"1\"", 42, 30,
            new Dictionary<string, long> { ["South"] = 40, ["B3"] = 2, ["North"] = 0 }, start.AddSeconds(90));
        var line = SessionJson.ToLine(r, Jst);
        Assert.DoesNotContain("North", line); // 0 回の物は書かない
        Assert.DoesNotContain("\n", line);
        Assert.Contains("\"lastOperation\":\"2026-01-02T12:05:35+09:00\"", line);

        var back = SessionJson.Parse(line)!;
        Assert.Equal(r.Id, back.Id);
        Assert.Equal(r.StartUtc, back.StartUtc);
        Assert.Equal(r.EndUtc, back.EndUtc);
        Assert.Equal(r.DurationSeconds, back.DurationSeconds);
        Assert.Equal(r.PadKind, back.PadKind);
        Assert.Equal(r.PadName, back.PadName);
        Assert.Equal(r.Inputs, back.Inputs);
        Assert.Equal(r.PeakPerMinute, back.PeakPerMinute);
        Assert.Equal(r.LastOperationUtc, back.LastOperationUtc);
        Assert.Equal(2, back.Buttons.Count);
        Assert.Equal(2, back.Buttons["B3"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1,"id":"zzz","start":"2026-11-17T20:15:03+09:00","end":"2026-11-17T21:02:44+09:00","durationSeconds":1,"inputs":1}""")]
    [InlineData("""{"schemaVersion":1,"id":"3f2b8c1e-5d4a-4e7b-9c0d-1a2b3c4d5e6f","start":"yesterday","end":"2026-11-17T21:02:44+09:00","durationSeconds":1,"inputs":1}""")]
    [InlineData("""{"schemaVersion":1,"id":"3f2b8c1e-5d4a-4e7b-9c0d-1a2b3c4d5e6f","start":"2026-11-17T20:15:03+09:00","end":"2026-11-17T21:02:44+09:00","durationSeconds":"long","inputs":1}""")]
    [InlineData("""{"schemaVersion":1,"id":"3f2b8c1e-5d4a-4e7b-9c0d-1a2b3c4d5e6f","start":"2026-11-17T20:15:03+09:00","end":"2026-11-17T21:02:44+09:00","durationSeconds":1,"inputs":1,"buttons":{"South":"x"}}""")]
    [InlineData("""{"schemaVersion":1,"id":"3f2b8c1e""")]
    public void Parse_BadLine_ReturnsNull(string line) => Assert.Null(SessionJson.Parse(line));

    [Fact]
    public void Format_UsesZoneOffset()
    {
        var utc = new DateTime(2026, 11, 17, 15, 30, 0, DateTimeKind.Utc);
        Assert.Equal("2026-11-18T00:30:00+09:00", SessionJson.Format(utc, Jst));
        var minus = TimeZoneInfo.CreateCustomTimeZone("Test-0330", TimeSpan.FromMinutes(-210), "m", "m");
        Assert.Equal("2026-11-17T12:00:00-03:30", SessionJson.Format(utc, minus));
    }
}
