// 通信で取ってきた物の確かめ(FR-43・FR-44・AC-52・AC-53)。通信はしない(文字列だけで確かめる)。
using PadSight.Core;

namespace PadSight.Tests;

public class WebChecksTests
{
    static string Db(int windows, params string[] extra)
    {
        var lines = new List<string> { "# Game Controller DB for SDL", "", "# Windows" };
        for (int i = 0; i < windows; i++)
            lines.Add($"03000000{i:x24},Pad {i},a:b0,b:b1,platform:Windows,");
        lines.Add("xinput,XInput Controller,a:b0,b:b1,platform:Windows,");
        lines.Add("030000005e0400008e02000014010000,X360,a:b0,platform:Linux,");
        lines.AddRange(extra);
        return string.Join("\r\n", lines) + "\r\n";
    }

    [Fact]
    public void Mapping_GoodList_CountsWindowsLines()
    {
        Assert.Equal(121, MappingDb.Check(Db(120)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>404</body></html>")]
    public void Mapping_NotAList_IsRejected(string text) => Assert.Null(MappingDb.Check(text));

    [Fact]
    public void Mapping_BadLineOrNulOrTooFew_IsRejected()
    {
        Assert.Null(MappingDb.Check(Db(120, "this is not a mapping")));
        Assert.Null(MappingDb.Check(Db(120) + "\0"));
        Assert.Null(MappingDb.Check(Db(90)));
    }

    const string Page = "https://github.com/takosasi-dev/padsight/releases/tag/";

    static string Json(string tag, string url) => $$"""{"tag_name":"{{tag}}","html_url":"{{url}}","draft":false}""";

    [Theory]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("v0.1.1", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.0", true)]   // v の無い tag
    [InlineData("v0.2", "0.1.0", true)]
    [InlineData("v0.1.0", "0.1.0", false)] // 同じ
    [InlineData("v0.1", "0.1.0", false)]   // 0.1 と 0.1.0 は同じ
    [InlineData("v0.0.9", "0.1.0", false)] // 古い
    [InlineData("latest", "0.1.0", false)] // 読めない tag
    public void Release_ComparesVersions(string tag, string current, bool newer)
    {
        var r = ReleaseCheck.Newer(Json(tag, Page + tag), current);
        Assert.Equal(newer, r is not null);
        if (newer) Assert.Equal((tag, Page + tag), r!.Value);
    }

    [Theory]
    [InlineData("https://example.com/padsight/releases/tag/v9.0.0")]
    [InlineData("http://github.com/takosasi-dev/padsight/releases/tag/v9.0.0")]
    [InlineData("https://github.com/someone-else/padsight/releases/tag/v9.0.0")]
    public void Release_OtherDestination_IsIgnored(string url) =>
        Assert.Null(ReleaseCheck.Newer(Json("v9.0.0", url), "0.1.0"));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":5,"html_url":"x"}""")]
    public void Release_Broken_IsNull(string json) => Assert.Null(ReleaseCheck.Newer(json, "0.1.0"));

    [Fact]
    public void Settings_UpdateCheck_DefaultsOn_AndRoundTrips()
    {
        Assert.True(new Settings().UpdateCheckOnStart);
        var s = new Settings { UpdateCheckOnStart = false };
        Assert.False(Settings.Parse(s.ToJson(), out var reset, out _).UpdateCheckOnStart);
        Assert.Empty(reset);
    }
}
