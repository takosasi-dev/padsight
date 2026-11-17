// テストで共通に使う物: 差し替えの時計、パッドの情報、イベントを流す手伝い。
using PadSight.Core;

namespace PadSight.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc);
    public ulong MonotonicNs { get; set; }
}

public static class T
{
    public const ulong Ms = Values.NsPerMs;
    public const ulong S = Values.NsPerSecond;
    public const ulong Min = 60 * S;

    public static PadInfo Xbox(uint id = 1, string guid = "guid-xbox", string name = "Xbox Controller") =>
        new(id, guid, name, PadKind.Xbox, new HashSet<PadButton>(Enum.GetValues<PadButton>()), true, "XBOXONE");

    public static PadInfo Unmapped(uint id = 9) =>
        new(id, "guid-raw", "Raw Stick", PadKind.Unmapped, new HashSet<PadButton>(), false, "Unmapped", null, 8, 2, 1);

    public static EngineConfig Config(int idleMinutes = 10, string mode = "last_used", string? pinned = null, string source = "both") =>
        EngineConfig.From(new Settings
        {
            SessionIdleMinutes = idleMinutes,
            SelectionMode = mode,
            PinnedGuid = pinned,
            History = new HistorySettings { DirectionSource = source },
        });

    public static InputEngine Engine(FakeClock clock, EngineConfig? config = null, Func<Guid>? newId = null) =>
        new(clock, config ?? Config(), newId);

    /// <summary>押して 10 ミリ秒後に離す。</summary>
    public static void Tap(this InputEngine e, ulong ns, PadButton b, uint id = 1)
    {
        e.Apply(new ButtonChanged(ns, id, b, true));
        e.Apply(new ButtonChanged(ns + 10 * Ms, id, b, false));
    }

    public static void Down(this InputEngine e, ulong ns, PadButton b, uint id = 1) => e.Apply(new ButtonChanged(ns, id, b, true));
    public static void Up(this InputEngine e, ulong ns, PadButton b, uint id = 1) => e.Apply(new ButtonChanged(ns, id, b, false));

    public static StickValue Stick(float deg, float mag) =>
        new(MathF.Cos(deg * MathF.PI / 180f) * mag, MathF.Sin(deg * MathF.PI / 180f) * mag, 0, 0);

    public static readonly HashSet<PadButton> None = [];
}
