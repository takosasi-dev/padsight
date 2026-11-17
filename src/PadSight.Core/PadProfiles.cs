// 機種のプロファイル(仕様書 §9.20・FR-55)。PadLab の profiles/*.json の中身(ボタンの一覧・名前・パドルの数)を、SDL の PadButton で書いた表。
// パッドの種類(SDL の GamepadType の名前)・名前・VID/PID から選ぶ。診断のタブで手で替えられる。ファイルは読まない(INV-13)。
// SDL が DualSense Edge の Fn・背面ボタン、Elite 2 のパドルをどの PadButton に出すかは未確認【要実機】。
namespace PadSight.Core;

/// <param name="Key">PadButton の名前、または "LeftTrigger"・"RightTrigger"</param>
/// <param name="Extended">PadLab の Extended(機種によって無いことがある物)。網羅の数には入れるが、無くても完了にできる</param>
public sealed record ProfileButton(string Key, string Label, bool Extended = false);

public sealed record PadProfile(string Id, string Name, IReadOnlyList<ProfileButton> Buttons, int PaddleCount);

public static class PadProfiles
{
    static ProfileButton B(PadButton b, string label, bool ext = false) => new(b.ToString(), label, ext);

    static IReadOnlyList<ProfileButton> Common(string a, string b, string x, string y, string lb, string rb, string lt, string rt,
        string ls, string rs, string start, string back, string guide, string dpad) =>
    [
        B(PadButton.South, a), B(PadButton.East, b), B(PadButton.West, x), B(PadButton.North, y),
        B(PadButton.LeftShoulder, lb), B(PadButton.RightShoulder, rb),
        new("LeftTrigger", lt), new("RightTrigger", rt),
        B(PadButton.DpadUp, dpad + " 上"), B(PadButton.DpadDown, dpad + " 下"), B(PadButton.DpadLeft, dpad + " 左"), B(PadButton.DpadRight, dpad + " 右"),
        B(PadButton.LeftStick, ls), B(PadButton.RightStick, rs),
        B(PadButton.Start, start), B(PadButton.Back, back), B(PadButton.Guide, guide),
    ];

    public static readonly PadProfile XboxOne = new("xbox_one", "Xbox ワイヤレス コントローラー",
        [.. Common("A", "B", "X", "Y", "LB", "RB", "LT", "RT", "左スティック押し込み", "右スティック押し込み", "Menu", "View", "Xbox", "十字"),
         B(PadButton.Misc1, "Share", true)], 0);

    public static readonly PadProfile XboxElite2 = new("xbox_elite2", "Xbox Elite ワイヤレス コントローラー シリーズ 2",
        [.. Common("A", "B", "X", "Y", "LB", "RB", "LT", "RT", "左スティック押し込み", "右スティック押し込み", "Menu", "View", "Xbox", "十字"),
         B(PadButton.Misc1, "Share", true)], 4);

    public static readonly PadProfile DualShock4 = new("dualshock4", "DUALSHOCK 4",
        [.. Common("×", "○", "□", "△", "L1", "R1", "L2", "R2", "L3", "R3", "Options", "Share", "PS", "方向キー"),
         B(PadButton.Touchpad, "タッチパッド", true)], 0);

    public static readonly PadProfile DualSense = new("dualsense", "DualSense",
        [.. Common("×", "○", "□", "△", "L1", "R1", "L2", "R2", "L3", "R3", "Options", "Create", "PS", "方向キー"),
         B(PadButton.Touchpad, "タッチパッド", true), B(PadButton.Misc1, "ミュート", true)], 0);

    public static readonly PadProfile DualSenseEdge = new("dualsense_edge", "DualSense Edge",
        [.. Common("×", "○", "□", "△", "L1", "R1", "L2", "R2", "L3", "R3", "Options", "Create", "PS", "方向キー"),
         B(PadButton.Touchpad, "タッチパッド", true), B(PadButton.Misc1, "ミュート", true)], 2);

    public static readonly PadProfile SwitchPro = new("switch_pro", "Nintendo Switch Pro コントローラー",
        [.. Common("B", "A", "Y", "X", "L", "R", "ZL", "ZR", "Lスティック押し込み", "Rスティック押し込み", "+", "−", "Home", "十字"),
         B(PadButton.Misc1, "キャプチャ", true)], 0);

    // SDL は汎用のパッドも標準の並びに割り当てるので、PadLab の「ボタン 1〜16」ではなく位置の名前で並べる
    public static readonly PadProfile Generic = new("generic", "汎用ゲームパッド",
        [.. Common("下のボタン", "右のボタン", "左のボタン", "上のボタン", "左肩", "右肩", "左トリガー", "右トリガー", "左スティック押し込み", "右スティック押し込み", "スタート", "セレクト", "ホーム", "十字")], 0);

    public static IReadOnlyList<PadProfile> All { get; } = [XboxOne, XboxElite2, DualShock4, DualSense, DualSenseEdge, SwitchPro, Generic];

    public static PadProfile? ById(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>パッドから選ぶ。Elite 2 と DualSense Edge は名前(と VID/PID)で見分ける。</summary>
    public static PadProfile For(PadInfo pad)
    {
        string n = pad.Name;
        bool Has(string s) => n.Contains(s, StringComparison.OrdinalIgnoreCase);
        return pad.TypeName switch
        {
            "XboxOne" or "Xbox360" => Has("Elite") || pad.Product is 0x0B00 or 0x0B05 or 0x0B22 ? XboxElite2 : XboxOne,
            "PS5" => Has("Edge") || pad.Product == 0x0DF2 ? DualSenseEdge : DualSense,
            "PS4" or "PS3" => DualShock4,
            "NintendoSwitchPro" => SwitchPro,
            _ => Generic,
        };
    }
}
