// 1 秒の最大(FR-37・AC-45)と、パッドごとの通算(FR-38・AC-46)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class PeakAndPadTotalsTests
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    [Fact]
    public void PeakPerSecond_10PerSecThen20PerSec_Is20()
    {
        var clock = new FakeClock();
        var e = T.Engine(clock);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        ulong t = T.S;
        for (int i = 0; i < 30; i++, t += 100 * T.Ms) e.Tap(t, PadButton.South); // 1 秒に 10 回を 3 秒
        for (int i = 0; i < 20; i++, t += 50 * T.Ms) e.Tap(t, PadButton.South);  // 1 秒に 20 回を 1 秒
        Assert.Equal(20, e.Snapshot(t, 0).Session.Peak1s);
        e.CloseSession();
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(20, rec.PeakPerSecond);
        Assert.Equal(20, SessionJson.Parse(SessionJson.ToLine(rec, Jst))!.PeakPerSecond);
    }

    [Fact]
    public void PadTotals_TwoPadsTwoSessions_SumsPerGuidAndRoundTrips()
    {
        var clock = new FakeClock();
        var e = T.Engine(clock);
        e.Apply(new PadAdded(0, 1, T.Xbox(1, "guid-a", "Pad A")));
        e.Apply(new PadAdded(0, 2, T.Xbox(2, "guid-b", "Pad B")));
        var totals = new PadTotals();
        ulong t = T.S;
        for (int s = 0; s < 2; s++)
        {
            e.Tap(t, PadButton.South, 1);
            e.Tap(t + 2 * T.S, PadButton.South, 1);
            e.Tap(t + 3 * T.S, PadButton.East, 2);
            e.CloseSession();
            foreach (var r in e.TakeFinalized()) totals.Add(r);
            t += 100 * T.S;
        }
        Assert.Equal(4, totals.Pads["guid-a"].Inputs);
        Assert.Equal(4, totals.Pads["guid-a"].Buttons["South"]);
        Assert.Equal(2, totals.Pads["guid-b"].Buttons["East"]);
        Assert.Equal("Pad B", totals.Pads["guid-b"].Name);

        var back = PadTotals.Parse(totals.ToJson(Jst));
        Assert.Equal(totals.ToJson(Jst), back.ToJson(Jst));
    }

    [Fact]
    public void PadTotals_ZeroLengthSession_NotAdded()
    {
        var clock = new FakeClock();
        var e = T.Engine(clock);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        e.Tap(T.S, PadButton.South); // 操作 1 回だけ = 長さ 0 秒
        e.CloseSession();
        Assert.Empty(e.TakeFinalized());
    }
}
