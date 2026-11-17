// つなぎ直しで SDL の時計が 0 に戻っても、セッションと入力履歴が続くこと(AC-54・E-29)と、
// 割り当ての一覧から Windows の行だけを残すこと(AC-60・FR-43)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class ReconnectAndMappingTests
{
    /// <summary>SdlInputThread と同じ並び: 止める前に DropPad と BeforeRestart、初期化し直したら SDL の時計は 0 から。</summary>
    static (InputEngine Engine, FakeClock Clock, TimeBase Time) PlayThenReconnect(ulong sdlBeforeQuit)
    {
        var time = new TimeBase();
        var clock = new FakeClock { MonotonicNs = time.Map(100 * T.S) };
        var e = T.Engine(clock);
        e.Apply(new PadAdded(time.Map(T.S), 1, T.Xbox()));
        for (ulong s = 10; s < 300; s += 10)
            e.Tap(time.Map(s * T.S), PadButton.South);
        e.Tick(time.Map(sdlBeforeQuit));
        e.DropPad(1);
        time.BeforeRestart(sdlBeforeQuit);
        e.Apply(new PadAdded(time.Map(T.S / 2), 1, T.Xbox())); // 新しい SDL の時計で 0.5 秒
        return (e, clock, time);
    }

    [Fact]
    public void Reconnect_KeepsSessionOpen()
    {
        var (e, clock, time) = PlayThenReconnect(300 * T.S);
        clock.MonotonicNs = time.Map(T.S);
        e.Tick(time.Map(T.S));
        Assert.True(e.SessionActive);
        Assert.Empty(e.TakeFinalized());
    }

    [Fact]
    public void Reconnect_HistoryLengthContinues()
    {
        var (e, _, time) = PlayThenReconnect(300 * T.S);
        e.Down(time.Map(2 * T.S), PadButton.East);
        var rows = e.Snapshot(time.Map(3 * T.S), 0).History;
        // 1 つ前の行は 290 秒の押下から(離しただけでは行を足さない。D-10)、つなぎ直した時計の 2 秒の押下まで = 12 秒。999+ にならない
        var prev = rows[1];
        Assert.NotNull(prev.EndNs);
        Assert.Equal(12 * T.S, prev.EndNs!.Value - prev.StartNs);
    }

    [Fact]
    public void Reconnect_IdleStillClosesSession()
    {
        var (e, _, time) = PlayThenReconnect(300 * T.S);
        e.Tick(time.Map(11 * T.Min)); // 最後の操作から 10 分を過ぎた
        Assert.False(e.SessionActive);
        Assert.Single(e.TakeFinalized());
    }

    [Fact]
    public void TimeBase_AddsEveryRestart()
    {
        var t = new TimeBase();
        Assert.Equal(5UL, t.Map(5));
        t.BeforeRestart(100);
        t.BeforeRestart(50);
        Assert.Equal(155UL, t.Map(5));
    }

    static string Line(string platform, int i) => $"{i:x32},Pad {i},a:b0,b:b1,platform:{platform},\n";

    [Fact]
    public void WindowsOnly_KeepsCommentsAndWindowsRows()
    {
        string text = "# Game Controller DB\n\n" + string.Concat(Enumerable.Range(0, 120).Select(i => Line("Windows", i)))
            + string.Concat(Enumerable.Range(200, 50).Select(i => Line("Mac OS X", i)))
            + "# Linux\r\n" + Line("Linux", 300);
        string kept = MappingDb.WindowsOnly(text);
        Assert.DoesNotContain("platform:Mac OS X,", kept);
        Assert.DoesNotContain("platform:Linux,", kept);
        Assert.Contains("# Game Controller DB\n", kept);
        Assert.Contains("# Linux\n", kept);
        Assert.Equal(120, MappingDb.Check(kept));
        Assert.Equal(MappingDb.Check(text), MappingDb.Check(kept));
    }
}

/// <summary>別のスレッドから積まれたイベントの時刻が少し戻っても、セッションが割れず、選び直しの 2 秒も守られる(点検で見つかった不具合)。</summary>
public class BackwardTimestampTests
{
    [Fact]
    public void SlightlyEarlierEvent_DoesNotSplitSession()
    {
        var e = T.Engine(new FakeClock());
        e.Apply(new PadAdded(T.S, 1, T.Xbox()));
        e.Apply(new PadAdded(T.S, 2, T.Xbox(2, "guid-b", "B")));
        e.Tap(10 * T.S, PadButton.South, 1);
        e.Tap(20 * T.S, PadButton.South, 1);
        e.Tap(20 * T.S - T.Ms, PadButton.South, 2); // 1 ミリ秒前の時刻で届いた
        e.Tap(30 * T.S, PadButton.South, 1);
        e.Tick(31 * T.S);
        Assert.True(e.SessionActive);
        Assert.Empty(e.TakeFinalized());
    }

    [Fact]
    public void TickSlightlyBehindLastOperation_KeepsSession()
    {
        var e = T.Engine(new FakeClock());
        e.Apply(new PadAdded(T.S, 1, T.Xbox()));
        e.Tap(10 * T.S, PadButton.South);
        e.Tap(11 * T.S, PadButton.South);
        e.Tick(11 * T.S - T.Ms); // 最後の操作より 1 ミリ秒前の時刻で区切りを見る
        Assert.True(e.SessionActive);
        Assert.Empty(e.TakeFinalized());
    }

    [Fact]
    public void SlightlyEarlierEvent_DoesNotSwitchPadImmediately()
    {
        var e = T.Engine(new FakeClock());
        e.Apply(new PadAdded(T.S, 1, T.Xbox()));
        e.Apply(new PadAdded(T.S, 2, T.Xbox(2, "guid-b", "B")));
        e.Tap(10 * T.S, PadButton.South, 1);
        e.Tap(10 * T.S - T.Ms, PadButton.South, 2); // 1 のすぐ後に届いたが時刻は 1 ミリ秒前
        Assert.Equal(1u, e.SelectedId);
    }
}
