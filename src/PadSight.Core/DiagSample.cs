// 診断用の細かい値(仕様書 §9.20)。診断のタブが測っている間だけ、SDL のスレッドがイベント 1 件ごとに、そのパッドの今の状態を 1 件作る。
// メモリだけで持ち、保存もログもしない(INV-8)。ボタンは PadButton の番号のビット。PadLab の GamepadSnapshot の代わりに、移した解析が受ける。
namespace PadSight.Core;

public readonly record struct DiagSample(
    ulong Ns, uint PadId, uint Buttons, StickValue Left, StickValue Right, float LeftTrigger, float RightTrigger)
{
    public bool Has(PadButton b) => (Buttons & Bit(b)) != 0;

    public static uint Bit(PadButton b) => 1u << (int)b;

    public static uint Mask(IEnumerable<PadButton> pressed)
    {
        uint m = 0;
        foreach (var b in pressed)
            m |= Bit(b);
        return m;
    }

    /// <summary>パッドの今の状態から 1 件作る。</summary>
    public static DiagSample Of(ulong ns, PadState p) =>
        new(ns, p.Info.InstanceId, Mask(p.Pressed), p.Left, p.Right, p.LeftTrigger, p.RightTrigger);
}

/// <summary>パッドの電池と、SDL が教える性質(§9.20 のパッドの情報)。SDL の問い合わせだけで作り、パッドには何も送らない。</summary>
/// <param name="Power">"OnBattery"・"Charging"・"Charged"・"NoBattery"(有線)・"Unknown"</param>
/// <param name="Percent">電池の残り(%)。分からなければ -1</param>
public sealed record PadPower(string Power, int Percent);
