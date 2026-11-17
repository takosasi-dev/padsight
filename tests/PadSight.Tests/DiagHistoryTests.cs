// 履歴・レポートの担当(B4)の確かめ。報告の間隔の参考値は PadLab の PollingRateMeterTests と同じ入力で同じ数が出ること(AC-69)、
// DiagSample がイベントごとにしか来ないことへの手当て、履歴が受ける記録の端の形(空の値・空の形・知らない種類)を見る。
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.Tests;

public class DiagHistoryTests
{
    /// <summary>PadLab の ScriptedPattern.Idle と同じ時刻の列(1 秒に hz 件、間は 1e9 / hz ns)。</summary>
    static void Idle(PollingRate m, double seconds, int hz, ulong startNs = 0)
    {
        ulong step = 1_000_000_000UL / (ulong)hz;
        int n = (int)(seconds * hz);
        for (int i = 0; i < n; i++)
            m.Feed(startNs + (ulong)i * step);
    }

    [Fact]
    public void Idle250Hz_Reports250()
    {
        var m = new PollingRate();
        Idle(m, 2, 250);
        var s = m.Stats;
        Assert.InRange(s.Hz, 240, 260);
        Assert.True(s.JitterMs < 0.01, $"Jitter={s.JitterMs}");
        Assert.InRange(s.MeanIntervalMs, 3.9, 4.1);
        Assert.True(s.Ready);
    }

    [Fact]
    public void Idle60Hz_Reports60()
    {
        var m = new PollingRate();
        Idle(m, 1, 60);
        Assert.InRange(m.Stats.Hz, 55, 65);
    }

    [Fact]
    public void Reset_ClearsSamples()
    {
        var m = new PollingRate();
        Idle(m, 1, 60);
        m.Reset();
        Assert.Equal(0, m.Stats.Samples);
        Assert.Equal(0, m.Stats.Hz);
        Assert.False(m.Stats.Ready);
    }

    [Fact]
    public void KeepsOnlyTheLastSecond()
    {
        var m = new PollingRate();
        Idle(m, 3, 125);
        Assert.InRange(m.Stats.Samples, 125, 126);
        Assert.Equal(8.0, m.Stats.MedianIntervalMs, 6);
    }

    [Fact]
    public void EventsOfTheSameReportCountOnce()
    {
        // 1 つの報告で X と Y と押下が変わると、同じ時刻(か数 µs 違い)の DiagSample が 3 件来る
        var m = new PollingRate();
        for (ulong i = 0; i < 250; i++)
        {
            m.Feed(i * 4_000_000);
            m.Feed(i * 4_000_000);
            m.Feed(i * 4_000_000 + 3_000);
        }
        Assert.InRange(m.Stats.Hz, 249.9, 250.1);
        Assert.Equal(250, m.Stats.Samples);
    }

    [Fact]
    public void PausesInTheMovementDoNotLowerTheRate()
    {
        // スティックを止めた間はイベントが来ない。中央値なので 1000 Hz のままで、平均と最大には間が出る
        var m = new PollingRate();
        Idle(m, 0.2, 1000);
        Idle(m, 0.2, 1000, startNs: 600_000_000);
        var s = m.Stats;
        Assert.InRange(s.Hz, 999, 1001);
        Assert.True(s.MaxIntervalMs > 400, $"Max={s.MaxIntervalMs}");
        Assert.True(s.MeanIntervalMs > 1.5, $"Mean={s.MeanIntervalMs}");
    }

    [Fact]
    public void AFewPressesAreNotAReading()
    {
        var m = new PollingRate();
        foreach (ulong ms in new ulong[] { 0, 90, 180, 300, 420 })
            m.Feed(ms * 1_000_000);
        Assert.False(m.Stats.Ready);
        Assert.Equal(0, new PollingRate().Stats.Hz);
    }

    [Fact]
    public void TimeGoingBackStartsOver()
    {
        var m = new PollingRate();
        Idle(m, 1, 500, startNs: 5_000_000_000);
        Idle(m, 0.1, 250); // デモに切り替わって時刻が小さくなった
        Assert.InRange(m.Stats.Hz, 249, 251);
        Assert.Equal(25, m.Stats.Samples);
    }

    [Fact]
    public void RecordsTheHistoryShowsSurviveTheRoundTrip()
    {
        // 値も形も無い記録・知らない種類・長い名前も、そのまま読み戻せる(履歴は種類を知らずに出す)
        var at = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var bare = new DiagRecord("some_future_kind", "新しいテスト", at, "", "", [], "", [], "0.1.0");
        var back = DiagRecord.Parse(bare.ToJson())!;
        Assert.Equal("some_future_kind", back.Kind);
        Assert.Empty(back.Values);
        Assert.Empty(back.Shape);
        Assert.Equal("", back.Verdict);

        var shape = Enumerable.Range(0, 72).Select(i => i % 9 == 0 ? 0 : 0.95 + i % 5 * 0.01).ToList();
        var outer = new DiagRecord("stick_outer", "外周(左)", at, new string('長', 120), "PlayStation",
            [new("平均", 0.97), new("1.00 からのずれの平均(%)", 2.1)], "目安です", shape, "0.1.0");
        var o = DiagRecord.Parse(outer.ToJson())!;
        Assert.Equal(shape, o.Shape);
        Assert.Equal(120, o.PadName.Length);
    }
}
