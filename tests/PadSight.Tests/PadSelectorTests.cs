// 表示するパッドの選び方(仕様書 FR-4・D-23・E-2・E-3)を、InputEngine にイベントを流して確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class PadSelectorTests
{
    static InputEngine TwoPads(EngineConfig cfg)
    {
        var e = T.Engine(new FakeClock(), cfg);
        e.Apply(new PadAdded(0, 1, T.Xbox(1, "guid-a", "A")));
        e.Apply(new PadAdded(0, 2, T.Xbox(2, "guid-b", "B")));
        return e;
    }

    [Fact]
    public void LastUsed_FirstConnectedSelectedBeforeAnyOperation()
    {
        var e = TwoPads(T.Config());
        Assert.Equal(1u, e.SelectedId);
    }

    [Fact]
    public void LastUsed_UnoperatedCurrent_SwitchesImmediately()
    {
        var e = TwoPads(T.Config());
        e.Tap(T.S, PadButton.South, id: 2);
        Assert.Equal(2u, e.SelectedId);
    }

    [Fact]
    public void LastUsed_SwitchesOnlyAfterTwoSecondsQuiet()
    {
        var e = TwoPads(T.Config());
        e.Tap(10 * T.S, PadButton.South, id: 1);
        e.Tap(11 * T.S, PadButton.South, id: 2);         // A は 1 秒前に操作 → そのまま
        Assert.Equal(1u, e.SelectedId);
        e.Tap(11_500 * T.Ms, PadButton.South, id: 1);
        e.Tap(13 * T.S, PadButton.South, id: 2);         // A は 1.5 秒前 → そのまま
        Assert.Equal(1u, e.SelectedId);
        e.Tap(14 * T.S, PadButton.South, id: 2);         // A は 2.5 秒前 → 切り替え
        Assert.Equal(2u, e.SelectedId);
        Assert.Equal("B", e.Snapshot(14 * T.S, 0).Pad?.Name);
    }

    [Fact]
    public void LastUsed_SelectedRemoved_FallsBackToOther()
    {
        var e = TwoPads(T.Config());
        e.Tap(T.S, PadButton.South, id: 1);
        e.Apply(new PadRemoved(2 * T.S, 1));
        Assert.Equal(2u, e.SelectedId);
    }

    [Fact]
    public void Pinned_IgnoresOtherPadAndStaysDisconnected()
    {
        var e = TwoPads(T.Config(mode: "pinned", pinned: "guid-b"));
        Assert.Equal(2u, e.SelectedId);
        e.Tap(T.S, PadButton.South, id: 1);
        e.Tap(5 * T.S, PadButton.South, id: 1);
        Assert.Equal(2u, e.SelectedId);

        e.Apply(new PadRemoved(6 * T.S, 2));
        Assert.Null(e.SelectedId);                       // 最後に操作には戻らない
        Assert.False(e.Snapshot(6 * T.S, 0).Connected);

        e.Apply(new PadAdded(7 * T.S, 3, T.Xbox(3, "guid-b", "B again")));
        Assert.Equal(3u, e.SelectedId);
    }

    [Fact]
    public void Pinned_SameGuidTwice_FirstConnectedWins()
    {
        var e = T.Engine(new FakeClock(), T.Config(mode: "pinned", pinned: "guid-x"));
        e.Apply(new PadAdded(0, 5, T.Xbox(5, "guid-x")));
        e.Apply(new PadAdded(0, 4, T.Xbox(4, "guid-x")));
        Assert.Equal(5u, e.SelectedId);
    }
}
