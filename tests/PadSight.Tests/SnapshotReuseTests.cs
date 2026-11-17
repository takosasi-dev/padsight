// スナップショットの部品の使い回し(PerfProbe で測った軽くする直し)を確かめる。
// 変わっていない部品は前と同じ物、変わった部品は新しい物になり、前に出したスナップショットは後のイベントで変わらないこと。
using PadSight.Core;

namespace PadSight.Tests;

public class SnapshotReuseTests
{
    static (InputEngine e, FakeClock c) Start()
    {
        var c = new FakeClock();
        var e = T.Engine(c);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        e.Apply(new PadAdded(0, 2, T.Xbox(2, "guid-2", "Second")));
        e.Tap(10 * T.Ms, PadButton.South); // パッド 1 を選ばせる
        return (e, c);
    }

    static AxisChanged LeftX(ulong ns, short raw, uint id = 1) => new(ns, id, PadAxis.LeftX, raw);

    [Fact]
    public void NothingChanged_EveryPieceIsReused()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        var b = e.Snapshot(104 * T.Ms, 0);
        Assert.NotEqual(a.Version, b.Version);
        Assert.Same(a.History, b.History);
        Assert.Same(a.Pads, b.Pads);
        Assert.Same(a.Live, b.Live);
        Assert.Same(a.Sticks, b.Sticks);
        Assert.Same(a.Pressed, b.Pressed);
        Assert.Same(a.Session, b.Session);
        Assert.Same(a.Chatter, b.Chatter);
    }

    [Fact]
    public void SelectedPressed_IsTheSameObjectAsItsLiveEntry()
    {
        var (e, _) = Start();
        e.Down(100 * T.Ms, PadButton.East);
        var s = e.Snapshot(110 * T.Ms, 0);
        Assert.Same(s.Live[1].Pressed, s.Pressed);
        Assert.Equal([PadButton.East], s.Pressed);
    }

    [Fact]
    public void SmallStickMove_ReplacesSticksAndLive_KeepsHistoryPadsAndPressed()
    {
        var (e, _) = Start();
        e.Down(100 * T.Ms, PadButton.East);
        var a = e.Snapshot(110 * T.Ms, 0);
        e.Apply(LeftX(120 * T.Ms, 3000)); // しきい値の下。方向は変わらない
        var b = e.Snapshot(124 * T.Ms, 0);
        Assert.NotSame(a.Sticks, b.Sticks);
        Assert.NotSame(a.Live, b.Live);
        Assert.NotSame(a.Live[1], b.Live[1]);
        Assert.Same(a.Live[2], b.Live[2]); // 動いていないパッドの写しはそのまま
        Assert.Same(a.Pressed, b.Pressed);
        Assert.Same(a.History, b.History);
        Assert.Same(a.Pads, b.Pads);
        Assert.Equal((short)0, a.Sticks[1].Left.RawX); // 前の写しは変わらない
        Assert.Equal((short)3000, b.Sticks[1].Left.RawX);
        Assert.Equal((short)0, a.Left.RawX);
        Assert.Equal((short)3000, b.Left.RawX);
    }

    [Fact]
    public void OtherPadButton_ReplacesOnlyItsLiveEntry_OldSnapshotKeepsOldSet()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        e.Down(110 * T.Ms, PadButton.North, id: 2);
        var b = e.Snapshot(120 * T.Ms, 0);
        Assert.Same(a.Live[1], b.Live[1]);
        Assert.NotSame(a.Live[2], b.Live[2]);
        Assert.Empty(a.Live[2].Pressed);
        Assert.Equal([PadButton.North], b.Live[2].Pressed);
        Assert.Same(a.Sticks, b.Sticks); // ボタンでは軸の写しを作り直さない
        e.Up(130 * T.Ms, PadButton.North, id: 2);
        var c = e.Snapshot(140 * T.Ms, 0);
        Assert.Empty(c.Live[2].Pressed);
        Assert.Equal([PadButton.North], b.Live[2].Pressed);
    }

    [Fact]
    public void NewRow_ReplacesHistory_OldSnapshotKeepsItsRows()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        int before = a.History.Count;
        var top = a.History[0];
        e.Tap(200 * T.Ms, PadButton.East);
        var b = e.Snapshot(300 * T.Ms, 0);
        Assert.NotSame(a.History, b.History);
        Assert.Equal(before + 1, b.History.Count);
        Assert.Equal(before, a.History.Count);
        Assert.Same(top, a.History[0]); // 前の写しの行は差し替わらない
        Assert.Null(a.History[0].EndNs);
        Assert.Equal(200 * T.Ms, b.History[1].EndNs);
    }

    [Fact]
    public void MergedPress_ReplacesHistory_OldSnapshotKeepsUnmergedRow()
    {
        var (e, _) = Start();
        e.Down(1 * T.S, PadButton.South);
        var a = e.Snapshot(1 * T.S + 1 * T.Ms, 0);
        e.Down(1 * T.S + 10 * T.Ms, PadButton.East); // 2 フレームの窓の中。一番上の行にまとまる
        var b = e.Snapshot(1 * T.S + 11 * T.Ms, 0);
        Assert.NotSame(a.History, b.History);
        Assert.Equal(a.History.Count, b.History.Count);
        Assert.Equal(["South", "East"], b.History[0].NewlyPressed);
        Assert.Equal(["South"], a.History[0].NewlyPressed);
        Assert.Null(a.History[0].MergeGapNs);
    }

    [Fact]
    public void ClearHistory_EmptiesNewSnapshot_OldKeepsRows()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        e.ClearHistory();
        var b = e.Snapshot(110 * T.Ms, 0);
        Assert.NotEmpty(a.History);
        Assert.Empty(b.History);
        Assert.Same(b.History, e.Snapshot(120 * T.Ms, 0).History);
    }

    [Fact]
    public void ReleaseThenPressAgain_IsANewRow()
    {
        var (e, _) = Start();
        e.Down(1 * T.S, PadButton.East);
        e.Apply(LeftX(1 * T.S + 5 * T.Ms, 1000)); // 押したままの軸のイベントは行を増やさない
        int rows = e.Snapshot(1 * T.S + 6 * T.Ms, 0).History.Count;
        e.Up(1 * T.S + 10 * T.Ms, PadButton.East);
        e.Apply(LeftX(1 * T.S + 15 * T.Ms, 2000));
        e.Down(1 * T.S + 20 * T.Ms, PadButton.East);
        var h = e.Snapshot(1 * T.S + 30 * T.Ms, 0).History;
        Assert.Equal(rows + 1, h.Count);
        Assert.Equal(["East"], h[0].NewlyPressed);
    }

    [Fact]
    public void PadsList_ReplacedOnAddDropAndReAdd()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        e.Apply(LeftX(110 * T.Ms, 20000));
        Assert.Same(a.Pads, e.Snapshot(120 * T.Ms, 0).Pads);
        e.DropPad(2);
        var b = e.Snapshot(130 * T.Ms, 0);
        Assert.NotSame(a.Pads, b.Pads);
        Assert.Equal([1u, 2u], a.Pads.Select(p => p.InstanceId));
        Assert.Equal([1u], b.Pads.Select(p => p.InstanceId));
        Assert.False(b.Live.ContainsKey(2));
        Assert.False(b.Sticks.ContainsKey(2));
        e.Apply(new PadAdded(140 * T.Ms, 2, T.Xbox(2, "guid-new", "Again")));
        var c = e.Snapshot(150 * T.Ms, 0);
        Assert.Equal("Again", c.Pads[1].Name);
        Assert.Equal("Again", c.Live[2].Info.Name);
        Assert.Equal("Second", a.Live[2].Info.Name);
    }

    [Fact]
    public void SessionView_ReusedUntilACountChanges_Top5ResetsOnNewSession()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        Assert.Same(a.Session, e.Snapshot(104 * T.Ms, 0).Session);
        e.Tap(200 * T.Ms, PadButton.East);
        var b = e.Snapshot(300 * T.Ms, 0);
        Assert.NotSame(a.Session, b.Session);
        Assert.Equal(["South"], a.Session.Top5.Select(kv => kv.Key));
        Assert.Equal(["East", "South"], b.Session.Top5.Select(kv => kv.Key));
        // 区切りの後の新しいセッションは、上位 5 つを数え直す
        e.Tick(300 * T.Ms + 11 * T.Min);
        var closed = e.Snapshot(300 * T.Ms + 11 * T.Min, 0);
        Assert.False(closed.Session.Active);
        e.Tap(300 * T.Ms + 12 * T.Min, PadButton.North);
        var c = e.Snapshot(300 * T.Ms + 12 * T.Min + 20 * T.Ms, 0);
        Assert.True(c.Session.Active);
        Assert.Equal(["North"], c.Session.Top5.Select(kv => kv.Key));
        Assert.Equal(["East", "South"], b.Session.Top5.Select(kv => kv.Key));
    }

    [Fact]
    public void SessionView_Last60DropsOnTick_NewViewOldUnchanged()
    {
        var (e, _) = Start();
        var a = e.Snapshot(100 * T.Ms, 0);
        Assert.Equal(1, a.Session.Last60);
        e.Tick(90 * T.S);
        var b = e.Snapshot(90 * T.S, 0);
        Assert.NotSame(a.Session, b.Session);
        Assert.Equal(0, b.Session.Last60);
        Assert.Equal(1, a.Session.Last60);
        Assert.Same(a.Session.Top5, b.Session.Top5); // ボタンの回数は変わっていない
    }

    [Fact]
    public void UnmappedPad_RawStateFollowsChanges_OldSnapshotKeepsOldRaw()
    {
        var c = new FakeClock();
        var e = T.Engine(c);
        e.Apply(new PadAdded(0, 9, T.Unmapped()));
        e.Apply(new RawButtonChanged(1 * T.S, 9, 2, true));
        var a = e.Snapshot(1 * T.S + 10 * T.Ms, 0);
        Assert.NotNull(a.Raw);
        Assert.Same(a.Live[9].Raw, a.Raw);
        Assert.True(a.Raw!.Buttons[2]);
        Assert.Equal(["B3"], a.History[0].NewlyPressed);
        e.Apply(new RawButtonChanged(2 * T.S, 9, 2, false));
        var b = e.Snapshot(2 * T.S + 10 * T.Ms, 0);
        Assert.False(b.Raw!.Buttons[2]);
        Assert.True(a.Raw.Buttons[2]);
    }

    [Fact]
    public void Trail_OldSnapshotKeepsItsPoints()
    {
        var (e, _) = Start();
        e.Apply(LeftX(100 * T.Ms, 20000));
        var a = e.Snapshot(104 * T.Ms, 0);
        e.Apply(LeftX(108 * T.Ms, 25000));
        var b = e.Snapshot(112 * T.Ms, 0);
        Assert.Single(a.LeftTrail);
        Assert.Equal(2, b.LeftTrail.Count);
        Assert.Equal(100 * T.Ms, a.LeftTrail[0].Ns);
    }
}
