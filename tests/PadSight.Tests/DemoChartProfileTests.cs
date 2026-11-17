// 2026-11-17 の追加分を確かめる: current_session の GUID ごとの回数(AC-48)、デモの入力(AC-49)、
// 日ごとの棒グラフの値(AC-50)、プロファイル(AC-51)。
using System.Text.Json.Nodes;
using PadSight.Core;

namespace PadSight.Tests;

public class DemoChartProfileTests
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    static SessionRecord Rec(DateTime startUtc, long seconds, long inputs, int peak1s, Dictionary<string, PadTally>? per = null) =>
        new(Guid.NewGuid(), startUtc, startUtc.AddSeconds(seconds), seconds, "Xbox", "Pad", inputs, 0, new Dictionary<string, long> { ["South"] = inputs })
        {
            PeakPerSecond = peak1s,
            PerPad = per ?? new Dictionary<string, PadTally>(),
        };

    // ---- AC-48 ----

    [Fact]
    public void CurrentSession_KeepsPerPad_SessionsLineDoesNot()
    {
        var per = new Dictionary<string, PadTally> { ["guid-a"] = new("Pad A", "Xbox", 12, new Dictionary<string, long> { ["South"] = 12 }) };
        var r = Rec(new DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc), 90, 12, 3, per) with { LastOperationUtc = new DateTime(2026, 11, 17, 11, 1, 30, DateTimeKind.Utc) };

        var back = SessionJson.Parse(SessionJson.ToLine(r, Jst, withPerPad: true))!;
        var t = Assert.Single(back.PerPad);
        Assert.Equal(("guid-a", "Pad A", "Xbox", 12L, 12L), (t.Key, t.Value.Name, t.Value.Kind, t.Value.Inputs, t.Value.Buttons["South"]));

        string line = SessionJson.ToLine(r, Jst);
        Assert.DoesNotContain("perPad", line);
        Assert.Empty(SessionJson.Parse(line)!.PerPad);
    }

    [Fact]
    public void RecoveredSession_AddsToPadTotals()
    {
        var per = new Dictionary<string, PadTally> { ["guid-a"] = new("Pad A", "Xbox", 5, new Dictionary<string, long> { ["East"] = 5 }) };
        var recovered = SessionJson.Parse(SessionJson.ToLine(Rec(new DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc), 60, 5, 1, per), Jst, withPerPad: true))!;
        var totals = new PadTotals();
        totals.Add(recovered);
        Assert.Equal(5, totals.Pads["guid-a"].Inputs);
    }

    // ---- AC-49 ----

    [Fact]
    public void Demo_OneCycle_EndsNeutral_MakesMotionRows_AndFastMash()
    {
        var clock = new FakeClock();
        var e = T.Engine(clock);
        ulong start = T.S;
        var demo = new DemoPlayer(start);
        e.Apply(demo.Added());
        var dirs = new HashSet<int>();
        ulong t = start;
        for (; t < start + DemoScript.CycleNs - 16 * T.Ms; t += 16 * T.Ms)
        {
            foreach (var ev in demo.Advance(t))
                e.Apply(ev);
            foreach (var row in e.Snapshot(t, 0).History)
                dirs.Add(row.Direction);
        }
        var s = e.Snapshot(t, 0);
        Assert.Empty(s.Pressed);
        Assert.Equal((0f, 0f, 0f, 0f, 0f, 0f), (s.Left.X, s.Left.Y, s.Right.X, s.Right.Y, s.LeftTrigger, s.RightTrigger));
        Assert.Contains(2, dirs); // ↓
        Assert.Contains(3, dirs); // ↘
        Assert.Contains(6, dirs); // →
        Assert.True(s.Session.Peak1s >= 10, $"1 秒の最大 {s.Session.Peak1s}");
        Assert.DoesNotContain(s.History, r => r.EndNs == r.StartNs); // 長さ 0 の行を作らない
    }

    [Fact]
    public void Demo_SameTime_SameState()
    {
        var a = DemoScript.State(3_020 * T.Ms);
        var b = DemoScript.State(DemoScript.CycleNs + 3_020 * T.Ms);
        Assert.Equal(a.Pressed, b.Pressed);
        Assert.Equal(a.Axes, b.Axes);
        Assert.Contains(PadButton.South, a.Pressed);
    }

    // ---- AC-50 ----

    [Fact]
    public void DailyChart_30Days_StartDay_Range_AndPeakIsMax()
    {
        var today = new DateTime(2026, 11, 17);
        // JST 11/16 23:30 に始まり 0 時をまたぐ → 11/16
        var crossing = Rec(new DateTime(2026, 11, 16, 14, 30, 0, DateTimeKind.Utc), 3600, 100, 7);
        var sameDay = Rec(new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc), 600, 50, 12);
        var tooOld = Rec(new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc), 600, 999, 99);
        var first = Rec(new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc), 120, 4, 2); // 30 日前の初日(10/19)より前
        var day1 = Rec(new DateTime(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc), 120, 8, 2);
        var records = new[] { crossing, sameDay, tooOld, first, day1 };

        var inputs = DailyChart.Build(records, "inputs", today, Jst);
        Assert.Equal(30, inputs.Count);
        Assert.Equal(new DateTime(2026, 10, 19), inputs[0].Day);
        Assert.Equal(today, inputs[^1].Day);
        Assert.Equal(8, inputs[0].Value);
        Assert.Equal(150, inputs[^2].Value);
        Assert.Equal(0, inputs[^1].Value);
        Assert.Equal(158, inputs.Sum(d => d.Value));

        Assert.Equal(12, DailyChart.Build(records, "peak1s", today, Jst)[^2].Value);
        Assert.Equal(70, DailyChart.Build(records, "minutes", today, Jst)[^2].Value);
    }

    // ---- AC-51 ----

    [Fact]
    public void Profile_SaveChangeApply_RestoresOverlayAndRows()
    {
        var s = new Settings();
        s.PanelTheme = "neon";
        s.Panels["pad"].X = 120;
        s.History.Rows = 12;
        s.StatsRows = ["inputs", "peak1s"];
        Assert.True(s.SaveProfile("  格ゲー  "));

        s.PanelTheme = "light";
        s.Panels["pad"].X = 5;
        s.History.Rows = 30;
        s.StatsRows = ["session"];
        s.HotkeyToggle = "Ctrl+Alt+K"; // プロファイルに入らない物は、当てても今の値のまま

        var applied = s.WithProfile("格ゲー", out var reset);
        Assert.Empty(reset);
        Assert.Equal(("neon", 120.0, 12), (applied.PanelTheme, applied.Panels["pad"].X, applied.History.Rows));
        Assert.Equal(["inputs", "peak1s"], applied.StatsRows);
        Assert.Equal("Ctrl+Alt+K", applied.HotkeyToggle);
        Assert.Equal("light", s.PanelTheme); // 自分は変えない
    }

    [Fact]
    public void Profile_LimitAndNames()
    {
        var s = new Settings();
        Assert.False(s.SaveProfile("   "));
        Assert.False(s.SaveProfile(new string('a', 31)));
        for (int i = 0; i < Settings.MaxProfiles; i++)
            Assert.True(s.SaveProfile($"p{i}"));
        Assert.False(s.SaveProfile("p20"));
        Assert.True(s.SaveProfile("p3")); // 上書きは数に入らない
    }

    [Fact]
    public void Profile_SurvivesSave_AndBadOneIsDropped()
    {
        var s = new Settings();
        s.PanelTheme = "contrast";
        s.SaveProfile("配信");
        var json = JsonNode.Parse(s.ToJson())!;
        json["profiles"]!["壊れ"] = "overlay ではない";
        var back = Settings.Parse(json.ToJsonString(), out var reset, out _);
        Assert.Equal(["配信"], back.Profiles.Keys);
        Assert.Equal(["profiles"], reset);
        Assert.Equal("contrast", back.WithProfile("配信", out _).PanelTheme);
    }

    [Fact]
    public void Profile_BadValueInside_ResetsOnlyThatItem()
    {
        var s = new Settings();
        s.SaveProfile("x");
        s.Profiles["x"]["overlay"]!["panel_theme"] = "rainbow";
        s.Profiles["x"]["history_rows"] = 99;
        var applied = s.WithProfile("x", out var reset);
        Assert.Equal("classic", applied.PanelTheme);
        Assert.Equal(20, applied.History.Rows);
        Assert.Equal(["overlay.panel_theme", "history.rows"], reset);
    }
}
