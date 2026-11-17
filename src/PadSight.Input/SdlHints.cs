// SDL のヒントを SDL_Init の前に設定する、ただ 1 か所(仕様書 §9.2)。
// 固定の値(§9.2.1)と、入力の取り方の切り替え(§9.2.2)を持つ。
// tools\PadProbe もこのファイルをリンクして使う(Core に依存しない)。
using SDL3;

namespace PadSight.Input;

public static class SdlHints
{
    /// <summary>§9.2.1 の固定の値と、§9.2.2 の入力の取り方を設定する。SDL.Init の前に呼ぶ。</summary>
    /// <param name="wgi">"default"(設定しない)/ "on" / "off"</param>
    /// <param name="hidapiPlayStation">偽なら PS4・PS5 の HIDAPI を "0" にする。真なら設定しない</param>
    /// <param name="hidapiSwitch">偽なら Switch の HIDAPI を "0" にする。真なら設定しない</param>
    public static void Apply(string wgi, bool hidapiPlayStation, bool hidapiSwitch)
    {
        // 9.2.1 固定。Switch の Home のランプと Steam の仮想パッドのヒントは、設定しないことが「変えない」になる。
        SDL.SetHint(SDL.Hints.JoystickAllowBackgroundEvents, "1");
        SDL.SetHint(SDL.Hints.JoystickEnhancedReports, "0");
        SDL.SetHint(SDL.Hints.JoystickHIDAPIPS5PlayerLED, "0");
        SDL.SetHint(SDL.Hints.JoystickHIDAPISwitchPlayerLED, "0");
        SDL.SetHint(SDL.Hints.JoystickThread, "1");

        // 9.2.2 入力の取り方。初期化し直すときに前の値が残らないよう、「設定しない」は ResetHint で既定に戻す。
        SetOrReset(SDL.Hints.JoystickWGI, wgi switch { "on" => "1", "off" => "0", _ => null });
        SetOrReset(SDL.Hints.JoystickHIDAPIPS4, hidapiPlayStation ? null : "0");
        SetOrReset(SDL.Hints.JoystickHIDAPIPS5, hidapiPlayStation ? null : "0");
        SetOrReset(SDL.Hints.JoystickHIDAPISwitch, hidapiSwitch ? null : "0");
    }

    static void SetOrReset(string name, string? value)
    {
        if (value is null) SDL.ResetHint(name); else SDL.SetHint(name, value);
    }

    /// <summary>§9.2.2 の A・B・C を、Apply の引数にする(PadProbe 用)。</summary>
    public static (string Wgi, bool HidapiPlayStation, bool HidapiSwitch) Preset(string name) => name.ToUpperInvariant() switch
    {
        "A" => ("default", true, true),
        "B" => ("off", true, true),
        "C" => ("off", false, false),
        _ => throw new ArgumentException($"config は A・B・C のどれか: {name}", nameof(name)),
    };

    /// <summary>§9.2 の全部のヒントの名前(ログと PadProbe hints で今の値を出すため)。</summary>
    public static readonly string[] AllNames =
    [
        SDL.Hints.JoystickAllowBackgroundEvents,
        SDL.Hints.JoystickEnhancedReports,
        SDL.Hints.JoystickHIDAPIPS5PlayerLED,
        SDL.Hints.JoystickHIDAPISwitchPlayerLED,
        SDL.Hints.JoystickHIDAPISwitchHomeLED,
        SDL.Hints.JoystickThread,
        SDL.Hints.JoystickWGI,
        SDL.Hints.JoystickHIDAPIPS4,
        SDL.Hints.JoystickHIDAPIPS5,
        SDL.Hints.JoystickHIDAPISwitch,
    ];

    /// <summary>SDL の版を "3.4.20" の形で返す。</summary>
    public static string SdlVersion()
    {
        int v = SDL.GetVersion();
        return $"{v / 1000000}.{v / 1000 % 1000}.{v % 1000}";
    }
}
