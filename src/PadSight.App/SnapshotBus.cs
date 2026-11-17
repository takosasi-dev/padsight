// SDL のスレッドが出す不変のスナップショットを、画面へ受け渡す(仕様書 §2・D-26)。
// 置くのは最新の 1 つだけで、読み手(パネル 4 枚、ホームの小さい表示、診断、見本)はそれぞれ版を見て描き直す。
namespace PadSight.App;

using PadSight.Core;

public sealed class SnapshotBus
{
    PadSnapshot _latest = PadSnapshot.Empty;

    public PadSnapshot Latest => Volatile.Read(ref _latest);

    public void Publish(PadSnapshot s) => Volatile.Write(ref _latest, s);

    /// <summary>UI のスレッドで、今の SDL の時刻を出す(§9.7)。UI のスレッドから SDL を呼ばない。</summary>
    public static ulong NowNs(PadSnapshot s)
    {
        if (s.StopwatchAtPublish == 0)
            return s.SdlNsAtPublish;
        long delta = System.Diagnostics.Stopwatch.GetTimestamp() - s.StopwatchAtPublish;
        return s.SdlNsAtPublish + (ulong)Math.Max(0, (double)delta * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency);
    }
}
