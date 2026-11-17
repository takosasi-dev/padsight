// ボタンの検査(AC-56・FR-45)の ChatterCounter と InputEngine を通した数え方、トリガーの範囲(AC-57・FR-46)の判定を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class DiagCheckTests
{
    static ChatterCount Count(ChatterCounter c, uint pad, PadButton b) => c.Snapshot()[pad][b];

    [Fact]
    public void ReleaseThen14ms_CountsShort_15msDoesNot()
    {
        var c = new ChatterCounter();
        c.Apply(1, PadButton.South, true, 100 * T.Ms);
        c.Apply(1, PadButton.South, false, 110 * T.Ms);
        c.Apply(1, PadButton.South, true, 124 * T.Ms); // 14 ms
        Assert.Equal(new ChatterCount(2, 1), Count(c, 1, PadButton.South));
        c.Apply(1, PadButton.South, false, 130 * T.Ms);
        c.Apply(1, PadButton.South, true, 145 * T.Ms); // 15 ms ちょうどは数えない
        Assert.Equal(new ChatterCount(3, 1), Count(c, 1, PadButton.South));
    }

    [Fact]
    public void FirstPress_NotShort_RepeatedDownNotCounted()
    {
        var c = new ChatterCounter();
        c.Apply(1, PadButton.East, true, 1 * T.Ms);
        c.Apply(1, PadButton.East, true, 2 * T.Ms);
        Assert.Equal(new ChatterCount(1, 0), Count(c, 1, PadButton.East));
    }

    [Fact]
    public void OtherButtonOrOtherPad_NotShort()
    {
        var c = new ChatterCounter();
        c.Apply(1, PadButton.South, true, 100 * T.Ms);
        c.Apply(1, PadButton.South, false, 110 * T.Ms);
        c.Apply(1, PadButton.East, true, 111 * T.Ms);
        c.Apply(2, PadButton.South, true, 112 * T.Ms);
        Assert.Equal(new ChatterCount(1, 0), Count(c, 1, PadButton.East));
        Assert.Equal(new ChatterCount(1, 0), Count(c, 2, PadButton.South));
        Assert.Equal(new ChatterCount(1, 0), Count(c, 1, PadButton.South));
    }

    [Fact]
    public void Reset_BackToZero()
    {
        var c = new ChatterCounter();
        c.Apply(1, PadButton.South, true, 100 * T.Ms);
        c.Apply(1, PadButton.South, false, 110 * T.Ms);
        c.Apply(1, PadButton.South, true, 112 * T.Ms);
        var before = c.Snapshot();
        c.Reset();
        Assert.Empty(c.Snapshot());
        Assert.Equal(new ChatterCount(2, 1), before[1][PadButton.South]); // 前の写しは変わらない
    }

    [Fact]
    public void ThroughEngine_CountsMappedButtons_ResetClears()
    {
        var e = T.Engine(new FakeClock());
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        e.Apply(new PadAdded(0, 2, T.Xbox(2, "guid-2")));
        e.Apply(new PadAdded(0, 9, T.Unmapped()));
        e.Down(100 * T.Ms, PadButton.South);
        e.Up(110 * T.Ms, PadButton.South);
        e.Down(124 * T.Ms, PadButton.South); // 14 ms
        e.Up(130 * T.Ms, PadButton.South);
        e.Down(145 * T.Ms, PadButton.South); // 15 ms
        e.Down(150 * T.Ms, PadButton.East);
        e.Down(151 * T.Ms, PadButton.South, 2);
        e.Apply(new AxisChanged(160 * T.Ms, 1, PadAxis.LeftTrigger, 32767)); // トリガーは数えない
        e.Apply(new RawButtonChanged(170 * T.Ms, 9, 0, true)); // 割り当ての無いパッドは数えない
        var ch = e.Snapshot(200 * T.Ms, 0).Chatter;
        Assert.Equal(new ChatterCount(3, 1), ch[1][PadButton.South]);
        Assert.Equal(new ChatterCount(1, 0), ch[1][PadButton.East]);
        Assert.Equal(new ChatterCount(1, 0), ch[2][PadButton.South]);
        Assert.Equal(2, ch[1].Count);
        Assert.False(ch.ContainsKey(9));
        e.ResetChatter();
        Assert.Empty(e.Snapshot(300 * T.Ms, 0).Chatter);
    }

    [Theory]
    [InlineData(new[] { 0f, 0.5f, 1f }, 0f, 1f, "問題なし")]
    [InlineData(new[] { 0.05f, 1f }, 0.05f, 1f, "戻りきっていません")]
    [InlineData(new[] { 0f, 0.9f }, 0f, 0.9f, "奥まで届いていません")]
    [InlineData(new[] { 0.1f, 0.5f }, 0.1f, 0.5f, "引き方が足りません")]
    [InlineData(new[] { 0.02f, 0.98f }, 0.02f, 0.98f, "問題なし")] // 境目はちょうどなら問題なし
    [InlineData(new float[0], 0f, 0f, "引き方が足りません")]
    public void TriggerRange_Verdicts(float[] samples, float min, float max, string verdict)
    {
        var r = StickDiagnostics.TriggerRange(samples);
        Assert.Equal(min, r.Min);
        Assert.Equal(max, r.Max);
        Assert.Equal(verdict, r.Verdict);
    }
}
