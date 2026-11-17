// 入力履歴を止めるホットキーの設定(FR-48・AC-59)の既定値・読み書き・形の違う値の戻し方を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class PauseHistoryTests
{
    [Fact]
    public void Default_IsCtrlAltF()
    {
        Assert.Equal("Ctrl+Alt+F", new Settings().HotkeyPauseHistory);
        var s = Settings.Parse("{}", out var reset, out _);
        Assert.Equal("Ctrl+Alt+F", s.HotkeyPauseHistory);
        Assert.Empty(reset);
    }

    [Theory]
    [InlineData("\"F\"")]          // 修飾キーが無い
    [InlineData("\"Ctrl+Alt+\"")]
    [InlineData("\"Ctrl+Ctrl+F\"")]
    [InlineData("4")]
    [InlineData("true")]
    public void Parse_Malformed_ResetsToDefault(string value)
    {
        var s = Settings.Parse($$$"""{"hotkeys":{"pause_history":{{{value}}},"toggle":"Ctrl+Alt+K"}}""", out var reset, out _);
        Assert.Equal("Ctrl+Alt+F", s.HotkeyPauseHistory);
        Assert.Equal(["hotkeys.pause_history"], reset);
        Assert.Equal("Ctrl+Alt+K", s.HotkeyToggle); // ほかの項目はそのまま
    }

    [Fact]
    public void Parse_Null_IsUnused()
    {
        var s = Settings.Parse("""{"hotkeys":{"pause_history":null}}""", out var reset, out _);
        Assert.Null(s.HotkeyPauseHistory);
        Assert.Empty(reset);
    }

    [Theory]
    [InlineData("Shift+F7")]
    [InlineData(null)]
    public void ToJson_Parse_RoundTrip(string? combo)
    {
        var s = new Settings { HotkeyPauseHistory = combo };
        var json = s.ToJson();
        Assert.Contains("\"pause_history\"", json);
        var back = Settings.Parse(json, out var reset, out _);
        Assert.Empty(reset);
        Assert.Equal(combo, back.HotkeyPauseHistory);
        Assert.Equal(combo, s.Clone().HotkeyPauseHistory); // 写しから戻す・プロファイルを当てるときも消えない
    }
}
