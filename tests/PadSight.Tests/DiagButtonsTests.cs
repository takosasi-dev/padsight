// 診断の「ボタン」のページの計算(仕様書 FR-55・FR-45・AC-68)を確かめる。PadLab の ButtonCoverageTests・PaddleMapperTests・ChatterDetectorTests の値を移した。
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.Tests;

public class DiagButtonsTests
{
    // ---- 合成の DiagSample の列(PadLab の Seq・ScriptedPattern と同じ形。ほかの担当と名前がぶつからないよう、このクラスの中だけに置く) ----

    const ulong Ms = 1_000_000;

    static DiagSample At(double ms, params PadButton[] pressed) => At(ms, 0, 0, pressed);

    static DiagSample At(double ms, float lt, float rt, params PadButton[] pressed) =>
        new((ulong)(ms * Ms), 1, DiagSample.Mask(pressed), default, default, lt, rt);

    static IEnumerable<DiagSample> Idle(double seconds, int hz = 250, ulong startNs = 0)
    {
        ulong step = 1_000_000_000UL / (ulong)hz;
        int n = (int)(seconds * hz);
        for (int i = 0; i < n; i++)
            yield return new DiagSample(startNs + (ulong)i * step, 1, 0, default, default, 0, 0);
    }

    /// <summary>押す→離すを intervalMs ごとに count 回(holdMs 押す)。PadLab と同じく末尾に次の押下の 1 件が付く。</summary>
    static IEnumerable<DiagSample> Taps(PadButton b, int count, double intervalMs, double holdMs = 40, int hz = 250)
    {
        long step = 1_000_000_000L / hz;
        long total = (long)(count * intervalMs * Ms) + step;
        for (long t = 0; t < total; t += step)
        {
            bool down = (t / (long)Ms) % intervalMs < holdMs;
            yield return new DiagSample((ulong)t, 1, down ? DiagSample.Bit(b) : 0, default, default, 0, 0);
        }
    }

    /// <summary>30ms 押す → gapMs 離す → 30ms 押す → 100ms 離す を spacingMs ごとに count 回(チャタリング)。</summary>
    static IEnumerable<DiagSample> Chatter(PadButton b, int count, double gapMs = 4, double spacingMs = 500, int hz = 1000)
    {
        long step = 1_000_000_000L / hz;
        for (int i = 0; i < count; i++)
        {
            long baseNs = (long)(i * spacingMs * Ms), t = 0;
            foreach (var (down, ms) in new[] { (true, 30.0), (false, gapMs), (true, 30.0), (false, 100.0) })
            {
                long end = (long)(ms * Ms);
                for (long u = 0; u < end; u += step)
                    yield return new DiagSample((ulong)(baseNs + t + u), 1, down ? DiagSample.Bit(b) : 0, default, default, 0, 0);
                t += end;
            }
        }
    }

    static readonly PadProfile Two = new("test", "test", [new("South", "A"), new("East", "B")], 0);

    static void Feed(ButtonCoverage c, params DiagSample[] s) { foreach (var x in s) c.Feed(x); }

    static void Feed(PaddleMapper p, IEnumerable<DiagSample> s) { foreach (var x in s) p.Feed(x); }

    // ---- ボタンの網羅(PadLab の ButtonCoverageTests。押しただけでなく離したところで印が付く) ----

    [Fact]
    public void Coverage_PressingAndReleasingAllExpected_SetsDone()
    {
        var c = new ButtonCoverage(Two, 0.5f);
        Feed(c, At(0, PadButton.South));
        Assert.Equal(CoverageMark.Held, c.State.Items[0].Mark); // 押しただけでは済みにしない
        Assert.Equal(2, c.State.Remaining);
        Feed(c, At(4));
        Assert.Equal(CoverageMark.Done, c.State.Items[0].Mark);
        Assert.False(c.State.AllDone);
        Assert.Equal(1, c.State.Remaining);
        Feed(c, At(8, PadButton.East), At(12));
        Assert.True(c.State.AllDone);
        Assert.Equal(0, c.State.Remaining);
        Assert.Equal(2, c.State.Done);
        Assert.Equal(2, c.State.Items.Count);
    }

    [Fact]
    public void Coverage_ExtraButton_AddsUnregisteredOnce()
    {
        var c = new ButtonCoverage(Two, 0.5f);
        Feed(c, At(0, PadButton.Touchpad), At(4), At(8, PadButton.Touchpad), At(12));
        var st = c.State;
        var extra = Assert.Single(st.Items, i => i.Unregistered);
        Assert.Equal("Touchpad", extra.Key);
        Assert.True(extra.Extended);
        Assert.Equal(CoverageMark.Done, extra.Mark);
        Assert.Equal(3, st.Items.Count);
        Assert.Equal(2, st.Remaining); // 未登録の物は完了の条件に入らない
    }

    [Fact]
    public void Coverage_UnknownButton_AddsToExpected_AndResetRemovesIt()
    {
        var c = new ButtonCoverage(Two, 0.5f);
        Feed(c, At(0, PadButton.West));
        Assert.Contains(c.State.Items, i => i.Key == "West" && i.Mark == CoverageMark.Held);
        c.Reset();
        Assert.Equal(2, c.State.Items.Count);
        Assert.All(c.State.Items, i => Assert.Equal(CoverageMark.NotYet, i.Mark));
    }

    [Fact]
    public void Coverage_ExtendedButtonIsCounted_ButNotNeededForAllDone()
    {
        var p = new PadProfile("t", "t", [new("South", "A"), new("Misc1", "Share", true)], 0);
        var c = new ButtonCoverage(p, 0.5f);
        Feed(c, At(0, PadButton.South), At(4));
        Assert.True(c.State.AllDone);
        Assert.Equal(1, c.State.Done);
        Assert.Equal(2, c.State.Items.Count);
        Feed(c, At(8, PadButton.Misc1), At(12));
        Assert.Equal(2, c.State.Done);
    }

    [Fact]
    public void Coverage_TriggerCountsAtThreshold_AndNeedsRelease()
    {
        var p = new PadProfile("t", "t", [new("LeftTrigger", "LT"), new("RightTrigger", "RT")], 0);
        var c = new ButtonCoverage(p, 0.5f);
        Feed(c, At(0, 0.49f, 0f));
        Assert.Equal(CoverageMark.NotYet, c.State.Items[0].Mark);
        Feed(c, At(4, 0.5f, 0f)); // しきい値ちょうどは押した(PadStateMachine と同じ >=)
        Assert.Equal(CoverageMark.Held, c.State.Items[0].Mark);
        Feed(c, At(8, 0.2f, 1f), At(12, 0f, 0f));
        Assert.True(c.State.AllDone);
    }

    [Fact]
    public void Coverage_ProfilePaddlesAreLeftToPaddleCheck_OthersPaddlesAreUnregistered()
    {
        var elite = new ButtonCoverage(PadProfiles.XboxElite2, 0.5f);
        Feed(elite, At(0, PadButton.Paddle1, PadButton.Paddle4), At(4));
        Assert.DoesNotContain(elite.State.Items, i => i.Unregistered);
        var edge = new ButtonCoverage(PadProfiles.DualSenseEdge, 0.5f); // パドルは 2 本
        Feed(edge, At(0, PadButton.Paddle2, PadButton.Paddle3), At(4));
        Assert.Equal(["Paddle3"], edge.State.Items.Where(i => i.Unregistered).Select(i => i.Key));
    }

    [Fact]
    public void Coverage_AllProfilesHaveKnownKeys_AndCanFinish()
    {
        foreach (var p in PadProfiles.All)
        {
            var c = new ButtonCoverage(p, 0.5f);
            var buttons = p.Buttons.Where(b => !b.Key.EndsWith("Trigger", StringComparison.Ordinal)).Select(b => Enum.Parse<PadButton>(b.Key)).ToArray();
            Feed(c, At(0, 1f, 1f, buttons), At(4));
            Assert.True(c.State.AllDone, p.Id);
            Assert.Equal(p.Buttons.Count, c.State.Done);
        }
    }

    // ---- パドルの確認(PadLab の PaddleMapperTests) ----

    [Fact]
    public void Paddle_FullFlow()
    {
        var p = new PaddleMapper(paddleCount: 3);
        Assert.Equal(PaddlePhase.Idle, p.State.Phase);
        p.Begin();
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        Assert.Equal(1, p.State.CurrentPaddle);

        // P1: A を押す → 100ms の窓が閉じて Captured
        Feed(p, Taps(PadButton.South, 1, 300).SkipLast(1));
        Assert.Equal(PaddlePhase.Captured, p.State.Phase);
        Assert.Equal([PadButton.South], p.State.Mappings[0].Mapped);
        Assert.False(p.State.Mappings[0].PhysicalSeen);

        // P2: B と X を 50ms の内に
        p.Next();
        Assert.Equal(2, p.State.CurrentPaddle);
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        p.Feed(At(1000));
        p.Feed(At(1010, PadButton.East));
        p.Feed(At(1050, PadButton.East, PadButton.West));
        p.Feed(At(1200));
        Assert.Equal(PaddlePhase.Captured, p.State.Phase);
        Assert.Equal([PadButton.East, PadButton.West], p.State.Mappings[1].Mapped);

        // P3: 反応なし
        p.Next();
        Assert.Equal(3, p.State.CurrentPaddle);
        Feed(p, Idle(11, startNs: 2_000_000_000));
        Assert.Equal(PaddlePhase.NoResponse, p.State.Phase);
        Assert.True(p.State.WaitedSeconds >= 10);
        Assert.Empty(p.State.Mappings[2].Mapped);

        // やり直し → SDL のパドル(Paddle3)と A
        p.Retry();
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        Assert.Equal(2, p.State.Mappings.Count);
        p.Feed(At(20_000, PadButton.Paddle3, PadButton.South));
        p.Feed(At(20_200));
        Assert.Equal(PaddlePhase.Captured, p.State.Phase);
        Assert.True(p.State.Mappings[2].PhysicalSeen);
        Assert.Equal([PadButton.South], p.State.Mappings[2].Mapped);
        Assert.Equal([PadButton.South, PadButton.Paddle3], p.State.Mappings[2].Arrived); // 届いた物は全部(ビットの順)

        p.Next();
        Assert.Equal(PaddlePhase.Done, p.State.Phase);
        Assert.Equal(3, p.State.Mappings.Count);
    }

    [Fact]
    public void Paddle_SameButtonTwice_IsInvalid()
    {
        var p = new PaddleMapper(1);
        p.Begin();
        p.Feed(At(0, PadButton.South));
        p.Feed(At(10));
        p.Feed(At(20, PadButton.South));
        Assert.Equal(PaddlePhase.Invalid, p.State.Phase);
        p.Retry();
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
    }

    [Fact]
    public void Paddle_ThreeStandardButtons_IsInvalid()
    {
        var p = new PaddleMapper(1);
        p.Begin();
        p.Feed(At(0, PadButton.South, PadButton.East, PadButton.West));
        Assert.Equal(PaddlePhase.Invalid, p.State.Phase);
        p.Reset();
        Assert.Equal(PaddlePhase.Idle, p.State.Phase);
    }

    [Fact]
    public void Paddle_TickClosesWindowAndTimesOut_WithoutNewSamples()
    {
        // DiagSample はイベントのときだけ来る。押しっぱなしでも、何も押さなくても、Tick で進む
        var p = new PaddleMapper(2);
        p.Begin();
        p.Feed(At(0, PadButton.Paddle1));
        p.Tick(50 * Ms);
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        p.Tick(101 * Ms);
        Assert.Equal(PaddlePhase.Captured, p.State.Phase);
        Assert.Equal([PadButton.Paddle1], p.State.Mappings[0].Arrived);
        Assert.Empty(p.State.Mappings[0].Mapped);

        p.Next();
        p.Tick(200 * Ms);
        p.Tick(100 * Ms); // 時刻が戻っても回らない
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        p.Tick(10_199 * Ms);
        Assert.Equal(PaddlePhase.Waiting, p.State.Phase);
        p.Tick(10_200 * Ms);
        Assert.Equal(PaddlePhase.NoResponse, p.State.Phase);
        Assert.Equal(10, p.State.WaitedSeconds, 3);
    }

    [Fact]
    public void Paddle_HeldButtonAtBegin_CountsAsNewPress()
    {
        var p = new PaddleMapper(1);
        p.Begin();
        p.Feed(At(0, PadButton.Paddle2));
        p.Tick(150 * Ms);
        Assert.Equal(PaddlePhase.Captured, p.State.Phase);
        p.Retry();
        p.Feed(At(200, PadButton.Paddle2)); // 押したままでも、やり直しの後は新しい押下として受ける
        p.Tick(400 * Ms);
        Assert.Equal([PadButton.Paddle2], Assert.Single(p.State.Mappings).Arrived);
    }

    // ---- ボタンの検査(PadLab の ChatterDetectorTests の列を PadSight の ChatterCounter に流す) ----

    static ChatterCounter Count(IEnumerable<DiagSample> samples)
    {
        var c = new ChatterCounter();
        uint prev = 0;
        foreach (var s in samples)
        {
            uint changed = s.Buttons ^ prev;
            for (int bit = 0; bit < 32; bit++)
                if ((changed & (1u << bit)) != 0)
                    c.Apply(s.PadId, (PadButton)bit, (s.Buttons & (1u << bit)) != 0, s.Ns);
            prev = s.Buttons;
        }
        return c;
    }

    [Fact]
    public void Chatter_PadLabChatterPattern_GivesSameCounts()
    {
        var r = Count(Chatter(PadButton.South, 5, gapMs: 4, hz: 1000)).Snapshot()[1][PadButton.South];
        Assert.Equal(10, r.Presses);
        Assert.Equal(5, r.Short);
    }

    [Fact]
    public void Chatter_PadLabHumanTapping_IsNotCounted()
    {
        var r = Count(Taps(PadButton.South, 50, intervalMs: 60, holdMs: 30, hz: 1000).SkipLast(1)).Snapshot()[1][PadButton.South];
        Assert.Equal(50, r.Presses);
        Assert.Equal(0, r.Short);
    }
}
