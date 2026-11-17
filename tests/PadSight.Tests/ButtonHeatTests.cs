// ボタン別の通算から塗りの濃さを出す計算(FR-47・AC-58)を確かめる。
using PadSight.Core;

namespace PadSight.Tests;

public class ButtonHeatTests
{
    [Fact]
    public void Intensities_MaxIsOne_ZeroIsZero_OthersProportional()
    {
        var h = ButtonHeat.Intensities(new Dictionary<string, long> { ["South"] = 200, ["East"] = 50, ["North"] = 0 });
        Assert.Equal(1.0, h["South"]);
        Assert.Equal(0.25, h["East"]);
        Assert.Equal(0.0, h["North"]);
    }

    [Fact]
    public void Intensities_EmptyOrAllZero_NoDivisionByZero()
    {
        Assert.Empty(ButtonHeat.Intensities(new Dictionary<string, long>()));
        var h = ButtonHeat.Intensities(new Dictionary<string, long> { ["South"] = 0 });
        Assert.Equal(0.0, h["South"]);
        Assert.Null(ButtonHeat.Top(new Dictionary<string, long>()));
        Assert.Null(ButtonHeat.Top(new Dictionary<string, long> { ["South"] = 0 }));
    }

    [Fact]
    public void Intensities_TriggerAndRawKeysIncluded()
    {
        var h = ButtonHeat.Intensities(new Dictionary<string, long> { ["RightTrigger"] = 400, ["LeftTrigger"] = 100, ["South"] = 200 });
        Assert.Equal(1.0, h["RightTrigger"]);
        Assert.Equal(0.25, h["LeftTrigger"]);
        Assert.Equal(0.5, h["South"]);
        Assert.Equal(("RightTrigger", 400L), ButtonHeat.Top(new Dictionary<string, long> { ["RightTrigger"] = 400, ["South"] = 200 }));
        Assert.Equal(1.0, ButtonHeat.Intensities(new Dictionary<string, long> { ["B3"] = 7 })["B3"]);
    }

    [Fact]
    public void Opacity_ZeroStaysUnfilled_SmallGetsFloor_LargeUnchanged()
    {
        Assert.Equal(0.0, ButtonHeat.Opacity(0));
        Assert.Equal(ButtonHeat.MinVisible, ButtonHeat.Opacity(0.01));
        Assert.Equal(0.6, ButtonHeat.Opacity(0.6));
        Assert.Equal(1.0, ButtonHeat.Opacity(1));
    }

    [Fact]
    public void Top_TieBreaksByKeyName()
    {
        Assert.Equal(("East", 5L), ButtonHeat.Top(new Dictionary<string, long> { ["South"] = 5, ["East"] = 5, ["North"] = 1 }));
    }
}
