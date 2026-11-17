// 入力履歴の行の作り方とフレーム数の文字列(仕様書 §9.7・AC-1・AC-16・E-21)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class HistoryBuilderTests
{
    [Fact]
    public void Update_SyntheticSequence_BuildsRowsNewestFirst()
    {
        var h = new HistoryBuilder();
        Assert.True(h.Update(0, 2, []));
        Assert.True(h.Update(100 * T.Ms, 3, []));
        Assert.True(h.Update(200 * T.Ms, 6, []));
        Assert.True(h.Update(300 * T.Ms, 6, ["South"]));

        var r = h.Rows;
        Assert.Equal([6, 6, 3, 2], r.Select(x => x.Direction));
        Assert.Equal(["South"], r[0].NewlyPressed);
        Assert.Empty(r[1].NewlyPressed);
        Assert.Equal(300 * T.Ms, r[0].StartNs);
        Assert.Null(r[0].EndNs);
        Assert.Equal(300 * T.Ms, r[1].EndNs);
        Assert.Equal(200 * T.Ms, r[2].EndNs);
        Assert.Equal(100 * T.Ms, r[3].EndNs);
    }

    [Fact]
    public void Update_ReleaseOnly_AddsNoRow()
    {
        var h = new HistoryBuilder();
        h.Update(0, 5, ["South"]);
        h.Update(10 * T.Ms, 5, ["East", "South"]);
        Assert.False(h.Update(20 * T.Ms, 5, ["South"]));
        Assert.False(h.Update(30 * T.Ms, 5, []));
        Assert.Equal(2, h.Rows.Count);
        Assert.Null(h.Rows[0].EndNs);
    }

    [Fact]
    public void Update_PressWhileHolding_SplitsNewlyAndHeld()
    {
        var h = new HistoryBuilder();
        h.Update(0, 5, ["South"]);
        h.Update(10 * T.Ms, 5, ["East", "South"]);
        Assert.Equal(["East"], h.Rows[0].NewlyPressed);
        Assert.Equal(["South"], h.Rows[0].Held);
    }

    [Fact]
    public void Update_DirectionBackToNeutral_AddsRow()
    {
        var h = new HistoryBuilder();
        h.Update(0, 6, []);
        Assert.True(h.Update(50 * T.Ms, 5, []));
        Assert.Equal(5, h.Rows[0].Direction);
    }

    [Fact]
    public void Update_SameButtonPressedAgainAfterRelease_AddsRow()
    {
        var h = new HistoryBuilder();
        h.Update(0, 5, ["South"]);
        h.Update(10 * T.Ms, 5, []);
        Assert.True(h.Update(20 * T.Ms, 5, ["South"]));
        Assert.Equal(2, h.Rows.Count);
    }

    [Fact]
    public void Update_MoreThan200Rows_KeepsNewest200()
    {
        var h = new HistoryBuilder();
        for (int i = 0; i < 250; i++)
            h.Update((ulong)i * T.Ms, i % 2 == 0 ? 6 : 4, []);
        Assert.Equal(HistoryBuilder.Capacity, h.Rows.Count);
        Assert.Equal(249 * T.Ms, h.Rows[0].StartNs);
        Assert.Equal(50 * T.Ms, h.Rows[^1].StartNs);
    }

    [Fact]
    public void Update_1800AlternatingPresses_CreatesRowEachTime()
    {
        var h = new HistoryBuilder();
        int created = 0;
        for (int i = 0; i < 1800; i++)
        {
            ulong t = (ulong)i * 33_333_333UL;
            if (h.Update(t, 5, [i % 2 == 0 ? "South" : "East"])) created++;
            h.Update(t + 10 * T.Ms, 5, []);
        }
        Assert.Equal(1800, created);
        Assert.Equal(200, h.Rows.Count);
    }

    [Fact]
    public void Clear_RemovesRowsButKeepsState()
    {
        var h = new HistoryBuilder();
        h.Update(0, 6, ["South"]);
        h.Clear();
        Assert.Empty(h.Rows);
        // 今の方向と押している物は覚えたまま: 同じ状態では行が増えない
        Assert.False(h.Update(10 * T.Ms, 6, ["South"]));
        Assert.True(h.Update(20 * T.Ms, 5, ["South"]));
        Assert.Single(h.Rows);
    }

    [Theory]
    [InlineData(1_000_000_000UL, 60, "60")]
    [InlineData(1_000_000_000UL, 120, "120")]
    [InlineData(1_000_000_000UL, 144, "144")]
    [InlineData(1_000_000_000UL, 240, "240")]
    [InlineData(1_000_000_000UL, 0, "1000")]
    [InlineData(16_666_667UL, 60, "1")]
    [InlineData(100_000_000UL, 144, "14")]   // 14.4
    [InlineData(25_000_000UL, 60, "2")]      // 1.5 は切り上げ
    [InlineData(24_999_999UL, 60, "1")]
    [InlineData(12_500_000UL, 120, "2")]
    [InlineData(12_499_999UL, 120, "1")]
    [InlineData(31_250_000UL, 144, "5")]     // 4.5
    [InlineData(31_249_999UL, 144, "4")]
    [InlineData(6_250_000UL, 240, "2")]
    [InlineData(6_249_999UL, 240, "1")]
    [InlineData(500_000UL, 0, "1")]          // 0.5 ms
    [InlineData(499_999UL, 0, "0")]
    [InlineData(1_500_000UL, 0, "2")]
    [InlineData(0UL, 60, "0")]
    public void FormatLength_Bases_RoundHalfUp(ulong ns, int frameBase, string expected) =>
        Assert.Equal(expected, HistoryBuilder.FormatLength(ns, frameBase));

    [Theory]
    [InlineData(16_650_000_000UL, 60, "999")]    // 999 フレーム
    [InlineData(16_675_000_000UL, 60, "999+")]   // 1000.5 → 1001
    [InlineData(16_666_666_667UL, 60, "999+")]   // 1000
    [InlineData(99_999_000_000UL, 0, "99999")]
    [InlineData(100_000_000_000UL, 0, "99999+")]
    [InlineData(99_999_500_000UL, 0, "99999+")]  // 99999.5 → 100000
    public void FormatLength_Overflow_ShowsPlus(ulong ns, int frameBase, string expected) =>
        Assert.Equal(expected, HistoryBuilder.FormatLength(ns, frameBase));
}
