// 方向の決め方(仕様書 §9.8・D-11・AC-2・E-22)を確かめる。
// 十字キーの 8 方向と同時押しの打ち消し、スティックの 8 方向、しきい値と角度の遊び、ハットの値。
using PadSight.Core;

namespace PadSight.Tests;

public class DirectionResolverTests
{
    static int Dpad(params PadButton[] b) => new DirectionResolver().Resolve(new HashSet<PadButton>(b), default, "both", 0.5f);

    [Theory]
    [InlineData(8, PadButton.DpadUp)]
    [InlineData(2, PadButton.DpadDown)]
    [InlineData(4, PadButton.DpadLeft)]
    [InlineData(6, PadButton.DpadRight)]
    [InlineData(9, PadButton.DpadUp, PadButton.DpadRight)]
    [InlineData(7, PadButton.DpadUp, PadButton.DpadLeft)]
    [InlineData(3, PadButton.DpadDown, PadButton.DpadRight)]
    [InlineData(1, PadButton.DpadDown, PadButton.DpadLeft)]
    [InlineData(5)]
    public void Resolve_Dpad_EightDirections(int expected, params PadButton[] pressed) =>
        Assert.Equal(expected, Dpad(pressed));

    [Theory]
    [InlineData(5, PadButton.DpadUp, PadButton.DpadDown)]
    [InlineData(5, PadButton.DpadLeft, PadButton.DpadRight)]
    [InlineData(6, PadButton.DpadUp, PadButton.DpadDown, PadButton.DpadRight)]
    [InlineData(8, PadButton.DpadUp, PadButton.DpadLeft, PadButton.DpadRight)]
    [InlineData(5, PadButton.DpadUp, PadButton.DpadDown, PadButton.DpadLeft, PadButton.DpadRight)]
    public void Resolve_OpposingDpad_Cancels(int expected, params PadButton[] pressed) =>
        Assert.Equal(expected, Dpad(pressed));

    [Theory]
    [InlineData(0f, 6)]
    [InlineData(45f, 9)]
    [InlineData(90f, 8)]
    [InlineData(135f, 7)]
    [InlineData(180f, 4)]
    [InlineData(225f, 1)]
    [InlineData(270f, 2)]
    [InlineData(315f, 3)]
    [InlineData(350f, 6)]
    [InlineData(22f, 6)]
    [InlineData(23f, 9)]
    public void FromStick_EightSectors(float deg, int expected) =>
        Assert.Equal(expected, new DirectionResolver().FromStick(T.Stick(deg, 0.9f), 0.5f));

    [Fact]
    public void FromStick_ThresholdHysteresis()
    {
        var d = new DirectionResolver();
        Assert.Equal(5, d.FromStick(T.Stick(0, 0.49f), 0.5f));
        Assert.Equal(6, d.FromStick(T.Stick(0, 0.50f), 0.5f));
        Assert.Equal(6, d.FromStick(T.Stick(0, 0.46f), 0.5f)); // T−0.05 までは方向あり
        Assert.Equal(5, d.FromStick(T.Stick(0, 0.44f), 0.5f));
        Assert.Equal(5, d.FromStick(T.Stick(0, 0.47f), 0.5f)); // 戻るには T が要る
        Assert.Equal(6, d.FromStick(T.Stick(0, 0.52f), 0.5f));
    }

    [Fact]
    public void FromStick_AngleHysteresis3Degrees()
    {
        var d = new DirectionResolver();
        Assert.Equal(6, d.FromStick(T.Stick(0, 0.9f), 0.5f));
        Assert.Equal(6, d.FromStick(T.Stick(24, 0.9f), 0.5f));
        Assert.Equal(9, d.FromStick(T.Stick(27, 0.9f), 0.5f));
        Assert.Equal(9, d.FromStick(T.Stick(21, 0.9f), 0.5f));
        Assert.Equal(6, d.FromStick(T.Stick(18, 0.9f), 0.5f));
    }

    [Fact]
    public void FromStick_WobbleAtBoundary_NoExtraRows()
    {
        var d = new DirectionResolver();
        var h = new HistoryBuilder();
        ulong t = 0;
        foreach (var deg in new[] { 21f, 24f, 21.5f, 23.9f, 22.6f, 22.4f, 25f, 20f })
            h.Update(t += T.Ms, d.FromStick(T.Stick(deg, 0.8f), 0.5f), []);
        Assert.Single(h.Rows);
        Assert.Equal(6, h.Rows[0].Direction);
    }

    [Fact]
    public void Resolve_DpadWinsOverStick_WhenBoth()
    {
        var d = new DirectionResolver();
        var stick = T.Stick(90, 0.9f);
        Assert.Equal(8, d.Resolve(T.None, stick, "both", 0.5f));
        Assert.Equal(2, d.Resolve(new HashSet<PadButton> { PadButton.DpadDown }, stick, "both", 0.5f));
        Assert.Equal(8, d.Resolve(T.None, stick, "both", 0.5f));
    }

    [Fact]
    public void Resolve_SourceDpad_IgnoresStick()
    {
        var d = new DirectionResolver();
        Assert.Equal(5, d.Resolve(T.None, T.Stick(90, 0.9f), "dpad", 0.5f));
        Assert.Equal(4, d.Resolve(new HashSet<PadButton> { PadButton.DpadLeft }, T.Stick(90, 0.9f), "dpad", 0.5f));
    }

    [Fact]
    public void Resolve_SourceStick_UsesStick()
    {
        var d = new DirectionResolver();
        Assert.Equal(7, d.Resolve(T.None, T.Stick(135, 0.9f), "stick", 0.5f));
    }

    [Theory]
    [InlineData((byte)0, 5)]
    [InlineData((byte)1, 8)]
    [InlineData((byte)2, 6)]
    [InlineData((byte)4, 2)]
    [InlineData((byte)8, 4)]
    [InlineData((byte)3, 9)]
    [InlineData((byte)6, 3)]
    [InlineData((byte)12, 1)]
    [InlineData((byte)9, 7)]
    [InlineData((byte)5, 5)]
    [InlineData((byte)10, 5)]
    public void FromHat_Bits(byte hat, int expected) => Assert.Equal(expected, DirectionResolver.FromHat(hat));
}
