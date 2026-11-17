// PadLab の統合の土台(仕様書 §9.20)を確かめる: 診断用の細かい値(DiagTap)、機種のプロファイルの選び方、診断の記録の読み書き。
using PadSight.Core;

namespace PadSight.Tests;

public class DiagFoundationTests
{
    [Fact]
    public void DiagTap_GivesStateAfterEachMappedEvent_AndNothingWhenOff()
    {
        var e = T.Engine(new FakeClock());
        var got = new List<DiagSample>();
        e.Apply(new PadAdded(T.S, 1, T.Xbox()));
        e.Apply(new PadAdded(T.S, 9, T.Unmapped()));
        e.Down(2 * T.S, PadButton.South); // 受け手が無い間は何も出ない
        e.DiagTap = got.Add;
        e.Down(3 * T.S, PadButton.East);
        e.Apply(new AxisChanged(4 * T.S, 1, PadAxis.LeftX, 16384));
        e.Apply(new AxisChanged(5 * T.S, 1, PadAxis.RightTrigger, 32767));
        e.Apply(new RawButtonChanged(6 * T.S, 9, 0, true)); // 割り当ての無いパッドは出さない
        Assert.Equal(3, got.Count);
        Assert.True(got[0].Has(PadButton.South) && got[0].Has(PadButton.East));
        Assert.Equal(3 * T.S, got[0].Ns);
        Assert.Equal(0.5f, got[1].Left.X, 2);
        Assert.Equal(1f, got[2].RightTrigger, 3);
        Assert.All(got, s => Assert.Equal(1u, s.PadId));
        e.DiagTap = null;
        e.Up(7 * T.S, PadButton.East);
        Assert.Equal(3, got.Count);
    }

    [Theory]
    [InlineData("XboxOne", "Xbox Wireless Controller", 0, "xbox_one")]
    [InlineData("XboxOne", "Xbox Elite Wireless Controller Series 2", 0, "xbox_elite2")]
    [InlineData("XboxOne", "Controller", 0x0B00, "xbox_elite2")]
    [InlineData("PS5", "DualSense Wireless Controller", 0, "dualsense")]
    [InlineData("PS5", "DualSense Edge Wireless Controller", 0, "dualsense_edge")]
    [InlineData("PS4", "PS4 Controller", 0, "dualshock4")]
    [InlineData("NintendoSwitchPro", "Nintendo Switch Pro Controller", 0, "switch_pro")]
    [InlineData("Standard", "8BitDo", 0, "generic")]
    public void Profile_IsChosenFromTypeNameAndProduct(string type, string name, int product, string want)
    {
        var pad = T.Xbox() with { TypeName = type, Name = name, Product = (ushort)product };
        Assert.Equal(want, PadProfiles.For(pad).Id);
    }

    [Fact]
    public void Profiles_HaveUniqueKeysAndTheBasicButtons()
    {
        foreach (var p in PadProfiles.All)
        {
            Assert.Equal(p.Buttons.Count, p.Buttons.Select(b => b.Key).Distinct().Count());
            Assert.Contains(p.Buttons, b => b.Key == "South");
            Assert.Contains(p.Buttons, b => b.Key == "LeftTrigger");
            Assert.All(p.Buttons.Where(b => b.Key is not ("LeftTrigger" or "RightTrigger")), b => Assert.True(Enum.TryParse<PadButton>(b.Key, out _)));
        }
        Assert.Equal(4, PadProfiles.XboxElite2.PaddleCount);
        Assert.Same(PadProfiles.SwitchPro, PadProfiles.ById("switch_pro"));
        Assert.Null(PadProfiles.ById("nope"));
    }

    [Fact]
    public void DiagRecord_RoundTrips_AndBrokenJsonIsNull()
    {
        var r = new DiagRecord("snapback", "スナップバック(左)", new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc), "DualSense", "PlayStation",
            [new("最大の振れ", 0.123), new("戻るまで(ms)", 45)], "振れが大きめの可能性があります", [0.9, 1.0, 0.95], "0.1.0");
        var back = DiagRecord.Parse(r.ToJson());
        Assert.NotNull(back);
        Assert.Equal(r.Kind, back.Kind);
        Assert.Equal(r.AtUtc, back.AtUtc);
        Assert.Equal(r.Values, back.Values);
        Assert.Equal(r.Shape, back.Shape);
        Assert.Equal(r.Verdict, back.Verdict);
        Assert.DoesNotContain("Ns", r.ToJson()); // 時刻つきの入力は入れない(INV-8)
        Assert.Null(DiagRecord.Parse("{"));
        Assert.Null(DiagRecord.Parse("[]"));
        Assert.Null(DiagRecord.Parse("{\"kind\":1}"));
    }
}
