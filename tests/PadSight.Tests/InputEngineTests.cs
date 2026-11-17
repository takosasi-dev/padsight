// イベントの列を InputEngine に流し、入力履歴・セッション・回数がつながって動くこと(AC-1・AC-3・AC-16・NFR-2・E-2)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class InputEngineTests
{
    static (InputEngine e, FakeClock c) Start(EngineConfig? cfg = null)
    {
        var c = new FakeClock { MonotonicNs = 0 };
        var e = T.Engine(c, cfg);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        return (e, c);
    }

    [Fact]
    public void Dpad_DownDownRightRight_ThenSouth_RowsLikeAC16()
    {
        var (e, _) = Start();
        e.Down(100 * T.Ms, PadButton.DpadDown);
        e.Down(200 * T.Ms, PadButton.DpadRight);
        e.Up(300 * T.Ms, PadButton.DpadDown);
        e.Down(400 * T.Ms, PadButton.South);
        var h = e.Snapshot(500 * T.Ms, 0).History;
        Assert.Equal([6, 6, 3, 2], h.Select(r => r.Direction));
        Assert.Equal(["South"], h[0].NewlyPressed);
        Assert.Null(h[0].EndNs);
        Assert.Equal(400 * T.Ms, h[1].EndNs);
        Assert.Equal("6", HistoryBuilder.FormatLength(h[3].EndNs!.Value - h[3].StartNs, 60)); // 100 ms
    }

    [Fact]
    public void ButtonRelease_AddsNoRow_DpadNotListedAsButton()
    {
        var (e, _) = Start();
        e.Tap(100 * T.Ms, PadButton.South);
        e.Tap(200 * T.Ms, PadButton.DpadUp);
        var h = e.Snapshot(300 * T.Ms, 0).History;
        // South、方向 8、離して方向 5 の 3 行(South を離しても行は増えない)
        Assert.Equal([5, 8, 5], h.Select(r => r.Direction));
        Assert.All(h, r => Assert.DoesNotContain("DpadUp", r.NewlyPressed));
    }

    [Fact]
    public void Trigger_PressStartIsRowAndInput()
    {
        var (e, _) = Start();
        e.Apply(new AxisChanged(100 * T.Ms, 1, PadAxis.RightTrigger, 20000));
        e.Apply(new AxisChanged(110 * T.Ms, 1, PadAxis.RightTrigger, 30000));
        e.Apply(new AxisChanged(120 * T.Ms, 1, PadAxis.RightTrigger, 0));
        e.Apply(new AxisChanged(2000 * T.Ms, 1, PadAxis.RightTrigger, 32767));
        var snap = e.Snapshot(2100 * T.Ms, 0);
        Assert.Equal(2, snap.History.Count);
        Assert.Equal(["RightTrigger"], snap.History[0].NewlyPressed);
        Assert.Equal(2, snap.Session.Inputs);
        Assert.Equal(0, snap.PressCounter); // トリガーはボタンの押下ではない
    }

    [Fact]
    public void Inputs_CountButtonsDpadTriggers_NotStick()
    {
        var (e, _) = Start();
        e.Tap(1 * T.S, PadButton.South);
        e.Tap(2 * T.S, PadButton.DpadDown);
        e.Apply(new AxisChanged(3 * T.S, 1, PadAxis.LeftTrigger, 32767));
        e.Apply(new AxisChanged(4 * T.S, 1, PadAxis.LeftX, 30000));
        e.Apply(new AxisChanged(5 * T.S, 1, PadAxis.LeftX, -30000));
        e.Apply(new AxisChanged(6 * T.S, 1, PadAxis.LeftX, 0));
        e.Apply(new AxisChanged(7 * T.S, 1, PadAxis.LeftY, 30000));
        e.Tick(7 * T.S);
        var v = e.Snapshot(7 * T.S, 0).Session;
        Assert.Equal(3, v.Inputs);
        Assert.Equal(3, v.Last60);
        Assert.Equal(["DpadDown", "LeftTrigger", "South"], v.Top5.Select(kv => kv.Key));
    }

    [Fact]
    public void StickOperation_KeepsSessionAlive()
    {
        var (e, _) = Start(T.Config(idleMinutes: 1));
        e.Tap(0, PadButton.South);
        e.Apply(new AxisChanged(50 * T.S, 1, PadAxis.LeftX, 30000));  // 操作(0.25 を越えた)
        e.Tick(100 * T.S);
        Assert.True(e.SessionActive);
        e.Tick(111 * T.S);
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(50, rec.DurationSeconds);
        Assert.Equal(1, rec.Inputs);
    }

    [Fact]
    public void IdleSplit_AtSessionIdleMinutes()
    {
        var (e, _) = Start(T.Config(idleMinutes: 1));
        e.Tap(0, PadButton.South);
        e.Tap(5 * T.S, PadButton.South);
        e.Tick(64 * T.S);
        Assert.Empty(e.TakeFinalized());
        e.Tick(66 * T.S);
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(5, rec.DurationSeconds);
        Assert.False(e.SessionActive);
        Assert.Equal(5, e.Snapshot(66 * T.S, 0).Session.PreviousDurationSeconds);
    }

    [Fact]
    public void AllPadsRemoved_ClosesSession()
    {
        var (e, _) = Start();
        e.Apply(new PadAdded(0, 2, T.Xbox(2, "guid-2")));
        e.Tap(0, PadButton.South);
        e.Tap(4 * T.S, PadButton.South);
        e.Apply(new PadRemoved(5 * T.S, 1));
        Assert.Empty(e.TakeFinalized());
        Assert.True(e.SessionActive);
        e.Apply(new PadRemoved(6 * T.S, 2));
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(4, rec.DurationSeconds);
        Assert.Equal("Xbox", rec.PadKind);
    }

    [Fact]
    public void CloseSession_Manual()
    {
        var (e, _) = Start();
        e.Tap(0, PadButton.South);
        e.Tap(2 * T.S, PadButton.East);
        e.CloseSession();
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(2, rec.Inputs);
        Assert.Empty(e.TakeFinalized());
        e.Tap(3 * T.S, PadButton.South);
        Assert.True(e.SessionActive);
        Assert.Equal(1, e.Snapshot(3 * T.S, 0).Session.Inputs);
    }

    [Fact]
    public void SingleOperationSession_NotFinalized()
    {
        var (e, _) = Start();
        e.Tap(0, PadButton.South);
        e.CloseSession();
        Assert.Empty(e.TakeFinalized());
    }

    [Fact]
    public void ThirtyPerSecondForSixtySeconds_Counts1800()
    {
        var (e, _) = Start();
        for (int i = 0; i < 1800; i++)
            e.Tap((ulong)i * 33_333_333UL, i % 2 == 0 ? PadButton.South : PadButton.East);
        ulong end = 1799 * 33_333_333UL + 20 * T.Ms;
        e.Tick(end);
        var snap = e.Snapshot(end, 0);
        Assert.Equal(1800, snap.Session.Inputs);
        Assert.Equal(1800, snap.Session.Last60);
        Assert.Equal(1800, snap.Session.Peak);
        Assert.Equal(1800, snap.PressCounter);
        Assert.Equal(HistoryBuilder.Capacity, snap.History.Count);
        Assert.Equal(1799 * 33_333_333UL, snap.History[0].StartNs);
        Assert.Equal(1600 * 33_333_333UL, snap.History[^1].StartNs);
        e.CloseSession();
        Assert.Equal(1800, Assert.Single(e.TakeFinalized()).Inputs);
    }

    [Fact]
    public void ClearHistory_KeepsSession()
    {
        var (e, _) = Start();
        e.Tap(0, PadButton.South);
        e.Tap(T.S, PadButton.South);
        e.ClearHistory();
        var snap = e.Snapshot(2 * T.S, 0);
        Assert.Empty(snap.History);
        Assert.Equal(2, snap.Session.Inputs);
    }

    [Fact]
    public void Unmapped_RawButtonAndHat()
    {
        var c = new FakeClock();
        var e = T.Engine(c);
        e.Apply(new PadAdded(0, 9, T.Unmapped()));
        e.Apply(new RawHatChanged(T.S, 9, 0, 3));
        e.Apply(new RawButtonChanged(2 * T.S, 9, 2, true));
        var snap = e.Snapshot(3 * T.S, 0);
        Assert.Equal(9, snap.History[0].Direction);
        Assert.Equal(["B3"], snap.History[0].NewlyPressed);
        Assert.Equal("B3", Assert.Single(snap.Session.Top5).Key);
    }

    [Fact]
    public void StartUtc_ComesFromClockAtFirstOperation()
    {
        var c = new FakeClock { UtcNow = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc), MonotonicNs = 100 * T.S };
        var e = T.Engine(c);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        e.Tap(98 * T.S, PadButton.South);
        c.UtcNow = c.UtcNow.AddDays(1); // 時計が変えられた
        c.MonotonicNs = 200 * T.S;
        e.Tap(158 * T.S, PadButton.South);
        e.CloseSession();
        var rec = Assert.Single(e.TakeFinalized());
        Assert.Equal(new DateTime(2026, 11, 17, 11, 59, 58, DateTimeKind.Utc), rec.StartUtc);
        Assert.Equal(60, rec.DurationSeconds);
        Assert.Equal(rec.StartUtc.AddSeconds(60), rec.EndUtc);
    }
}
