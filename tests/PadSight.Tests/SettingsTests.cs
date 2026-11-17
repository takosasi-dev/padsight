// settings.json の読み取り(仕様書 §9.14・AC-5・FR-33)を確かめる。
// 範囲の外・形の違う値はその項目だけ既定値に戻り、戻した項目の名前が返ること。往復で値が残ること。
using System.Text.Json.Nodes;
using PadSight.Core;

namespace PadSight.Tests;

public class SettingsTests
{
    static readonly string DefaultJson = new Settings().ToJson();

    /// <summary>既定の JSON の 1 か所を書き換えて読む。</summary>
    static (Settings s, List<string> reset) ParseWith(string path, JsonNode? value)
    {
        var root = JsonNode.Parse(DefaultJson)!.AsObject();
        var parts = path.Split('.');
        var o = root;
        foreach (var p in parts[..^1])
            o = o[p]!.AsObject();
        o[parts[^1]] = value;
        var s = Settings.Parse(root.ToJsonString(), out var reset, out _);
        return (s, reset);
    }

    [Fact]
    public void Parse_Defaults_NoReset()
    {
        var s = Settings.Parse(DefaultJson, out var reset, out var newer);
        Assert.Empty(reset);
        Assert.False(newer);
        Assert.Equal(DefaultJson, s.ToJson());
    }

    [Fact]
    public void Parse_EmptyObject_AllDefaultsNoReset()
    {
        var s = Settings.Parse("{}", out var reset, out _);
        Assert.Empty(reset);
        Assert.Equal(DefaultJson, s.ToJson());
    }

    [Theory]
    [InlineData("overlay.panels.pad.scale", "55")]       // 10 刻みでない
    [InlineData("overlay.panels.pad.scale", "310")]
    [InlineData("overlay.panels.pad.scale", "40")]
    [InlineData("overlay.panels.stats.opacity", "10")]
    [InlineData("overlay.panels.stats.opacity", "\"90\"")]
    [InlineData("overlay.panels.stick.background_opacity", "101")]
    [InlineData("theme", "\"blue\"")]
    [InlineData("overlay.label_style", "\"nintendo\"")]
    [InlineData("history.rows", "4")]
    [InlineData("history.frame_base", "100")]
    [InlineData("history.frame_base", "\"fps\"")]
    [InlineData("history.stick_threshold", "0.95")]
    [InlineData("history.direction_source", "\"left\"")]
    [InlineData("history.buttons", "[\"South\",\"Jump\"]")]
    [InlineData("history.buttons", "\"all\"")]
    [InlineData("stick.trail_ms", "2001")]
    [InlineData("stick.deadzone", "0.6")]
    [InlineData("trigger_threshold", "0.05")]
    [InlineData("stats.session_idle_minutes", "0")]
    [InlineData("stats.session_idle_minutes", "61")]
    [InlineData("stats.rows", "[\"inputs\",\"inputs\"]")]
    [InlineData("export.grindlog_type", "\"\"")]
    [InlineData("export.grindlog_game", "\"0123456789012345678901234567890\"")]
    [InlineData("hotkeys.toggle", "\"G\"")]                // 修飾キーが無い
    [InlineData("hotkeys.arrange", "\"J\"")]
    [InlineData("hotkeys.clear_history", "123")]
    [InlineData("input.wgi", "\"maybe\"")]
    [InlineData("selection.mode", "\"random\"")]
    public void Parse_BadItem_ResetsOnlyThatItem(string path, string valueJson)
    {
        var (s, reset) = ParseWith(path, JsonNode.Parse(valueJson));
        Assert.Equal([path], reset);
        Assert.Equal(DefaultJson, s.ToJson());
    }

    [Fact]
    public void Parse_BadScale_KeepsOtherNonDefaultValues()
    {
        var root = JsonNode.Parse(DefaultJson)!.AsObject();
        root["theme"] = "dark";
        root["overlay"]!["panels"]!["pad"]!["scale"] = 55;
        root["overlay"]!["panels"]!["pad"]!["opacity"] = 70;
        root["overlay"]!["panels"]!["history"]!["scale"] = 150;
        var s = Settings.Parse(root.ToJsonString(), out var reset, out _);
        Assert.Equal(["overlay.panels.pad.scale"], reset);
        Assert.Equal(100, s.Panels["pad"].Scale);
        Assert.Equal(70, s.Panels["pad"].Opacity);
        Assert.Equal(150, s.Panels["history"].Scale);
        Assert.Equal("dark", s.Theme);
    }

    [Fact]
    public void Parse_FrameBaseMs_Accepted()
    {
        var (s, reset) = ParseWith("history.frame_base", "ms");
        Assert.Empty(reset);
        Assert.Equal(0, s.History.FrameBase);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(240)]
    public void Parse_FrameBaseNumbers_Accepted(int fb)
    {
        var (s, reset) = ParseWith("history.frame_base", fb);
        Assert.Empty(reset);
        Assert.Equal(fb, s.History.FrameBase);
    }

    [Fact]
    public void Parse_ButtonsDefault_UsesDefaultKeys()
    {
        var (s, reset) = ParseWith("history.buttons", "default");
        Assert.Empty(reset);
        Assert.Null(s.History.Buttons);
        Assert.Contains("RightTrigger", s.HistoryButtonKeys);
        Assert.Contains("South", s.HistoryButtonKeys);
        Assert.DoesNotContain("DpadUp", s.HistoryButtonKeys);
        Assert.DoesNotContain("Guide", s.HistoryButtonKeys);
    }

    [Fact]
    public void Parse_ButtonsList_Kept()
    {
        var (s, reset) = ParseWith("history.buttons", new JsonArray("South", "LeftTrigger"));
        Assert.Empty(reset);
        Assert.Equal(["South", "LeftTrigger"], s.HistoryButtonKeys);
    }

    [Fact]
    public void Parse_NullHotkey_Disabled()
    {
        var (s, reset) = ParseWith("hotkeys.toggle", null);
        Assert.Empty(reset);
        Assert.Null(s.HotkeyToggle);
        Assert.Equal("Ctrl+Alt+J", s.HotkeyArrange);
    }

    [Theory]
    [InlineData("Ctrl+Alt+G", true)]
    [InlineData("G", false)]          // 修飾キーが無い(FR-12・§9.11)
    [InlineData("Alt+", false)]
    public void IsValidHotkey(string v, bool ok) => Assert.Equal(ok, Settings.IsValidHotkey(v));

    [Fact]
    public void Parse_NewerSchema_Flagged()
    {
        var root = JsonNode.Parse(DefaultJson)!.AsObject();
        root["schemaVersion"] = 2;
        Settings.Parse(root.ToJsonString(), out _, out var newer);
        Assert.True(newer);
    }

    [Fact]
    public void Parse_UnknownDeviceSkin_ResetsThatDevice()
    {
        var root = JsonNode.Parse(DefaultJson)!.AsObject();
        root["devices"] = new JsonObject
        {
            ["guid-ok"] = new JsonObject { ["skin"] = "arcade" },
            ["guid-bad"] = new JsonObject { ["skin"] = "nes" },
        };
        var s = Settings.Parse(root.ToJsonString(), out var reset, out _);
        Assert.Equal(["devices.guid-bad"], reset);
        Assert.Equal("arcade", s.Devices["guid-ok"]);
        Assert.False(s.Devices.ContainsKey("guid-bad"));
    }

    [Fact]
    public void ToJson_Parse_RoundTripKeepsValues()
    {
        var s = new Settings
        {
            Theme = "dark",
            PressedColor = "#00FF7F",
            LabelStyle = "playstation",
            SelectionMode = "pinned",
            PinnedGuid = "0300abcd",
            TrailMs = 1200,
            Deadzone = 0.15,
            ShowRaw = true,
            TriggerThreshold = 0.3,
            SessionIdleMinutes = 25,
            StatsRows = ["peak", "inputs"],
            GrindLogGame = "スト6",
            GrindLogType = "トレモ",
            HotkeyToggle = null,
            HotkeyArrange = "Shift+F9",
            InputWgi = "off",
            InputHidapiSwitch = false,
            History = new HistorySettings
            {
                Rows = 33, FrameBase = 0, DirectionSource = "dpad", StickThreshold = 0.35,
                DirectionStyle = "number", Buttons = ["East", "RightTrigger"],
            },
        };
        s.Panels["stick"].Scale = 250;
        s.Panels["stick"].Opacity = 20;
        s.Panels["stick"].X = -1234.5;
        s.Panels["stick"].Monitor = @"\\.\DISPLAY2";
        s.Devices["guid-1"] = "switch_pro";

        var json = s.ToJson();
        var back = Settings.Parse(json, out var reset, out var newer);
        Assert.Empty(reset);
        Assert.False(newer);
        Assert.Equal(json, back.ToJson());
        Assert.Equal(0, back.History.FrameBase);
        Assert.Null(back.HotkeyToggle);
        Assert.Equal(-1234.5, back.Panels["stick"].X);
        Assert.Equal("スト6", back.GrindLogGame);
    }
}
