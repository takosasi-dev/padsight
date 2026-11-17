// スティックの精度(AC-69・FR-56)。PadLab のテストの列と値を DiagSample に移して、スナップバック・分解能・応答カーブ・デッドゾーンのおすすめ値が同じ数を出すかを確かめる。
// あわせて、PadSight で足した物(止まった後の Hold、直線からのずれ、ずれの目安・揺れ、外周の真円度と左右差、記録の形)を確かめる。
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.Tests;

public class DiagStickTests
{
    /// <summary>ms の時刻に左のスティックが (x, y) の DiagSample(PadLab の Seq.At の代わり)。</summary>
    static DiagSample At(double ms, float x, float y = 0) =>
        new((ulong)(ms * Values.NsPerMs), 1, 0, new StickValue(x, y, 0, 0), default, 0, 0);

    /// <summary>1000Hz で (x, 0) を ms の間続ける列(PadLab の SnapbackTestTests.Hold)。</summary>
    static IEnumerable<DiagSample> Hold(ref double t, float x, double ms)
    {
        var list = new List<DiagSample>();
        for (int i = 0; i < ms; i++, t++) list.Add(At(t, x));
        return list;
    }

    static void FeedAll(SnapbackTest s, IEnumerable<DiagSample> seq) { foreach (var x in seq) s.Feed(x); }

    // ---- スナップバック(PadLab の SnapbackTestTests)

    [Fact]
    public void Snapback_FiveCleanReleases_Complete()
    {
        var s = new SnapbackTest(right: false);
        s.Start();
        double t = 0;
        for (int i = 0; i < 5; i++) { FeedAll(s, Hold(ref t, 1f, 300)); FeedAll(s, Hold(ref t, 0f, 300)); }
        var r = s.Result;
        Assert.True(r.Complete);
        Assert.False(s.Running);
        Assert.Equal(5, r.Releases);
        Assert.Equal(5, r.TargetReleases);
        Assert.True(r.MeanReturnMs < 5, $"mean={r.MeanReturnMs}");
        Assert.Equal(5, r.Samples.Count);
        Assert.InRange(r.Samples[0].ReleaseAngleDeg, -1f, 1f);
        Assert.Equal("反対側への振れは小さめです(目安)", r.Verdict);
    }

    [Fact]
    public void Snapback_Overshoot_IsMeasured()
    {
        var s = new SnapbackTest(false, targetReleases: 1);
        s.Start();
        double t = 0;
        FeedAll(s, Hold(ref t, 1f, 300));
        FeedAll(s, Hold(ref t, -0.2f, 20));
        FeedAll(s, Hold(ref t, 0f, 300));
        var r = s.Result;
        Assert.Equal(1, r.Releases);
        Assert.InRange(r.MaxOvershoot, 0.19f, 0.21f);
        Assert.InRange(r.MaxReturnMs, 15, 25);
        Assert.Contains("大きめ", r.Verdict);
    }

    [Fact]
    public void Snapback_Reset_ReturnsToIdle()
    {
        var s = new SnapbackTest(false, 1);
        s.Start();
        double t = 0;
        FeedAll(s, Hold(ref t, 1f, 300)); FeedAll(s, Hold(ref t, 0f, 300));
        s.Reset();
        Assert.False(s.Result.Complete);
        Assert.False(s.Running);
        Assert.Equal(0, s.Result.Releases);
    }

    [Fact]
    public void Snapback_RestingStickSendsNothing_HoldCompletesTheRelease()
    {
        // SDL は値が変わったときしか届かない: 倒して、0 を 1 件だけ送って、その後は何も来ない
        var s = new SnapbackTest(false, 1);
        s.Start();
        double t = 0;
        FeedAll(s, Hold(ref t, 1f, 300));
        s.Feed(At(t, 0f));
        Assert.Equal(0, s.Result.Releases);
        s.Hold((ulong)((t + 49) * Values.NsPerMs));
        Assert.Equal(0, s.Result.Releases);
        s.Hold((ulong)((t + 50) * Values.NsPerMs));
        Assert.Equal(1, s.Result.Releases);
        Assert.True(s.Result.Complete);
        Assert.Equal(0, s.Result.MaxReturnMs, 3);
    }

    [Fact]
    public void Snapback_RightStickAndFinish()
    {
        var s = new SnapbackTest(right: true, targetReleases: 5);
        s.Start();
        // 左を倒しても数えない
        for (int i = 0; i < 300; i++) s.Feed(At(i, 1f));
        for (int i = 300; i < 600; i++) s.Feed(At(i, 0f));
        Assert.Equal(0, s.Result.Releases);
        // 右を上に倒して離す
        for (int i = 600; i < 900; i++) s.Feed(new DiagSample((ulong)i * Values.NsPerMs, 1, 0, default, new StickValue(0, 1, 0, 0), 0, 0));
        for (int i = 900; i < 1000; i++) s.Feed(new DiagSample((ulong)i * Values.NsPerMs, 1, 0, default, new StickValue(0, 0, 0, 0), 0, 0));
        Assert.Equal(1, s.Result.Releases);
        Assert.InRange(s.Result.Samples[0].ReleaseAngleDeg, 89f, 91f);
        Assert.False(s.Result.Complete);
        s.Finish();
        Assert.True(s.Result.Complete);
        Assert.False(s.Running);
    }

    // ---- 分解能(PadLab の ResolutionTestTests)

    [Fact]
    public void Resolution_FineSteps_ReportMinStep()
    {
        var t = new ResolutionTest(false, seconds: 1);
        t.Start();
        // 0.001 刻みで 0→0.5 (500 サンプル、4ms 間隔 = 2s)
        for (int i = 0; i <= 500; i++) t.Feed(At(i * 4, i * 0.001f));
        var r = t.Result;
        Assert.True(r.Complete);
        Assert.InRange(r.MinStep, 0.0009f, 0.0011f);
        Assert.True(r.NoiseLevel < 0.001f, $"noise={r.NoiseLevel}");
        Assert.InRange(r.DistinctLevels, 200, 260);
        Assert.InRange(r.ElapsedSeconds, 0.99, 1.01);
        Assert.InRange(r.StepsEstimate, 1950, 2050);
        Assert.Equal("細かい刻みで値が出ています(目安)", r.Verdict);
    }

    [Fact]
    public void Resolution_BeforeStart_Idle()
    {
        var t = new ResolutionTest(false);
        t.Feed(At(0, 0.5f));
        Assert.False(t.Result.Complete);
        Assert.Equal(0, t.Result.DistinctLevels);
    }

    [Fact]
    public void Resolution_EightBitSteps_Coarse_AndFinishByTime()
    {
        // 8 ビットの値を広げたパッド: 刻みは 257/32767(約 0.0078)
        var t = new ResolutionTest(false, seconds: 5);
        t.Start();
        Assert.False(t.Started);
        const float step = 257f / 32767f;
        for (int i = 0; i < 60; i++) t.Feed(At(i * 10, i * step));
        Assert.True(t.Started);
        Assert.False(t.Result.Complete);
        t.Finish(); // 動かすのをやめて値が届かなくなった
        var r = t.Result;
        Assert.True(r.Complete);
        Assert.InRange(r.MinStep, step - 1e-5f, step + 1e-5f);
        Assert.InRange(r.StepsEstimate, 250, 260);
        Assert.Contains("粗め", r.Verdict);
        Assert.Equal(60, t.Trace.Count);
    }

    [Fact]
    public void Resolution_EightBitWithOtherAxisJitter_StillCoarse()
    {
        // x を 8 ビットの刻みで動かし、y が 1 段だけ行ったり来たりする。|v| の差は小さく出るが、軸の刻みで見る
        var t = new ResolutionTest(false);
        t.Start();
        const float step = 257f / 32767f;
        for (int i = 0; i < 60; i++)
            t.Feed(At(i * 10, 0.3f + (i / 2) * step, i % 2 == 0 ? 0 : step));
        t.Finish();
        var r = t.Result;
        Assert.True(r.MinStep < 0.001f, $"min={r.MinStep}"); // PadLab の |v| の刻みは小さく出る
        Assert.InRange(r.AxisStep, step - 1e-5f, step + 1e-5f);
        Assert.InRange(r.StepsEstimate, 250, 260);
        Assert.Contains("粗め", r.Verdict);
    }

    [Fact]
    public void Resolution_CoarseWinsOverFewLevels()
    {
        // 8 ビットのパッドをゆっくり小さく動かすと、出る値も 12 個ほどしかない。粗さの方を言う
        var t = new ResolutionTest(false);
        t.Start();
        const float step = 257f / 32767f;
        for (int i = 0; i < 40; i++) t.Feed(At(i * 50, (38 + i % 12) * step));
        t.Finish();
        Assert.Equal(12, t.Result.DistinctLevels);
        Assert.Contains("粗め", t.Result.Verdict);
        // 刻みが細かくても値の数が少なければ、動きが足りない
        var u = new ResolutionTest(false);
        u.Start();
        for (int i = 0; i < 40; i++) u.Feed(At(i * 50, 0.3f + (i % 5) * 0.0001f));
        u.Finish();
        Assert.Contains("動きが小さすぎて", u.Result.Verdict);
    }

    [Fact]
    public void Resolution_FinishWithoutAnySample_NotComplete()
    {
        var t = new ResolutionTest(false);
        t.Start();
        t.Finish();
        Assert.False(t.Result.Complete);
    }

    // ---- 応答カーブ(PadLab の LinearityTestTests)

    static void Ramp(LinearityTest t, IEnumerable<float> values)
    {
        int i = 0;
        foreach (var v in values) t.Feed(At(i++ * 4, v));
    }

    [Fact]
    public void Linearity_LinearRamp_NoJumps()
    {
        var t = new LinearityTest(false);
        t.Start();
        Ramp(t, Enumerable.Range(0, 500).Select(i => i / 499f));
        var r = t.Result;
        Assert.True(r.Complete);
        Assert.Equal(0, r.JumpCount);
        Assert.Equal(0, r.ReversalCount);
        Assert.True(r.Curve.Count > 100);
        Assert.InRange(r.Curve[0].X, 0f, 0.001f);
        Assert.InRange(r.Curve[^1].X, 0.999f, 1f);
        Assert.True(r.Curve[^1].Y > 0.95f);
        Assert.InRange(r.MaxDeviation, 0f, 0.002f);
        Assert.Equal("飛びや戻りは見られません(目安)", r.Verdict);
    }

    [Fact]
    public void Linearity_SkippedRange_CountsJump()
    {
        var t = new LinearityTest(false);
        t.Start();
        Ramp(t, Enumerable.Range(0, 500).Select(i => i / 499f).Where(v => v < 0.4f || v > 0.6f));
        var r = t.Result;
        Assert.Equal(1, r.JumpCount);
        Assert.InRange(r.MaxJump, 0.19f, 0.22f);
        Assert.Contains("1 か所", r.Verdict);
    }

    [Fact]
    public void Linearity_Decrease_CountsReversal()
    {
        var t = new LinearityTest(false);
        t.Start();
        Ramp(t, [0f, 0.1f, 0.2f, 0.3f, 0.25f, 0.4f, 0.5f, 1f]);
        Assert.Equal(1, t.Result.ReversalCount);
        Assert.True(t.Result.Complete);
    }

    [Fact]
    public void Linearity_Curved_DeviationFromStraightLine()
    {
        // 時間に対して 2 乗で増える(一定の速さで倒しても値が曲線で出る)。直線(弦)からの最大のずれは (u1 − u0)² / 4
        var t = new LinearityTest(false);
        t.Start();
        Ramp(t, Enumerable.Range(0, 2000).Select(i => MathF.Pow(i / 1999f, 2)));
        var r = t.Result;
        Assert.True(r.Complete);
        float u0 = r.Curve[0].Y, u1 = r.Curve[^1].Y;
        float expected = MathF.Pow(MathF.Sqrt(u1) - MathF.Sqrt(u0), 2) / 4;
        Assert.InRange(r.MaxDeviation, expected - 0.003f, expected + 0.003f);
        Assert.InRange(r.MaxDeviation, 0.13f, 0.15f);
    }

    [Fact]
    public void Linearity_NeverReachesEnd_FinishSaysHowFar()
    {
        var t = new LinearityTest(false);
        t.Start();
        Ramp(t, Enumerable.Range(0, 300).Select(i => i / 299f * 0.9f));
        Assert.False(t.Result.Complete);
        t.Finish();
        Assert.True(t.Result.Complete);
        Assert.Contains("0.90 までしか届きませんでした", t.Result.Verdict);
    }

    // ---- デッドゾーンのおすすめ値(PadLab の DeadzoneAdvisorTests)

    [Theory]
    [InlineData(0.06f, 0.005f, null, 9)]
    [InlineData(0.0f, 0.0f, null, 3)]
    [InlineData(0.5f, 0.0f, null, 30)]
    [InlineData(0.02f, 0.0f, 0.05f, 7)]
    public void Deadzone_Percent(float maxDrift, float noise, float? idle, int expected)
    {
        var a = DeadzoneAdvisor.Compute(maxDrift, noise, idle);
        Assert.Equal(expected, a.RecommendedPercent);
        Assert.Contains($"{expected}%", a.Rationale);
        Assert.Equal(3, a.Hints.Count);
        Assert.Contains(a.Hints, h => h.Use == "FPS");
    }

    // ---- 前からのスティック診断に足した物

    [Theory]
    [InlineData(0.02f, 0)]
    [InlineData(0.03f, 1)]
    [InlineData(0.079f, 1)]
    [InlineData(0.08f, 2)]
    public void Drift_Tier_UsesPadLabThresholds(float max, int tier)
    {
        // 止まった位置 (0.05, 0.02) から ±max 動く。止まった位置そのもののずれは目安に入れない(PadLab の基準点からの距離と同じ)
        var d = StickDiagnostics.Drift([new StickValue(0.05f - max, 0.02f, 0, 0), new StickValue(0.05f + max, 0.02f, 0, 0)]);
        Assert.Equal(max, d.MaxFromMean, 5);
        Assert.Equal(tier, d.Tier);
        Assert.Contains(tier == 0 ? "小さい範囲" : "可能性があります", d.Verdict);
    }

    [Fact]
    public void Drift_RestOffsetAloneIsNotFlagged()
    {
        var d = StickDiagnostics.Drift([.. Enumerable.Repeat(new StickValue(0.06f, 0, 0, 0), 50)]);
        Assert.Equal(0.06f, d.MaxMagnitude, 5);
        Assert.Equal(0, d.Tier);
    }

    [Fact]
    public void Drift_NoiseAndDirection()
    {
        // 平均の位置 (0, 0.05) の周りを、半径 0.01 で回る → 平均の位置からの距離は全部 0.01 で、揺れ(標準偏差)は 0
        var circle = Enumerable.Range(0, 360).Select(i => new StickValue(0.01f * MathF.Cos(i * MathF.PI / 180), 0.05f + 0.01f * MathF.Sin(i * MathF.PI / 180), 0, 0)).ToList();
        var d = StickDiagnostics.Drift(circle);
        Assert.Equal(0f, d.Noise, 4);
        Assert.Equal(90f, d.DirectionDeg, 1);
        // 0・0.04・0.01 → 平均 0.0167 からの距離 0.0167・0.0233・0.0067 の標準偏差
        var alt = new[] { 0f, 0.04f, 0.01f }.Select(x => new StickValue(x, 0, 0, 0)).ToList();
        var dists = new[] { 50f / 3000f, 70f / 3000f, 20f / 3000f };
        float mean = dists.Average();
        float sd = MathF.Sqrt(dists.Average(x => (x - mean) * (x - mean)));
        Assert.Equal(sd, StickDiagnostics.Drift(alt).Noise, 4);
    }

    [Fact]
    public void Outer_RoundnessAndAsymmetry()
    {
        // 右が 1.0、左が 0.8 の卵形(上下は 0.9)
        var samples = new List<StickValue>();
        for (int i = 0; i < 3600; i++)
        {
            float a = i / 10f * MathF.PI / 180f, c = MathF.Cos(a), s = MathF.Sin(a);
            float rx = c >= 0 ? 1f : 0.8f;
            samples.Add(new StickValue(rx * c, 0.9f * s, 0, 0));
        }
        var r = StickDiagnostics.Outer(samples);
        Assert.False(r.Insufficient);
        Assert.Equal(r.Min / r.Max, r.Roundness, 5);
        Assert.InRange(r.AsymmetryX, 0.2f / r.Mean - 0.01f, 0.2f / r.Mean + 0.01f);
        Assert.InRange(r.AsymmetryY, -0.01f, 0.01f);
        // 真円は 1 と 0
        var circle = StickDiagnostics.Outer([.. Enumerable.Range(0, 360 * 3).Select(i => T.Stick(i / 3f, 1f))]);
        Assert.Equal(1f, circle.Roundness, 4);
        Assert.Equal(0f, circle.AsymmetryX, 4);
        Assert.Equal(0f, circle.AsymmetryY, 4);
    }

    [Fact]
    public void Outer_Shape_EmptyBinsAreZero_AndRecordRoundTrips()
    {
        // 最後の 5 つの区切りを空ける(足りている)
        var samples = Enumerable.Range(0, 67).Select(k => T.Stick(2.5f + 5 * k, 0.95f)).ToList();
        var o = StickDiagnostics.Outer(samples);
        Assert.False(o.Insufficient);
        Assert.Equal(72, o.Shape.Count);
        Assert.Equal(5, o.Shape.Count(v => v == 0));
        Assert.All(o.Shape, v => Assert.InRange(v, 0, 1));
        var rec = new DiagRecord("stick_outer", "外周の形(左)", new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc), "DualSense", "PlayStation",
            [new("平均", o.Mean)], "", o.Shape, "0.1.0");
        var back = DiagRecord.Parse(rec.ToJson());
        Assert.NotNull(back);
        Assert.Equal(o.Shape, back!.Shape);
    }
}
