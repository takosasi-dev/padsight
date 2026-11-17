// 同時押しのまとめ(FR-50・AC-63)とコマンドの印(FR-51・AC-64)、その設定(history.merge_frames・history.motions)を確かめる。
// 境目はどちらも「以内」を含む: まとめは一番上の行の始まりから merge_frames/60 秒ちょうどまで、コマンドは 20/60 秒ちょうどまで。
using System.Text.Json.Nodes;
using PadSight.Core;

namespace PadSight.Tests;

public class MergeAndMotionTests
{
    const ulong F = T.S / 60; // 1/60 秒(端数は切り捨て。窓の計算と同じ)

    static (InputEngine e, FakeClock c) Start(EngineConfig? cfg = null)
    {
        var c = new FakeClock();
        var e = T.Engine(c, cfg);
        e.Apply(new PadAdded(0, 1, T.Xbox()));
        return (e, c);
    }

    static HistoryBuilder Builder(int mergeFrames = 2, bool motions = true) =>
        new() { MergeWindowNs = HistoryBuilder.MergeWindowOf(mergeFrames), Motions = motions };

    // ---- FR-50 同時押し ----

    [Fact]
    public void AC63_PressWithinOneFrame_MergesIntoOneRowWithGap1()
    {
        var (e, _) = Start();
        e.Down(T.S, PadButton.South);
        e.Down(T.S + F, PadButton.East);
        var h = e.Snapshot(2 * T.S, 0).History;
        var row = Assert.Single(h);
        Assert.Equal(["South", "East"], row.NewlyPressed); // 押した順
        Assert.Equal(F, row.MergeGapNs);
        Assert.Equal(T.S, row.StartNs);
        Assert.Equal("ずれ 1", HistoryBuilder.FormatGap(row.MergeGapNs!.Value, 60));
    }

    [Fact]
    public void AC63_PressAfterThreeFrames_IsSecondRow()
    {
        var (e, _) = Start();
        e.Down(T.S, PadButton.South);
        e.Down(T.S + 3 * F, PadButton.East);
        var h = e.Snapshot(2 * T.S, 0).History;
        Assert.Equal(2, h.Count);
        Assert.Equal(["East"], h[0].NewlyPressed);
        Assert.Equal(["South"], h[0].Held);
        Assert.All(h, r => Assert.Null(r.MergeGapNs));
    }

    [Fact]
    public void AC63_DirectionChange_AlwaysStartsNewRow()
    {
        var (e, _) = Start();
        e.Down(T.S, PadButton.South);
        e.Down(T.S + 1, PadButton.DpadDown); // 窓の中でも方向が変わったら新しい行
        e.Down(T.S + 2, PadButton.East);     // 一番上は方向の行なので、まとめない
        var h = e.Snapshot(2 * T.S, 0).History;
        Assert.Equal([2, 2, 5], h.Select(r => r.Direction));
        Assert.Equal(["East"], h[0].NewlyPressed);
        Assert.Empty(h[1].NewlyPressed);
        Assert.Equal(["South"], h[2].NewlyPressed);
        Assert.All(h, r => Assert.Null(r.MergeGapNs));
    }

    [Fact]
    public void AC63_MergeZero_KeepsOldBehaviour()
    {
        var (e, _) = Start(T.Config() with { MergeFrames = 0 });
        e.Down(T.S, PadButton.South);
        e.Down(T.S, PadButton.East); // 同じ時刻でも 2 行(D-10 のまま)
        var h = e.Snapshot(2 * T.S, 0).History;
        Assert.Equal(2, h.Count);
        Assert.Equal(T.S, h[1].EndNs);
        Assert.All(h, r => Assert.Null(r.MergeGapNs));
    }

    [Fact]
    public void Merge_SameTimestamp_ShowsGapZero()
    {
        var h = Builder();
        h.Update(T.S, 5, ["South"]);
        h.Update(T.S, 5, ["East", "South"]);
        var row = Assert.Single(h.Rows);
        Assert.Equal(0UL, row.MergeGapNs);
        Assert.Equal("ずれ 0", HistoryBuilder.FormatGap(0, 60));
    }

    [Fact]
    public void Merge_WindowBoundary_IsInclusive()
    {
        ulong w = HistoryBuilder.MergeWindowOf(2);
        Assert.Equal(33_333_333UL, w);

        var at = Builder();
        at.Update(T.S, 5, ["South"]);
        at.Update(T.S + w, 5, ["East", "South"]);
        Assert.Single(at.Rows);

        var after = Builder();
        after.Update(T.S, 5, ["South"]);
        after.Update(T.S + w + 1, 5, ["East", "South"]);
        Assert.Equal(2, after.Rows.Count);
    }

    [Fact]
    public void Merge_WindowCountsFromFirstPress_NotLast()
    {
        var h = Builder();
        h.Update(T.S, 5, ["South"]);
        h.Update(T.S + F, 5, ["East", "South"]);
        h.Update(T.S + 2 * F, 5, ["East", "North", "South"]);    // 始まりから 2/60 秒: まとめる
        h.Update(T.S + 3 * F, 5, ["East", "North", "South", "West"]); // 3/60 秒: 新しい行
        Assert.Equal(2, h.Rows.Count);
        Assert.Equal(["South", "East", "North"], h.Rows[1].NewlyPressed);
        Assert.Equal(2 * F, h.Rows[1].MergeGapNs);
        Assert.Equal(T.S + 3 * F, h.Rows[1].EndNs);
        Assert.Equal(["West"], h.Rows[0].NewlyPressed);
    }

    [Fact]
    public void Merge_ThenDirectionChange_NewRowAndMergedRowEnds()
    {
        var h = Builder();
        h.Update(T.S, 5, ["South"]);
        h.Update(T.S + F, 5, ["East", "South"]);
        h.Update(T.S + 10 * F, 6, ["East", "South"]);
        Assert.Equal([6, 5], h.Rows.Select(r => r.Direction));
        Assert.Equal(["East", "South"], h.Rows[0].Held);
        Assert.Equal(T.S + 10 * F, h.Rows[1].EndNs);
        Assert.Equal(F, h.Rows[1].MergeGapNs);
    }

    [Fact]
    public void Merge_RepressSameButton_IsNewRow()
    {
        var h = Builder();
        h.Update(T.S, 5, ["South"]);
        h.Update(T.S + 5 * T.Ms, 5, []);
        h.Update(T.S + 10 * T.Ms, 5, ["South"]); // 窓の中の押し直しは別の押下
        Assert.Equal(2, h.Rows.Count);
        Assert.All(h.Rows, r => Assert.Null(r.MergeGapNs));
    }

    [Fact]
    public void Merge_RepressHeldButton_IsNewRow()
    {
        var h = Builder();
        h.Update(T.S, 5, ["West"]);
        h.Update(2 * T.S, 5, ["South", "West"]);           // West は押しっぱなし
        h.Update(2 * T.S + 5 * T.Ms, 5, ["South"]);
        h.Update(2 * T.S + 10 * T.Ms, 5, ["South", "West"]); // 押しっぱなしだった物の押し直し
        Assert.Equal(3, h.Rows.Count);
    }

    [Fact]
    public void Merge_TopRowStartedByDirection_DoesNotMerge()
    {
        var h = Builder();
        h.Update(T.S, 6, []);
        h.Update(T.S + F, 6, ["South"]);
        Assert.Equal(2, h.Rows.Count);
        Assert.Null(h.Rows[0].MergeGapNs);
    }

    [Fact]
    public void Merge_ReplacesRow_OldSnapshotKeepsItsRow()
    {
        var (e, _) = Start();
        e.Down(T.S, PadButton.South);
        var before = e.Snapshot(T.S, 0).History;
        e.Down(T.S + F, PadButton.East);
        var after = e.Snapshot(T.S + F, 0).History;
        Assert.Equal(["South"], Assert.Single(before).NewlyPressed); // 止めた画面が持つ行は変わらない
        Assert.NotSame(before[0], after[0]);
    }

    [Theory]
    [InlineData(60, "ずれ 1")]
    [InlineData(120, "ずれ 2")]
    [InlineData(240, "ずれ 4")]
    [InlineData(0, "ずれ 17ms")]
    public void FormatGap_UsesFrameBaseUnits(int frameBase, string expected) =>
        Assert.Equal(expected, HistoryBuilder.FormatGap(F, frameBase));

    [Fact]
    public void FormatGap_RoundsLikeRowLength()
    {
        Assert.Equal("ずれ 1", HistoryBuilder.FormatGap(F / 2 + 1, 60)); // 0.5 は切り上げ
        Assert.Equal("ずれ 0", HistoryBuilder.FormatGap(F / 2 - 1_000, 60));
        Assert.Equal("ずれ 33ms", HistoryBuilder.FormatGap(2 * F, 0));
    }

    // ---- FR-51 コマンド ----

    /// <summary>(フレーム, 方向, ボタン) の列を流し、最後の行の印を返す。</summary>
    static string? Motion(params (double Frame, int Dir, string[] Keys)[] steps)
    {
        var h = Builder(mergeFrames: 0);
        foreach (var (f, d, k) in steps)
            h.Update(T.S + (ulong)Math.Round(f * T.S / 60.0), d, k);
        return h.Rows[0].Motion;
    }

    static readonly string[] A = ["South"], No = [];

    [Fact]
    public void AC64_DownDownForwardForward_A_Is236()
    {
        var (e, _) = Start();
        e.Down(T.S, PadButton.DpadDown);
        e.Down(T.S + 2 * F, PadButton.DpadRight);
        e.Up(T.S + 4 * F, PadButton.DpadDown);
        e.Down(T.S + 6 * F, PadButton.South);
        var h = e.Snapshot(2 * T.S, 0).History;
        Assert.Equal([6, 6, 3, 2], h.Select(r => r.Direction));
        Assert.Equal("236", h[0].Motion);
        Assert.All(h.Skip(1), r => Assert.Null(r.Motion)); // 方向だけの行には付けない
    }

    [Fact]
    public void AC64_ForwardDownDownForward_A_Is623() =>
        Assert.Equal("623", Motion((0, 6, No), (2, 2, No), (4, 3, No), (6, 3, A)));

    [Fact]
    public void AC64_ButtonAfter21Frames_NoMotion() =>
        Assert.Null(Motion((0, 2, No), (2, 3, No), (4, 6, No), (25, 6, A)));

    [Fact]
    public void AC64_41236_BeatsContained236() =>
        Assert.Equal("41236", Motion((0, 4, No), (2, 1, No), (4, 2, No), (6, 3, No), (8, 6, No), (10, 6, A)));

    [Fact]
    public void AC64_DownNeutralDown_A_Is22() =>
        Assert.Equal("22", Motion((0, 2, No), (3, 5, No), (6, 2, No), (8, 2, A)));

    [Fact]
    public void AC64_MotionsOff_NoMotion()
    {
        var (e, _) = Start(T.Config() with { Motions = false });
        e.Down(T.S, PadButton.DpadDown);
        e.Down(T.S + 2 * F, PadButton.DpadRight);
        e.Up(T.S + 4 * F, PadButton.DpadDown);
        e.Down(T.S + 6 * F, PadButton.South);
        Assert.All(e.Snapshot(2 * T.S, 0).History, r => Assert.Null(r.Motion));
    }

    [Fact]
    public void Motion_22_NeedsNeutralBetween()
    {
        Assert.Null(Motion((0, 2, No), (3, 3, No), (6, 2, No), (8, 2, A)));
        Assert.Null(Motion((0, 2, No), (8, 2, A))); // 押しっぱなしの下は 1 つにまとまる
    }

    [Fact]
    public void Motion_FirstDirectionExactlyAtWindow_Counts_OneNsEarlier_DoesNot()
    {
        var h = Builder(mergeFrames: 0);
        ulong button = 2 * T.S;
        h.Update(button - MotionDetector.WindowNs, 2, []);
        h.Update(button - 10 * F, 3, []);
        h.Update(button - 5 * F, 6, []);
        h.Update(button, 6, A);
        Assert.Equal("236", h.Rows[0].Motion);

        var g = Builder(mergeFrames: 0);
        g.Update(button - MotionDetector.WindowNs - 1, 2, []);
        g.Update(button - 10 * F, 3, []);
        g.Update(button - 5 * F, 6, []);
        g.Update(button, 6, A);
        Assert.Null(g.Rows[0].Motion);
    }

    [Fact]
    public void Motion_LongHeldDownBeforeWindow_DoesNotAnchor()
    {
        // 下を長く押してから ↘→ + A: 下の行の始まりが 20/60 秒より前なので成立しない(§9.19 のとおり)
        Assert.Null(Motion((0, 2, No), (30, 3, No), (32, 6, No), (34, 6, A)));
        // 下を押したまま B を押した行があっても、まとめた下の始まりは一番古い行の物
        Assert.Null(Motion((0, 2, No), (25, 2, ["East"]), (30, 3, No), (32, 6, No), (34, 6, A)));
    }

    [Fact]
    public void Motion_NeutralAndOtherDirectionsBetween_Allowed()
    {
        Assert.Equal("236", Motion((0, 2, No), (2, 5, No), (4, 3, No), (6, 6, No), (8, 6, A)));
        Assert.Equal("236", Motion((0, 2, No), (2, 1, No), (4, 3, No), (6, 6, No), (8, 6, A)));
    }

    [Fact]
    public void Motion_NoMirroring_214IsItsOwn()
    {
        Assert.Equal("214", Motion((0, 2, No), (2, 1, No), (4, 4, No), (6, 4, A)));
        Assert.Equal("421", Motion((0, 4, No), (2, 2, No), (4, 1, No), (6, 1, A)));
        Assert.Equal("63214", Motion((0, 6, No), (2, 3, No), (4, 2, No), (6, 1, No), (8, 4, No), (10, 4, A)));
    }

    [Fact]
    public void Motion_623BeatsContained236() =>
        Assert.Equal("623", Motion((0, 6, No), (2, 2, No), (4, 3, No), (6, 6, No), (8, 6, A)));

    [Fact]
    public void Motion_SameTier_NewerEndingWins()
    {
        Assert.Equal("214", Motion((0, 2, No), (1, 3, No), (2, 6, No), (3, 2, No), (4, 1, No), (5, 4, No), (6, 4, A)));
        Assert.Equal("236", Motion((0, 2, No), (1, 1, No), (2, 4, No), (3, 2, No), (4, 3, No), (5, 6, No), (6, 6, A)));
    }

    [Fact]
    public void Motion_ButtonRowOwnDirection_IsLastOfSequence()
    {
        // 方向とボタンが同じ時に変わった行(履歴の上では 1 行)でも、その行の方向が並びの最後に入る
        var h = Builder(mergeFrames: 0);
        h.Update(T.S, 2, []);
        h.Update(T.S + 2 * F, 3, []);
        h.Update(T.S + 4 * F, 6, A);
        Assert.Equal("236", h.Rows[0].Motion);
    }

    [Fact]
    public void Motion_ClearedRows_AreNotUsed()
    {
        var h = Builder(mergeFrames: 0);
        h.Update(T.S, 2, []);
        h.Update(T.S + 2 * F, 3, []);
        h.Clear();
        h.Update(T.S + 4 * F, 6, A);
        Assert.Null(h.Rows[0].Motion);
    }

    [Fact]
    public void Motion_KeptWhenButtonsMergeIntoRow()
    {
        var h = Builder(mergeFrames: 2);
        h.Update(T.S, 2, []);
        h.Update(T.S + 2 * F, 3, []);
        h.Update(T.S + 4 * F, 6, []);
        h.Update(T.S + 6 * F, 6, ["South"]);
        h.Update(T.S + 7 * F, 6, ["East", "South"]);
        var row = h.Rows[0];
        Assert.Equal("236", row.Motion);
        Assert.Equal(["South", "East"], row.NewlyPressed);
        Assert.Equal(F, row.MergeGapNs);
    }

    [Fact]
    public void Motion_EmptyRows_IsNull() => Assert.Null(MotionDetector.Detect([]));

    // ---- 設定 ----

    [Fact]
    public void Settings_Defaults()
    {
        var s = new Settings();
        Assert.Equal(2, s.History.MergeFrames);
        Assert.True(s.History.Motions);
        var cfg = EngineConfig.From(s);
        Assert.Equal((2, true), (cfg.MergeFrames, cfg.Motions));
    }

    [Fact]
    public void Settings_OldFileWithoutKeys_DefaultsWithoutReset()
    {
        var root = JsonNode.Parse(new Settings().ToJson())!.AsObject();
        var h = root["history"]!.AsObject();
        h.Remove("merge_frames");
        h.Remove("motions");
        var s = Settings.Parse(root.ToJsonString(), out var reset, out _);
        Assert.Empty(reset);
        Assert.Equal((2, true), (s.History.MergeFrames, s.History.Motions));
    }

    [Fact]
    public void Settings_RoundTripAndClone()
    {
        var s = new Settings();
        s.History.MergeFrames = 0;
        s.History.Motions = false;
        var back = Settings.Parse(s.ToJson(), out var reset, out _);
        Assert.Empty(reset);
        Assert.Equal((0, false), (back.History.MergeFrames, back.History.Motions));
        var c = s.Clone();
        Assert.Equal((0, false), (c.History.MergeFrames, c.History.Motions));
        s.History.MergeFrames = 5;
        Assert.Equal(5, Settings.Parse(s.ToJson(), out _, out _).History.MergeFrames);
        var cfg = EngineConfig.From(c);
        Assert.Equal((0, false), (cfg.MergeFrames, cfg.Motions));
    }

    [Theory]
    [InlineData("merge_frames", "6")]
    [InlineData("merge_frames", "-1")]
    [InlineData("merge_frames", "2.5")]
    [InlineData("merge_frames", "\"2\"")]
    [InlineData("merge_frames", "null")]
    [InlineData("motions", "\"yes\"")]
    [InlineData("motions", "1")]
    [InlineData("motions", "null")]
    public void Settings_BadValue_FallsBackToDefault(string key, string json)
    {
        var s0 = new Settings();
        s0.History.MergeFrames = 4;
        s0.History.Motions = false;
        var root = JsonNode.Parse(s0.ToJson())!.AsObject();
        root["history"]![key] = JsonNode.Parse(json);
        var s = Settings.Parse(root.ToJsonString(), out var reset, out _);
        Assert.Equal(["history." + key], reset);
        if (key == "merge_frames")
            Assert.Equal((2, false), (s.History.MergeFrames, s.History.Motions));
        else
            Assert.Equal((4, true), (s.History.MergeFrames, s.History.Motions));
    }

    [Fact]
    public void Settings_ProfileSaveAndApply_KeepCurrentValues()
    {
        // プロファイルの形は §9.14 で決まっている(overlay・history_rows・stats_rows)ので、この 2 つは持たず、当てても今の値のまま
        var s = new Settings();
        s.History.MergeFrames = 0;
        Assert.True(s.SaveProfile("練習"));
        Assert.False(s.Profiles["練習"].ContainsKey("merge_frames"));
        s.History.MergeFrames = 5;
        s.History.Motions = false;
        var applied = s.WithProfile("練習", out var reset);
        Assert.Empty(reset);
        Assert.Equal((5, false), (applied.History.MergeFrames, applied.History.Motions));
    }
}
