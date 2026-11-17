// 十字キーと左スティック(割り当ての無いパッドはハット 1)から、方向をテンキー表記で決める(仕様書 §9.8・D-11)。
// スティックは、倒し具合のしきい値に 0.05 の遊び、角度の区切りに 3 度の遊びを持たせて、境目のちらつきを抑える。
namespace PadSight.Core;

public sealed class DirectionResolver
{
    // 区切りの番号(右から反時計回りに 45 度ずつ)とテンキーの対応
    static readonly int[] SectorToNumpad = [6, 9, 8, 7, 4, 1, 2, 3];

    bool _stickActive;
    int _lastStickDir = 5;

    public int Resolve(IReadOnlySet<PadButton> pressed, StickValue left, string source, float threshold)
    {
        bool up = pressed.Contains(PadButton.DpadUp), down = pressed.Contains(PadButton.DpadDown);
        bool l = pressed.Contains(PadButton.DpadLeft), r = pressed.Contains(PadButton.DpadRight);
        if (source != "stick" && (up || down || l || r))
        {
            _stickActive = false;
            _lastStickDir = 5;
            return FromParts(up && !down ? 1 : down && !up ? -1 : 0, r && !l ? 1 : l && !r ? -1 : 0);
        }
        if (source == "dpad")
            return 5;
        return FromStick(left, threshold);
    }

    public int FromStick(StickValue s, float threshold)
    {
        float mag = s.Magnitude;
        _stickActive = _stickActive ? mag >= threshold - 0.05f : mag >= threshold;
        if (!_stickActive)
        {
            _lastStickDir = 5;
            return 5;
        }
        float angle = s.AngleDeg;
        if (_lastStickDir != 5)
        {
            int prevSector = Array.IndexOf(SectorToNumpad, _lastStickDir);
            float diff = MathF.Abs(((angle - prevSector * 45f) % 360f + 540f) % 360f - 180f);
            if (diff <= 22.5f + 3f)
                return _lastStickDir;
        }
        int sector = (int)MathF.Floor((angle + 22.5f) / 45f) % 8;
        _lastStickDir = SectorToNumpad[sector];
        return _lastStickDir;
    }

    /// <summary>ハットの値(上 1、右 2、下 4、左 8 のビット)から方向を出す。</summary>
    public static int FromHat(byte hat)
    {
        bool up = (hat & 1) != 0, right = (hat & 2) != 0, down = (hat & 4) != 0, left = (hat & 8) != 0;
        return FromParts(up && !down ? 1 : down && !up ? -1 : 0, right && !left ? 1 : left && !right ? -1 : 0);
    }

    /// <param name="vertical">上が 1、下が -1</param>
    /// <param name="horizontal">右が 1、左が -1</param>
    public static int FromParts(int vertical, int horizontal) => 5 + vertical * 3 + horizontal;

    public void Reset()
    {
        _stickActive = false;
        _lastStickDir = 5;
    }
}
