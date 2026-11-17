// パッドの名前・種類・GUID・持っているボタンを SDL から読み、Core の PadInfo にする(仕様書 FR-1・§9.5.1)。
// SDL のボタンと軸の番号は、Core の PadButton・PadAxis と同じ並び(South〜Touchpad が 0〜20、軸が 0〜5)。
using System.Text;
using PadSight.Core;
using SDL3;

namespace PadSight.Input;

public static class DeviceCatalog
{
    public const int MaxMappedButton = (int)SDL.GamepadButton.Touchpad; // MISC2 以降は捨てる(§9.3)

    public static PadInfo Gamepad(uint id, nint handle)
    {
        var type = SDL.GetGamepadType(handle);
        var buttons = new HashSet<PadButton>();
        for (int b = 0; b <= MaxMappedButton; b++)
            if (SDL.GamepadHasButton(handle, (SDL.GamepadButton)b))
                buttons.Add((PadButton)b);
        string?[] faces = [.. new[] { SDL.GamepadButton.South, SDL.GamepadButton.East, SDL.GamepadButton.West, SDL.GamepadButton.North }
            .Select(b => Label(SDL.GetGamepadButtonLabel(handle, b)))];
        var (motors, triggers) = PadVibration.Caps(handle);
        return new PadInfo(id, Guid(id), SDL.GetGamepadName(handle) ?? SDL.GetGamepadNameForID(id) ?? "(名前なし)",
            Kind(type), buttons, true, type.ToString(), faces)
        {
            // §9.20 のパッドの情報。どれも SDL への問い合わせだけ
            Vendor = SDL.GetGamepadVendor(handle),
            Product = SDL.GetGamepadProduct(handle),
            Connection = Connection(SDL.GetGamepadConnectionState(handle)),
            CanVibrate = motors,
            CanVibrateTriggers = triggers,
        };
    }

    static string Connection(SDL.JoystickConnectionState s) => s switch
    {
        SDL.JoystickConnectionState.Wired => "Wired",
        SDL.JoystickConnectionState.Wireless => "Wireless",
        _ => "Unknown",
    };

    /// <summary>電池の状態(§9.20)。SDL への問い合わせだけ。</summary>
    public static PadPower Power(nint gamepad)
    {
        var state = SDL.GetGamepadPowerInfo(gamepad, out int percent);
        string name = state switch
        {
            SDL.PowerState.OnBattery => "OnBattery",
            SDL.PowerState.Charging => "Charging",
            SDL.PowerState.Charged => "Charged",
            SDL.PowerState.NoBattery => "NoBattery",
            _ => "Unknown",
        };
        return new PadPower(name, percent);
    }

    public static PadInfo Joystick(uint id, nint handle) =>
        new(id, Guid(id), SDL.GetJoystickName(handle) ?? "(名前なし)", PadKind.Unmapped, new HashSet<PadButton>(), false, "Unmapped", null,
            Math.Max(0, SDL.GetNumJoystickButtons(handle)), Math.Max(0, SDL.GetNumJoystickAxes(handle)), Math.Max(0, SDL.GetNumJoystickHats(handle)));

    public static PadKind Kind(SDL.GamepadType t) => t switch
    {
        SDL.GamepadType.Xbox360 or SDL.GamepadType.XboxOne => PadKind.Xbox,
        SDL.GamepadType.PS3 or SDL.GamepadType.PS4 or SDL.GamepadType.PS5 => PadKind.PlayStation,
        SDL.GamepadType.NintendoSwitchPro => PadKind.SwitchPro,
        SDL.GamepadType.NintendoSwitchJoyconPair => PadKind.JoyConPair,
        SDL.GamepadType.NintendoSwitchJoyconLeft => PadKind.JoyConLeft,
        SDL.GamepadType.NintendoSwitchJoyconRight => PadKind.JoyConRight,
        SDL.GamepadType.GameCube => PadKind.GameCube,
        _ => PadKind.Generic,
    };

    static string? Label(SDL.GamepadButtonLabel l) => l switch
    {
        SDL.GamepadButtonLabel.A => "A",
        SDL.GamepadButtonLabel.B => "B",
        SDL.GamepadButtonLabel.X => "X",
        SDL.GamepadButtonLabel.Y => "Y",
        SDL.GamepadButtonLabel.Cross => "×",
        SDL.GamepadButtonLabel.Circle => "○",
        SDL.GamepadButtonLabel.Square => "□",
        SDL.GamepadButtonLabel.Triangle => "△",
        _ => null,
    };

    static string Guid(uint id)
    {
        var buf = new byte[33];
        SDL.GUIDToString(SDL.GetJoystickGUIDForID(id), buf, buf.Length);
        int n = Array.IndexOf(buf, (byte)0);
        return Encoding.ASCII.GetString(buf, 0, n < 0 ? buf.Length : n);
    }
}
