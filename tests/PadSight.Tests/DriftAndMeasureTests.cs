// ずれの見張り(AC-65)と計測の計算(AC-66)を、250 ミリ秒ごとの合成の値と、作った押下の時刻で確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class DriftAndMeasureTests
{
    const ulong Step = 250 * T.Ms;
    static StickValue V(float x, float y = 0) => new(x, y, 0, 0);

    /// <summary>0 秒から seconds 秒まで 250 ミリ秒ごとに、左に value(右は中心)を流し、出た警告を全部返す。</summary>
    static List<DriftWarning> Run(DriftWatch w, double seconds, Func<ulong, StickValue> left, double dz = 0.10, uint pad = 1, ulong from = 0)
    {
        var all = new List<DriftWarning>();
        for (ulong ns = from; ns <= from + (ulong)(seconds * T.S); ns += Step)
            all.AddRange(w.Feed(ns, pad, "Pad", left(ns), V(0), dz));
        return all;
    }

    [Fact]
    public void Drift_Untouched015For60s_WarnsOnce()
    {
        var w = new DriftWatch();
        var got = Run(w, 60, _ => V(0.15f));
        var d = Assert.Single(got);
        Assert.Equal(1u, d.PadId);
        Assert.False(d.Right);
        Assert.Equal(0.15, d.Mean, 5);
        Assert.Equal(0.10, d.Deadzone);
        Assert.Equal("drift1L", d.Key);
        Assert.Empty(Run(w, 120, _ => V(0.15f), from: 61 * T.S)); // 2 回目は無い
    }

    [Fact]
    public void Drift_JustUnder60s_NoWarningYet()
    {
        Assert.Empty(Run(new DriftWatch(), 59.75, _ => V(0.15f)));
    }

    [Fact]
    public void Drift_Untouched005_NoWarning_AndDecidedOnce()
    {
        var w = new DriftWatch();
        Assert.Empty(Run(w, 60, _ => V(0.05f)));
        // 判定は 1 回なので、後でずれが大きくなっても知らせない
        Assert.Empty(Run(w, 120, _ => V(0.15f), from: 61 * T.S));
    }

    [Fact]
    public void Drift_MovingValues_NotCounted()
    {
        var w = new DriftWatch();
        // 0.15 と 0.18 を交互(動きが 0.02 を超える)。大きさは 0.25 未満でも触っているとみなす
        Assert.Empty(Run(w, 120, ns => V(ns / Step % 2 == 0 ? 0.15f : 0.18f)));
        // 0.25 以上に倒していれば止まっていても数えない
        Assert.Empty(Run(new DriftWatch(), 120, _ => V(0.30f)));
    }

    [Fact]
    public void Drift_MovementResetsOnlyWhileMoving_TimeAccumulates()
    {
        var w = new DriftWatch();
        // 30 秒止める → 1 回だけ動かす → 3 秒は窓に残るので数えない → また止める。合わせて 60 秒で知らせる
        Assert.Empty(Run(w, 30, _ => V(0.15f)));
        Assert.Empty(w.Feed(30 * T.S + Step, 1, "Pad", V(0.5f), V(0), 0.10));
        var got = Run(w, 40, _ => V(0.15f), from: 30 * T.S + 2 * Step);
        Assert.Single(got);
    }

    [Fact]
    public void Drift_LeftAndRightAndPadsSeparate()
    {
        var w = new DriftWatch();
        var all = new List<DriftWarning>();
        for (ulong ns = 0; ns <= 60 * T.S; ns += Step)
        {
            all.AddRange(w.Feed(ns, 1, "A", V(0.15f), V(0, -0.2f), 0.10));
            all.AddRange(w.Feed(ns, 2, "B", V(0.05f), V(0.12f), 0.10));
        }
        Assert.Equal(new[] { "drift1L", "drift1R", "drift2R" }, all.Select(d => d.Key).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(0.2, all.Single(d => d.Key == "drift1R").Mean, 5);
    }

    [Fact]
    public void Drift_PadRemoved_Forgets()
    {
        var w = new DriftWatch();
        Assert.Single(Run(w, 60, _ => V(0.15f)));
        Run(w, 10, _ => V(0.15f), pad: 2);
        Assert.Equal(new[] { 1u }, w.Retain([2u]));
        // 同じ InstanceId でつなぎ直しても、初めから数え直して 1 回知らせる
        Assert.Single(Run(w, 60, _ => V(0.15f), from: 100 * T.S));
        Assert.Empty(w.Retain([1u, 2u]));
    }

    [Fact]
    public void Drift_GapDoesNotCountAsUntouched()
    {
        var w = new DriftWatch();
        // 30 秒の間が空いても、空いた時間は足さない(30 秒 + 29 秒 + 1 秒で 60 秒)
        Assert.Empty(Run(w, 30, _ => V(0.15f)));
        Assert.Empty(Run(w, 29, _ => V(0.15f), from: 60 * T.S));
        Assert.Single(Run(w, 5, _ => V(0.15f), from: 90 * T.S));
    }

    [Fact]
    public void Drift_TimeGoingBack_ClearsWindow()
    {
        var w = new DriftWatch();
        // 先の時刻の動いた値が窓に残ると、後の値がずっと「動いている」に見えてしまう。時刻が戻ったら窓を捨てる
        w.Feed(1000 * T.S, 1, "Pad", V(0.5f), V(0), 0.10);
        Assert.Single(Run(w, 60, _ => V(0.15f)));
    }

    [Fact]
    public void Rapid_EvenPresses()
    {
        // 100 ミリ秒ごとに 10 秒で 100 回(0, 100, …, 9900 ms)
        var t = Enumerable.Range(0, 100).Select(i => (ulong)i * 100 * T.Ms).ToList();
        var r = RapidStats.From(t, 10);
        Assert.Equal(100, r.Count);
        Assert.Equal(10.0, r.PerSecond, 9);
        Assert.Equal(10, r.Max1s); // [t, t+1 秒) なので 1000 ms ちょうどの物は入らない
        Assert.Equal(100.0, r.MeanIntervalMs!.Value, 9);
        Assert.Equal(0.0, r.StdevIntervalMs!.Value, 9);
    }

    [Fact]
    public void Rapid_UnevenPresses_PopulationStdevAndMaxWindow()
    {
        // 間隔 100, 200, 300 ms(平均 200、母集団の標準偏差 sqrt(20000/3))、後で 50 ms ごとに 15 回の固まり
        ulong[] head = [0, 100 * T.Ms, 300 * T.Ms, 600 * T.Ms];
        var burst = Enumerable.Range(0, 15).Select(i => 5 * T.S + (ulong)i * 50 * T.Ms);
        var r1 = RapidStats.From(head, 10);
        Assert.Equal(4, r1.Count);
        Assert.Equal(0.4, r1.PerSecond, 9);
        Assert.Equal(4, r1.Max1s);
        Assert.Equal(200.0, r1.MeanIntervalMs!.Value, 9);
        Assert.Equal(Math.Sqrt(20000.0 / 3), r1.StdevIntervalMs!.Value, 9);

        var r2 = RapidStats.From([.. burst.Reverse(), .. head], 10); // 順番がばらばらでも並べ直す
        Assert.Equal(19, r2.Count);
        Assert.Equal(15, r2.Max1s);
    }

    [Fact]
    public void Rapid_EmptyAndSingle()
    {
        var e = RapidStats.From([], 10);
        Assert.Equal(0, e.Count);
        Assert.Equal(0, e.PerSecond);
        Assert.Equal(0, e.Max1s);
        Assert.Null(e.MeanIntervalMs);
        Assert.Null(e.StdevIntervalMs);
        var one = RapidStats.From([5 * T.S], 10);
        Assert.Equal(1, one.Count);
        Assert.Equal(1, one.Max1s);
        Assert.Null(one.MeanIntervalMs);
    }

    [Fact]
    public void Rapid_Spread_EvenlyUpToTheRealLast()
    {
        Assert.Equal(new[] { 150 * T.Ms }, RapidStats.Spread(100 * T.Ms, 150 * T.Ms, 1).ToArray());
        Assert.Equal(new[] { 200 * T.Ms, 300 * T.Ms, 400 * T.Ms }, RapidStats.Spread(100 * T.Ms, 400 * T.Ms, 3).ToArray());
        Assert.Equal(new[] { 50 * T.Ms, 50 * T.Ms }, RapidStats.Spread(80 * T.Ms, 50 * T.Ms, 2).ToArray()); // 前の方が後なら同じ時刻に置く
        Assert.Empty(RapidStats.Spread(0, 10, 0));
    }

    [Fact]
    public void Reaction_AverageAndBest()
    {
        var r = ReactionStats.From([250, 210.5, 300, 199, 240.5]);
        Assert.NotNull(r);
        Assert.Equal(240.0, r.AverageMs, 9);
        Assert.Equal(199.0, r.BestMs, 9);
        Assert.Null(ReactionStats.From([]));
    }
}
