// 振動テスト(仕様書 FR-58・D-28)。INV-3 の唯一の例外で、パッドへ振動を送るのはこのファイルだけ(tools\check.ps1 がほかを止める)。
// 利用者が振動テストのボタンを押したときだけ、SDL のスレッドから呼ばれる。止めるのは SDL の長さの指定に任せる(終われば SDL が 0 にする)。
// 振動の可否の問い合わせ(性質を読むだけ)もここに置く。ほかのファイルには振動の API の名前を書かない。
using SDL3;

namespace PadSight.Input;

public static class PadVibration
{
    public const uint DurationMs = 500;

    /// <summary>左(低い音・重い)と右(高い音・軽い)のモーター。0〜65535。</summary>
    public static bool Motors(nint gamepad, ushort low, ushort high) => SDL.RumbleGamepad(gamepad, low, high, DurationMs);

    /// <summary>左右のトリガーのモーター(Xbox 系だけ)。0〜65535。</summary>
    public static bool Triggers(nint gamepad, ushort left, ushort right) => SDL.RumbleGamepadTriggers(gamepad, left, right, DurationMs);

    /// <summary>SDL が教える振動の可否(モーター、トリガー)。性質を読むだけで、パッドには何も送らない。</summary>
    public static (bool Motors, bool Triggers) Caps(nint gamepad)
    {
        uint props = SDL.GetGamepadProperties(gamepad);
        return (SDL.GetBooleanProperty(props, SDL.Props.GamepadCapRumbleBoolean, false),
                SDL.GetBooleanProperty(props, SDL.Props.GamepadCapTriggerRumbleBoolean, false));
    }
}
