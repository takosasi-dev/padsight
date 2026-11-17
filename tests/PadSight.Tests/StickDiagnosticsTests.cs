// スティック診断の計算(仕様書 §9.9・AC-4)を、合成の値(中心のずれ、円、楕円、区切りの欠け)で確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class StickDiagnosticsTests
{
    [Fact]
    public void Drift_CentreOffset()
    {
        var samples = new List<StickValue>();
        for (int i = 0; i < 100; i++)
            samples.Add(StickValue.FromRaw((short)(i % 2 == 0 ? 800 : 824), -120)); // 生の値の平均 x=+812、y=−120(SDL は上が負)
        var d = StickDiagnostics.Drift(samples);
        Assert.Equal(812, d.RawMeanX, 6);
        Assert.Equal(-120, d.RawMeanY, 6);
        Assert.Equal(812f / 32767f, d.MeanX, 5);
        Assert.Equal(120f / 32767f, d.MeanY, 5);
        Assert.Equal(MathF.Sqrt(d.MeanX * d.MeanX + d.MeanY * d.MeanY), d.MeanMagnitude, 5);
        Assert.Equal(StickValue.FromRaw(824, -120).Magnitude, d.MaxMagnitude, 6);
    }

    [Fact]
    public void Drift_NoSamples_Zero()
    {
        var d = StickDiagnostics.Drift([]);
        Assert.Equal(0f, d.MeanMagnitude);
    }

    [Fact]
    public void Outer_PerfectCircle_MeanOneDeviationZero()
    {
        var samples = Enumerable.Range(0, 360 * 3).Select(i => T.Stick(i / 3f, 1f)).ToList();
        var r = StickDiagnostics.Outer(samples);
        Assert.False(r.Insufficient);
        Assert.Equal(0, r.EmptyBins);
        Assert.Equal(1f, r.Mean, 4);
        Assert.Equal(1f, r.Min, 4);
        Assert.Equal(1f, r.Max, 4);
        Assert.Equal(0f, r.DeviationPercent, 3);
    }

    [Fact]
    public void Outer_Ellipse_MatchesBinMaxima()
    {
        const float a = 1f, b = 0.8f;
        var samples = new List<StickValue>();
        for (int i = 0; i < 3600; i++)
        {
            float t = i / 10f * MathF.PI / 180f;
            samples.Add(new StickValue(a * MathF.Cos(t), b * MathF.Sin(t), 0, 0));
        }
        var r = StickDiagnostics.Outer(samples);
        Assert.False(r.Insufficient);
        Assert.Equal(0, r.EmptyBins);
        Assert.Equal(0.8f, r.Min, 2);
        Assert.Equal(1f, r.Max, 3);

        // 期待値は同じ点から区切りごとの最大を取り直して出す
        var bins = new float[72];
        foreach (var s in samples)
        {
            int i = (int)(s.AngleDeg / 5f) % 72;
            bins[i] = MathF.Max(bins[i], s.Magnitude);
        }
        Assert.Equal(bins.Average(), r.Mean, 4);
        Assert.Equal(bins.Average(x => MathF.Abs(1 - x)) * 100f, r.DeviationPercent, 3);
        Assert.InRange(r.DeviationPercent, 5f, 15f);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(20, true)]
    public void Outer_MissingBins(int missing, bool insufficient)
    {
        // 区切りの真ん中(2.5 + 5k 度)に 1 点ずつ。最後の missing 個を空ける
        var samples = Enumerable.Range(0, 72 - missing).Select(k => T.Stick(2.5f + 5 * k, 0.95f)).ToList();
        var r = StickDiagnostics.Outer(samples);
        Assert.Equal(missing, r.EmptyBins);
        Assert.Equal(insufficient, r.Insufficient);
        if (insufficient)
            Assert.Equal(0f, r.Mean);
        else
            Assert.Equal(5f, r.DeviationPercent, 3);
    }

    [Fact]
    public void Outer_CentreSamplesIgnored()
    {
        var r = StickDiagnostics.Outer([new StickValue(0, 0, 0, 0)]);
        Assert.Equal(72, r.EmptyBins);
        Assert.True(r.Insufficient);
    }
}
