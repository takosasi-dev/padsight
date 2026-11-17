// セッションの区切り・回数・直近 60 秒・60 秒の最大と、日時の出し方(仕様書 §9.6.1・AC-3・E-17・E-18・E-28)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class SessionTrackerTests
{
    static void Press(SessionTracker s, ulong ns, string key = "South")
    {
        s.OnOperation(ns);
        s.OnInput(ns, key, null);
    }

    [Fact]
    public void Tick_IdlePassed_ClosesAtLastOperation()
    {
        var s = new SessionTracker(new FakeClock()) { IdleMinutes = 1 };
        Press(s, 0);
        Press(s, 10 * T.S);
        Assert.Null(s.Tick(10 * T.S + 59 * T.S));
        var rec = s.Tick(10 * T.S + 61 * T.S);
        Assert.NotNull(rec);
        Assert.Equal(10, rec.DurationSeconds);
        Assert.Equal(2, rec.Inputs);
        Assert.False(s.Active);
    }

    [Fact]
    public void OnOperation_AfterIdleWithoutTick_ClosesOldAndStartsNew()
    {
        var ids = new Queue<Guid>([Guid.NewGuid(), Guid.NewGuid()]);
        var first = ids.Peek();
        var s = new SessionTracker(new FakeClock(), ids.Dequeue) { IdleMinutes = 1 };
        Press(s, 0);
        Press(s, 5 * T.S);
        var closed = s.OnOperation(5 * T.S + 2 * T.Min); // スリープ明け(E-18)
        Assert.NotNull(closed);
        Assert.Equal(first, closed.Id);
        Assert.Equal(5, closed.DurationSeconds);
        Assert.True(s.Active);
        Assert.Equal(0, s.View().Inputs);
    }

    [Fact]
    public void Close_SingleOperation_NotSaved()
    {
        var s = new SessionTracker(new FakeClock());
        Press(s, 0);
        Assert.Null(s.Close());
        Assert.False(s.Active);
    }

    [Fact]
    public void Close_Manual_ReturnsRecordAndNextOperationStartsNew()
    {
        var s = new SessionTracker(new FakeClock());
        Press(s, 0);
        Press(s, 3 * T.S);
        var rec = s.Close();
        Assert.NotNull(rec);
        Assert.Equal(3, rec.DurationSeconds);
        Assert.Equal(3, s.View().PreviousDurationSeconds);
        Assert.Equal(2, s.View().PreviousInputs);
        Press(s, 4 * T.S);
        Assert.True(s.Active);
        Assert.Equal(1, s.View().Inputs);
    }

    [Fact]
    public void OnOperation_Over24Hours_ClosesAndStartsNew()
    {
        var s = new SessionTracker(new FakeClock(), Guid.NewGuid) { IdleMinutes = 60 };
        SessionRecord? closed = null;
        ulong t = 0;
        Guid? firstId = null;
        while (closed is null)
        {
            Press(s, t);
            firstId ??= s.Checkpoint()!.Id;
            t += 59 * T.Min;
            closed = s.OnOperation(t);
        }
        Assert.Equal(firstId, closed.Id);
        Assert.True(closed.DurationSeconds <= 24 * 3600);
        Assert.True(closed.DurationSeconds > 23 * 3600);
        Assert.True(s.Active);
        Assert.NotEqual(firstId, s.Checkpoint()!.Id);
    }

    [Fact]
    public void Tick_Over24Hours_Closes()
    {
        var s = new SessionTracker(new FakeClock()) { IdleMinutes = 60 };
        for (ulong t = 0; t < 24 * 60 * T.Min; t += 30 * T.Min)
        {
            Press(s, t);
            Assert.Null(s.Tick(t + T.S));
        }
        var rec = s.Tick(24 * 60 * T.Min);
        Assert.NotNull(rec);
        Assert.Equal(23 * 3600 + 1800, rec.DurationSeconds);
        Assert.True(GrindLogCsv.Minutes(rec.DurationSeconds) <= 1440);
    }

    [Fact]
    public void Last60AndPeak_SlidingWindow()
    {
        var s = new SessionTracker(new FakeClock());
        for (int i = 0; i < 10; i++)
            Press(s, (ulong)i * T.S);           // 0〜9 秒に 10 回
        Assert.Equal(10, s.View().Last60);
        Press(s, 30 * T.S);
        Assert.Null(s.Tick(30 * T.S + 250 * T.Ms));
        Assert.Equal(11, s.View().Last60);
        s.Tick(65 * T.S);                       // 0〜5 秒の 6 回が窓の外
        Assert.Equal(5, s.View().Last60);
        s.Tick(95 * T.S);
        Assert.Equal(0, s.View().Last60);
        Assert.Equal(11, s.View().Peak);
        Assert.Equal(11, s.View().Inputs);
    }

    [Fact]
    public void Top5_OrderedByCountThenKeyOrdinal()
    {
        var s = new SessionTracker(new FakeClock());
        ulong t = 0;
        foreach (var (key, n) in new[] { ("South", 3), ("East", 3), ("DpadDown", 5), ("North", 1), ("West", 2), ("RightTrigger", 1) })
            for (int i = 0; i < n; i++)
                Press(s, t += T.S, key);
        Assert.Equal(["DpadDown", "East", "South", "West", "North"], s.View().Top5.Select(kv => kv.Key));
    }

    [Fact]
    public void StartUtc_FromClockAtStart_DurationFromNs()
    {
        var clock = new FakeClock { UtcNow = new DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc), MonotonicNs = 1000 * T.S };
        var s = new SessionTracker(clock) { IdleMinutes = 60 };
        Press(s, 990 * T.S); // 時計を読んだ 10 秒前のイベント
        Assert.Equal(new DateTime(2026, 11, 17, 10, 59, 50, DateTimeKind.Utc), s.Checkpoint()!.StartUtc);

        // 時計が 3 時間戻されても(E-17)、長さと終わりは ns の差で決まる
        clock.UtcNow = clock.UtcNow.AddHours(-3);
        clock.MonotonicNs = 2000 * T.S;
        Press(s, 1990 * T.S);
        var rec = s.Close()!;
        Assert.Equal(new DateTime(2026, 11, 17, 10, 59, 50, DateTimeKind.Utc), rec.StartUtc);
        Assert.Equal(1000, rec.DurationSeconds);
        Assert.Equal(rec.StartUtc.AddSeconds(1000), rec.EndUtc);
    }

    [Fact]
    public void Record_PadIsTheOneWithMostInputs()
    {
        var s = new SessionTracker(new FakeClock());
        var a = T.Xbox(1, name: "A");
        var b = T.Xbox(2, name: "B");
        ulong t = 0;
        foreach (var p in new[] { a, b, b, a, b })
        {
            s.OnOperation(t += T.S);
            s.OnInput(t, "South", p);
        }
        Assert.Equal("B", s.Close()!.PadName);
    }
}
