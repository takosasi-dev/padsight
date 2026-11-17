// トリガーの時系列(TriggerTrace)と放置監視(IdleMonitor)を確かめる(仕様書 FR-57・FR-59・AC-69・AC-71)。
// PadLab の TriggerTrackerTests・IdleMonitorTests の値をそのまま移し、PadSight で足した所(時間で止まる・描く列)も見る。
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.Tests;

public class DiagTriggerIdleTests
{
    const ulong Ms = 1_000_000UL, Sec = 1_000_000_000UL;

    // PadLab の Seq.At と ScriptedPattern の代わり(ほかのテストと名前がぶつからないように、この中に置く)
    static DiagSample At(double ms, uint buttons = 0, float lx = 0, float ly = 0, float lt = 0, float rt = 0) =>
        new((ulong)(ms * Ms), 1, buttons, new StickValue(lx, ly, 0, 0), default, lt, rt);

    static IEnumerable<DiagSample> Idle(double seconds, int hz = 250, ulong startNs = 0)
    {
        ulong step = Sec / (ulong)hz;
        int n = (int)(seconds * hz);
        for (int i = 0; i < n; i++)
            yield return new DiagSample(startNs + (ulong)i * step, 1, 0, default, default, 0, 0);
    }

    static IEnumerable<DiagSample> Drift(double seconds, float dx, float dy, float noise = 0.005f, int hz = 250, ulong startNs = 0, int seed = 1)
    {
        var rng = new Random(seed);
        ulong step = Sec / (ulong)hz;
        int n = (int)(seconds * hz);
        for (int i = 0; i < n; i++)
        {
            var v = new StickValue(dx + (float)(rng.NextDouble() * 2 - 1) * noise, dy + (float)(rng.NextDouble() * 2 - 1) * noise, 0, 0);
            yield return new DiagSample(startNs + (ulong)i * step, 1, 0, v, default, 0, 0);
        }
    }

    static IEnumerable<DiagSample> Taps(PadButton b, int count, double intervalMs, double holdMs = 40, int hz = 250, ulong startNs = 0)
    {
        ulong step = Sec / (ulong)hz;
        ulong total = (ulong)(count * intervalMs * Ms) + step;
        for (ulong t = 0; t < total; t += step)
        {
            double phase = (double)(t / Ms) % intervalMs;
            yield return new DiagSample(startNs + t, 1, phase < holdMs ? DiagSample.Bit(b) : 0, default, default, 0, 0);
        }
    }

    static IdleMonitor Started(IdleMonitorSettings? s = null)
    {
        var m = new IdleMonitor(s);
        m.Start(0, default, default);
        return m;
    }

    static void FeedAll(IdleMonitor m, IEnumerable<DiagSample> seq)
    {
        foreach (var s in seq) m.Feed(s);
    }

    // ---- TriggerTrace(PadLab の TriggerTrackerTests) ----

    [Fact]
    public void Trace_TracksMaxCurrentHistory()
    {
        var t = new TriggerTrace(right: false);
        int i = 0;
        foreach (var v in new[] { 0f, 0.5f, 1.0f, 0.2f }) t.Feed(At(i++ * 4, lt: v));
        Assert.Equal(1.0f, t.Max);
        Assert.Equal(0.2f, t.Current);
        Assert.Equal(4, t.History.Count);
        Assert.Equal(4, t.Count);
        Assert.Equal(0.5f, t.History[1].Value);
        Assert.True(t.History[0].Ns < t.History[3].Ns);
    }

    [Fact]
    public void Trace_Capacity_DropsOldest()
    {
        var t = new TriggerTrace(right: true, capacity: 3);
        for (int i = 0; i < 5; i++) t.Feed(At(i * 4) with { RightTrigger = i * 0.1f });
        Assert.Equal(3, t.History.Count);
        Assert.Equal(0.2f, t.History[0].Value, 3);
        t.Reset();
        Assert.Empty(t.History);
    }

    [Fact]
    public void Trace_SkipsUnchangedValues_AndNeverGoesBackInTime()
    {
        var t = new TriggerTrace(right: false);
        t.Add(10 * Ms, 0.3f);
        t.Add(20 * Ms, 0.3f); // ほかの軸のイベント: 値が同じなら積まない
        t.Add(5 * Ms, 0.6f);  // 前より古い時刻は前の時刻にそろえる
        Assert.Equal(2, t.History.Count);
        Assert.Equal(10 * Ms, t.History[1].Ns);
        Assert.Equal(0.6f, t.Current);
    }

    [Fact]
    public void Trace_Resample_HoldsValueToNow_AndKeepsPeaks()
    {
        var t = new TriggerTrace(right: false);
        t.Add(1 * Sec, 0.2f);
        t.Add(3 * Sec + 10 * Ms, 1.0f);  // 短い山
        t.Add(3 * Sec + 20 * Ms, 0.4f);
        var cols = new float[5];
        t.Resample(5 * Sec, 5 * Sec, cols); // 0〜1, 1〜2, 2〜3, 3〜4, 4〜5 秒
        Assert.True(float.IsNaN(cols[0])); // 最初の値より前
        Assert.Equal(0.2f, cols[1]);
        Assert.Equal(0.2f, cols[2]);
        Assert.Equal(1.0f, cols[3]); // 列の中の最大(山を落とさない)
        Assert.Equal(0.4f, cols[4]); // イベントが無くても今まで持ち越す
        // 窓より前の値も持ち越しの始まりになる
        t.Resample(20 * Sec, 5 * Sec, cols);
        Assert.All(cols, v => Assert.Equal(0.4f, v));
    }

    // ---- IdleMonitor(PadLab の IdleMonitorTests) ----

    [Fact]
    public void Idle_NoEvents()
    {
        var m = Started();
        FeedAll(m, Idle(1));
        var s = m.Summary;
        Assert.True(s.Running);
        Assert.False(s.Finished);
        Assert.Empty(s.Events);
        Assert.InRange(s.ElapsedSeconds, 0.9, 1.1);
    }

    [Fact]
    public void Idle_NotStarted_IgnoresInput()
    {
        var m = new IdleMonitor();
        FeedAll(m, Drift(0.5, 0.2f, 0));
        Assert.False(m.Summary.Running);
        Assert.Empty(m.Summary.Events);
    }

    [Fact]
    public void Idle_Drift_Buttons_Disconnect_AreCounted()
    {
        var m = Started();
        FeedAll(m, Idle(1));
        FeedAll(m, Drift(0.5, 0.08f, 0, startNs: 1 * Sec));
        var s = m.Summary;
        Assert.Equal(1, s.DriftEvents);
        Assert.InRange(s.MaxLeftDeviation, 0.07f, 0.09f);
        Assert.Equal(IdleEventKind.StickDrift, s.Events[0].Kind);
        Assert.Contains("左スティック", s.Events[0].Detail);

        // PadLab の B は PadSight の East(右の面ボタン)
        FeedAll(m, Taps(PadButton.East, 2, 300, startNs: 1_500_000_000).SkipLast(1));
        Assert.Equal(2, m.Summary.ButtonEvents);
        var press = m.Summary.Events.First(e => e.Kind == IdleEventKind.PhantomButton);
        Assert.Equal(PadButton.East, press.Button);
        Assert.Contains("East", press.Detail);

        // 報告の途切れは SDL からは見えないので数えない(PadLab の PollingGap は移さない)
        FeedAll(m, Idle(0.1, startNs: 2_100_000_000));
        FeedAll(m, Idle(0.1, startNs: 2_400_000_000));
        Assert.Equal(3, m.Summary.TotalEvents);

        m.NoteDisconnected(4 * Sec);
        m.NoteReconnected(5 * Sec);
        Assert.Equal(2, m.Summary.DisconnectEvents);
        Assert.Equal(4 * Sec, m.Summary.Events.First(e => e.Kind == IdleEventKind.Disconnected).OffsetNs);
        Assert.Equal(5 * Sec, m.Summary.Events.First(e => e.Kind == IdleEventKind.Reconnected).OffsetNs);
    }

    [Fact]
    public void Idle_TriggerWobble_IsCounted()
    {
        var m = Started();
        m.Feed(At(0));
        m.Feed(At(4, lt: 0.2f));
        m.Feed(At(8, lt: 0.3f));
        Assert.Equal(1, m.Summary.TriggerEvents);
    }

    [Fact]
    public void Idle_Duration_Finishes()
    {
        var m = Started(new IdleMonitorSettings(DurationSeconds: 1));
        FeedAll(m, Idle(1.5));
        Assert.True(m.Summary.Finished);
        Assert.False(m.Summary.Running);
        m.Reset();
        Assert.False(m.Summary.Finished);
    }

    [Fact]
    public void Idle_Stop_EndsRun()
    {
        var m = Started();
        FeedAll(m, Idle(0.5));
        m.Stop();
        Assert.False(m.Summary.Running);
        Assert.True(m.Summary.Finished);
    }

    // ---- PadSight で足した所 ----

    [Fact]
    public void Idle_FinishesByTime_WithoutAnySample()
    {
        // 触っていないパッドからはイベントが来ない。描画のたびの Advance で時間が来たら止まる
        var m = new IdleMonitor(new IdleMonitorSettings(DurationSeconds: 60));
        m.Start(100 * Sec, default, default);
        Assert.True(m.Advance(130 * Sec));
        Assert.Equal(30, m.Summary.ElapsedSeconds, 3);
        Assert.False(m.Advance(161 * Sec));
        Assert.True(m.Summary.Finished);
        Assert.Equal(60, m.Summary.ElapsedSeconds, 3); // 決めた長さで止める
        m.Feed(At(162_000, lt: 1f)); // 止まった後は見ない
        m.NoteDisconnected(163 * Sec);
        Assert.Equal(0, m.Summary.TotalEvents);
    }

    [Fact]
    public void Idle_MeasuresFromStartCenter_AndIgnoresHeldButtonsAndOldSamples()
    {
        var m = new IdleMonitor();
        // 真ん中が (0.1, 0) にずれたまま始める。押しっぱなしの South は勝手な押下と数えない
        m.Start(1 * Sec, new StickValue(0.1f, 0, 0, 0), default, DiagSample.Bit(PadButton.South));
        m.Feed(At(500, lx: 0.9f)); // 始める前のサンプルは見ない
        m.Feed(At(1100, buttons: DiagSample.Bit(PadButton.South), lx: 0.12f));
        Assert.Equal(0, m.Summary.TotalEvents);
        m.Feed(At(1200, lx: 0.2f));
        var e = Assert.Single(m.Summary.Events);
        Assert.Equal(IdleEventKind.StickDrift, e.Kind);
        Assert.Equal(200 * Ms, e.OffsetNs);
        Assert.Equal(0.1f, e.Magnitude, 3);
        // 立ち上がりで 1 件(越えている間は増えない)。戻ってからまた越えたら 1 件
        m.Feed(At(1300, lx: 0.25f));
        m.Feed(At(1400, lx: 0.1f));
        m.Feed(At(1500, lx: 0.3f));
        Assert.Equal(2, m.Summary.DriftEvents);
    }

    [Fact]
    public void Idle_ListIsCapped_ButCountsKeepGoing()
    {
        var m = Started();
        for (int i = 0; i < IdleMonitor.MaxListed + 50; i++)
        {
            m.Feed(At(i * 2, lt: 0.5f));
            m.Feed(At(i * 2 + 1));
        }
        Assert.Equal(IdleMonitor.MaxListed, m.Summary.Events.Count);
        Assert.Equal(IdleMonitor.MaxListed + 50, m.Summary.TriggerEvents);
    }
}
