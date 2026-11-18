// FR-61 触らない間の薄さ(AC-73)・FR-62 プロファイルのホットキー(AC-74)・FR-64 電池の知らせ(AC-76)。
using PadSight.Core;

namespace PadSight.Tests;

public class IdleBatteryProfileKeysTests
{
    const ulong S = Values.NsPerSecond;

    static PadSnapshot Snap(bool connected = true, IReadOnlySet<PadButton>? pressed = null, IReadOnlyList<HistoryRow>? history = null) =>
        PadSnapshot.Empty with { Connected = connected, Pressed = pressed ?? new HashSet<PadButton>(), History = history ?? [] };

    [Fact]
    public void IdleFade_DimsAfterWaitAndRecoversOnPress()
    {
        var f = new IdleFade();
        var idle = Snap();
        Assert.Equal(1, f.Factor(idle, 0, 5, 0.1));
        Assert.Equal(1, f.Factor(idle, 5 * S, 5, 0.1));
        Assert.Equal(0.65, f.Factor(idle, 5 * S + IdleFade.FadeNs / 2, 5, 0.1), 3);
        Assert.Equal(IdleFade.Dim, f.Factor(idle, 5 * S + IdleFade.FadeNs, 5, 0.1), 3);
        Assert.Equal(IdleFade.Dim, f.Factor(idle, 60 * S, 5, 0.1), 3);
        Assert.Equal(1, f.Factor(Snap(pressed: new HashSet<PadButton> { PadButton.South }), 61 * S, 5, 0.1));
        Assert.Equal(1, f.Factor(idle, 62 * S, 5, 0.1)); // 離してからまた数え直す
    }

    [Fact]
    public void IdleFade_NewHistoryRowCountsAsTouch()
    {
        var f = new IdleFade();
        var row = new HistoryRow(5, ["A"], [], 0, 1);
        f.Factor(Snap(history: [row]), 0, 5, 0.1);
        Assert.True(f.Factor(Snap(history: [row]), 10 * S, 5, 0.1) < 1);
        var next = new HistoryRow(5, ["B"], [], 10 * S, null);
        Assert.Equal(1, f.Factor(Snap(history: [next, row]), 10 * S + 1, 5, 0.1));
    }

    [Fact]
    public void IdleFade_OffOrDisconnected_IsAlwaysOne()
    {
        var f = new IdleFade();
        f.Factor(Snap(), 0, 0, 0.1);
        Assert.Equal(1, f.Factor(Snap(), 100 * S, 0, 0.1));
        Assert.Equal(1, f.Factor(Snap(connected: false), 200 * S, 5, 0.1));
    }

    [Fact]
    public void IdleFade_SettingParsesOnlyChoices()
    {
        Assert.Equal(0, new Settings().IdleFadeSeconds);
        Assert.Equal(10, Settings.Parse("""{"overlay":{"idle_fade_seconds":10}}""", out _, out _).IdleFadeSeconds);
        var s = Settings.Parse("""{"overlay":{"idle_fade_seconds":7}}""", out var reset, out _);
        Assert.Equal(0, s.IdleFadeSeconds);
        Assert.Equal(["overlay.idle_fade_seconds"], reset);
        Assert.Equal(30, Settings.Parse(new Settings { IdleFadeSeconds = 30 }.ToJson(), out _, out _).IdleFadeSeconds);
    }

    [Fact]
    public void ProfileHotkeys_DefaultRoundTripAndReset()
    {
        var d = Settings.Parse("{}", out var none, out _);
        Assert.Equal("Ctrl+Alt+1|Ctrl+Alt+2|Ctrl+Alt+3|Ctrl+Alt+4", string.Join('|', d.HotkeyProfiles));
        Assert.Empty(none);
        var s = new Settings();
        s.HotkeyProfiles[1] = null;
        s.HotkeyProfiles[2] = "Ctrl+Shift+9";
        var back = Settings.Parse(s.ToJson(), out _, out _);
        Assert.Equal("Ctrl+Alt+1||Ctrl+Shift+9|Ctrl+Alt+4", string.Join('|', back.HotkeyProfiles));
        Assert.Null(back.HotkeyProfiles[1]);
        var bad = Settings.Parse("""{"hotkeys":{"profile_4":"4"}}""", out var reset, out _);
        Assert.Equal("Ctrl+Alt+4", bad.HotkeyProfiles[3]);
        Assert.Equal(["hotkeys.profile_4"], reset);
        Assert.Equal(8, s.AllHotkeys.Length);
    }

    [Fact]
    public void ProfileOrder_IsByName()
    {
        var s = new Settings();
        s.SaveProfile("練習");
        s.SaveProfile("Apex");
        s.SaveProfile("格ゲー");
        Assert.Equal(s.Profiles.Keys.Order(StringComparer.CurrentCulture), s.ProfileOrder);
        Assert.Equal("Apex", s.ProfileOrder[0]);
    }

    static Dictionary<uint, PadPower> P(params (uint Id, string Power, int Percent)[] pads) =>
        pads.ToDictionary(p => p.Id, p => new PadPower(p.Power, p.Percent));

    [Fact]
    public void Battery_TellsOncePerLevel()
    {
        var w = new BatteryWatch();
        Assert.Empty(w.Feed(P((1, "OnBattery", 21))));
        Assert.Equal([(1u, 20)], w.Feed(P((1, "OnBattery", 20))));
        Assert.Empty(w.Feed(P((1, "OnBattery", 15))));
        Assert.Equal([(1u, 10)], w.Feed(P((1, "OnBattery", 10))));
        Assert.Empty(w.Feed(P((1, "OnBattery", 4))));
    }

    [Fact]
    public void Battery_StartsAtTenSkipsTwenty()
    {
        var w = new BatteryWatch();
        Assert.Equal([(1u, 8)], w.Feed(P((1, "OnBattery", 8))));
        Assert.Empty(w.Feed(P((1, "OnBattery", 18))));
    }

    [Fact]
    public void Battery_ForgetsOnChargeRecoveryOrRemoval()
    {
        var w = new BatteryWatch();
        w.Feed(P((1, "OnBattery", 19)));
        w.Feed(P((1, "Charging", 19)));
        Assert.Equal([(1u, 19)], w.Feed(P((1, "OnBattery", 19))));
        w.Feed(P((1, "OnBattery", 24)));
        Assert.Empty(w.Feed(P((1, "OnBattery", 19)))); // 25% 未満のゆれでは出し直さない
        w.Feed(P((1, "OnBattery", 25)));
        Assert.Equal([(1u, 19)], w.Feed(P((1, "OnBattery", 19))));
        w.Feed(P());
        Assert.Equal([(1u, 19)], w.Feed(P((1, "OnBattery", 19))));
    }

    [Fact]
    public void Battery_IgnoresWiredAndUnknownPercent()
    {
        var w = new BatteryWatch();
        Assert.Empty(w.Feed(P((1, "NoBattery", 0), (2, "OnBattery", -1), (3, "Unknown", 5), (4, "Charging", 5))));
    }
}
