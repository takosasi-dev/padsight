// スキンの選び方とボタンの表記(仕様書 §9.5.1・§9.5.2)。
// 鍵(PadButton の名前・LeftTrigger・RightTrigger・B1〜)を、スキンと表記の設定に合わせた文字にする。
namespace PadSight.Core;

public enum Skin { Xbox, PlayStation, SwitchPro, JoyConPair, Arcade, Generic, Unmapped }

public static class Labels
{
    /// <summary>§9.5.1。手で選んだ物(devices の skin)が auto 以外ならそれを使う。</summary>
    public static Skin ChooseSkin(PadInfo? pad, string? manual)
    {
        if (pad is not null && !pad.IsMapped)
            return Skin.Unmapped;
        switch (manual)
        {
            case "xbox": return Skin.Xbox;
            case "playstation": return Skin.PlayStation;
            case "switch_pro": return Skin.SwitchPro;
            case "joycon_pair": return Skin.JoyConPair;
            case "arcade": return Skin.Arcade;
            case "generic": return Skin.Generic;
        }
        return pad?.Kind switch
        {
            PadKind.Xbox => Skin.Xbox,
            PadKind.PlayStation => Skin.PlayStation,
            PadKind.SwitchPro => Skin.SwitchPro,
            PadKind.JoyConPair => Skin.JoyConPair,
            _ => Skin.Generic,
        };
    }

    enum Column { Xbox, PlayStation, Switch, Generic }

    static readonly string[] XboxFace = ["A", "B", "X", "Y"], PsFace = ["×", "○", "□", "△"],
        SwitchFace = ["B", "A", "Y", "X"], GenericFace = ["下", "右", "左", "上"];

    static Column ColumnFor(Skin skin, string labelStyle) => labelStyle switch
    {
        "xbox" => Column.Xbox,
        "playstation" => Column.PlayStation,
        _ => skin switch
        {
            Skin.Xbox or Skin.Arcade => Column.Xbox,
            Skin.PlayStation => Column.PlayStation,
            Skin.SwitchPro or Skin.JoyConPair => Column.Switch,
            _ => Column.Generic,
        },
    };

    /// <summary>鍵の表記。十字キーは矢印の文字(統計と CSV 用。パネルの矢印は図形で描く)。</summary>
    public static string For(string key, Skin skin, string labelStyle, PadInfo? pad)
    {
        var col = ColumnFor(skin, labelStyle);
        if (key == "LeftTrigger" || key == "RightTrigger")
        {
            bool l = key == "LeftTrigger";
            return col switch { Column.Xbox => l ? "LT" : "RT", Column.Switch => l ? "ZL" : "ZR", _ => l ? "L2" : "R2" };
        }
        if (!Enum.TryParse<PadButton>(key, out var b))
            return key; // B1〜
        switch (b)
        {
            case PadButton.South or PadButton.East or PadButton.West or PadButton.North:
                int i = (int)b;
                if (col == Column.Xbox) return XboxFace[i];
                if (col == Column.PlayStation) return PsFace[i];
                var sdl = pad?.FaceLabels is { Count: 4 } f ? f[i] : null;
                if (sdl is not null) return sdl;
                return col == Column.Switch ? SwitchFace[i] : GenericFace[i];
            case PadButton.LeftShoulder: return col switch { Column.Xbox => "LB", Column.Switch => "L", _ => "L1" };
            case PadButton.RightShoulder: return col switch { Column.Xbox => "RB", Column.Switch => "R", _ => "R1" };
            case PadButton.Back:
                return col switch
                {
                    Column.Xbox => "View",
                    Column.PlayStation => pad?.TypeName == "PS5" ? "Create" : "Share",
                    Column.Switch => "−",
                    _ => "Back",
                };
            case PadButton.Start: return col switch { Column.Xbox => "Menu", Column.PlayStation => "Options", Column.Switch => "+", _ => "Start" };
            case PadButton.Guide: return "ホーム";
            case PadButton.LeftStick: return col is Column.Xbox or Column.Switch ? "LS" : "L3";
            case PadButton.RightStick: return col is Column.Xbox or Column.Switch ? "RS" : "R3";
            case PadButton.Misc1: return col switch { Column.Xbox => "共有", Column.PlayStation => "マイク", Column.Switch => "キャプチャ", _ => "M1" };
            case PadButton.Touchpad: return "タッチパッド";
            case PadButton.Paddle1: return "P1";
            case PadButton.Paddle2: return "P2";
            case PadButton.Paddle3: return "P3";
            case PadButton.Paddle4: return "P4";
            case PadButton.DpadUp: return "↑";
            case PadButton.DpadDown: return "↓";
            case PadButton.DpadLeft: return "←";
            case PadButton.DpadRight: return "→";
            default: return key;
        }
    }

    /// <summary>保存したセッション(パッドの種類の名前だけを持つ)からの表記。CSV のメモと統計の画面で使う。</summary>
    public static string ForRecord(string key, string padKind)
    {
        var kind = Enum.TryParse<PadKind>(padKind, out var k) ? k : PadKind.Generic;
        var pad = new PadInfo(0, "", "", kind, new HashSet<PadButton>(), kind != PadKind.Unmapped);
        return For(key, ChooseSkin(pad, null), "pad", pad);
    }
}
