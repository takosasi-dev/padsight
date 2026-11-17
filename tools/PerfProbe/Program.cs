// PerfProbe: パッド 2 台・左スティックを回し続ける(軸 250 回/秒)・ボタン 10 回/秒を 60 秒分、InputEngine に流して測る。
// SdlInputThread と同じく 4 ミリ秒ごとに Snapshot、250 ミリ秒ごとに Tick を呼ぶ。時間は作った時刻で、実時間は待たない。
// 実行: dotnet run -c Release --project tools\PerfProbe (結果は標準出力だけ。digest は直す前後で同じになるはず)
using System.Diagnostics;
using PadSight.Core;

const int Seconds = 60;
const int Repeats = 5;
const ulong Ms = Values.NsPerMs;

Run(spin: true, digest: true); // 慣らし(JIT)
foreach (bool spin in new[] { true, false })
{
    // 測る回は digest を作らない(digest の割り当てで GC の回数が変わるため)。digest は別の 1 回で作る
    var runs = Enumerable.Range(0, Repeats).Select(_ => Run(spin, digest: false)).ToList();
    var r = runs[0];
    long dg = Run(spin, digest: true).Digest;
    if (runs.Any(x => x.SnapBytes != r.SnapBytes))
        Console.WriteLine("  (warning: runs differ)");
    double Median(Func<Result, double> f) => runs.Select(f).Order().ElementAt(Repeats / 2);
    Console.WriteLine($"[{(spin ? "spin" : "idle")}] events={r.Events} snapshots={r.Snaps} gen0(median)={Median(x => x.Gen0)}  (median of {Repeats})");
    Console.WriteLine($"  Apply:    {r.ApplyBytes / Math.Max(1, r.Events),8} B/event  {Median(x => x.ApplyUs),8:F3} us/event");
    Console.WriteLine($"  Snapshot: {r.SnapBytes / Math.Max(1, r.Snaps),8} B/snap   {Median(x => x.SnapUs),8:F3} us/snap");
    Console.WriteLine($"  Total:    {r.TotalBytes / Seconds,8} B per simulated second (Apply + Snapshot + Tick)");
    Console.WriteLine($"  digest={dg:X16}");
}

static Result Run(bool spin, bool digest)
{
    var clock = new SimClock();
    var engine = new InputEngine(clock, EngineConfig.From(new Settings()));
    engine.Apply(new PadAdded(0, 1, Pad(1, "guid-a")));
    engine.Apply(new PadAdded(0, 2, Pad(2, "guid-b")));
    PadButton[] taps = [PadButton.South, PadButton.East, PadButton.West, PadButton.North];

    long applyBytes = 0, applyTicks = 0, applyCount = 0;
    long snapBytes = 0, snapTicks = 0, snapCount = 0;
    long dg = 17;
    var sw = new Stopwatch();
    GC.Collect();
    int gen0 = GC.CollectionCount(0);
    long tickBytes = 0;

    void Apply(InputEvent e)
    {
        long b = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        engine.Apply(e);
        sw.Stop();
        applyBytes += GC.GetAllocatedBytesForCurrentThread() - b;
        applyTicks += sw.ElapsedTicks;
        applyCount++;
    }

    for (ulong t = 1; t <= Seconds * 1000UL; t++)
    {
        ulong ns = t * Ms;
        clock.MonotonicNs = ns;
        if (spin)
        {
            // 1 秒で 1 周。X と Y を 4 ミリ秒ごとに交互に送る(合わせて 250 回/秒)
            if (t % 4 == 0)
            {
                double a = t / 1000.0 * 2 * Math.PI;
                bool x = t / 4 % 2 == 0;
                short v = (short)((x ? Math.Cos(a) : -Math.Sin(a)) * 30000);
                Apply(new AxisChanged(ns, 1, x ? PadAxis.LeftX : PadAxis.LeftY, v));
            }
            // 10 回/秒の押下。4 回に 1 回は 2 台目のパッドも押す
            if (t % 100 == 0)
            {
                Apply(new ButtonChanged(ns, 1, taps[t / 100 % 4], true));
                if (t % 400 == 0)
                    Apply(new ButtonChanged(ns, 2, PadButton.South, true));
            }
            else if (t % 100 == 30)
            {
                Apply(new ButtonChanged(ns, 1, taps[t / 100 % 4], false));
                if (t % 400 == 30)
                    Apply(new ButtonChanged(ns, 2, PadButton.South, false));
            }
        }
        if (t % 250 == 0)
        {
            long b = GC.GetAllocatedBytesForCurrentThread();
            engine.Tick(ns);
            tickBytes += GC.GetAllocatedBytesForCurrentThread() - b;
        }
        // 回している間は 4 ミリ秒ごと、止まっている間は Tick のたびに出す(SdlInputThread の dirty と同じ)
        if (spin ? t % 4 == 2 : t % 250 == 0)
        {
            long b = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            var s = engine.Snapshot(ns, 0);
            sw.Stop();
            snapBytes += GC.GetAllocatedBytesForCurrentThread() - b;
            snapTicks += sw.ElapsedTicks;
            snapCount++;
            if (digest)
                dg = dg * 31 + Digest(s);
        }
    }
    long total = applyBytes + snapBytes + tickBytes; // digest の割り当ては数えない
    gen0 = GC.CollectionCount(0) - gen0;
    double us(long ticks, long n) => n == 0 ? 0 : ticks * 1e6 / Stopwatch.Frequency / n;
    return new(applyCount, snapCount, applyBytes, snapBytes, total, us(applyTicks, applyCount), us(snapTicks, snapCount), gen0, dg);
}

static PadInfo Pad(uint id, string guid) =>
    new(id, guid, "Probe Pad " + id, PadKind.Xbox, new HashSet<PadButton>(Enum.GetValues<PadButton>()), true, "XBOXONE");

// スナップショットの中身を数にまとめる(直す前後で振る舞いが同じかを見るため。古い写しを返していれば変わる)
static long Digest(PadSnapshot s)
{
    long h = s.History.Count;
    foreach (var r in s.History)
    {
        h = h * 31 + r.Direction + (long)r.StartNs + (long)(r.EndNs ?? 1) + (long)(r.MergeGapNs ?? 3) + Str(r.Motion);
        foreach (var k in r.NewlyPressed) h = h * 31 + Str(k);
        foreach (var k in r.Held) h = h * 37 + Str(k);
    }
    h = h * 31 + s.LeftTrail.Count + s.RightTrail.Count * 7;
    foreach (var p in s.LeftTrail)
        h = h * 31 + (long)p.Ns + (long)(p.X * 1000) + (long)(p.Y * 100000);
    var v = s.Session;
    h = h * 31 + (v.Active ? 1 : 0) + (long)v.StartNs + v.Inputs + v.Last60 * 3 + v.Peak * 5 + v.Peak1s * 7 + (v.PreviousInputs ?? 9);
    foreach (var kv in v.Top5)
        h = h * 31 + kv.Value + Str(kv.Key);
    h = h * 31 + s.Version + (s.Pad?.InstanceId ?? 99) + (s.Connected ? 1 : 0) + Set(s.Pressed) + Stick(s.Left) + Stick(s.Right)
        + (long)(s.LeftTrigger * 1000) + s.PressCounter + (long)s.LastPressNs + (s.DuplicateWarning ? 1 : 0);
    foreach (var p in s.Pads)
        h = h * 31 + p.InstanceId + Str(p.Guid);
    foreach (var kv in s.Live)
        h = h * 31 + kv.Key + Set(kv.Value.Pressed) + Stick(kv.Value.Left) + Stick(kv.Value.Right) + (long)(kv.Value.RightTrigger * 1000) + Str(kv.Value.Info.Guid);
    foreach (var kv in s.Sticks)
        h = h * 31 + kv.Key + Stick(kv.Value.Left) + Stick(kv.Value.Right);
    foreach (var kv in s.Chatter)
        foreach (var c in kv.Value)
            h = h * 31 + kv.Key + (int)c.Key + c.Value.Presses + c.Value.Short * 3;
    return h;

    static long Str(string? x) => x is null ? 0 : x.Aggregate(7L, (a, c) => a * 31 + c);
    static long Set(IReadOnlySet<PadButton> b) => b.Sum(x => 1L << (int)x);
    static long Stick(StickValue x) => x.RawX * 65537L + x.RawY + (long)(x.X * 1e6);
}

sealed record Result(long Events, long Snaps, long ApplyBytes, long SnapBytes, long TotalBytes, double ApplyUs, double SnapUs, int Gen0, long Digest);

sealed class SimClock : IClock
{
    public DateTime UtcNow { get; } = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    public ulong MonotonicNs { get; set; }
}
