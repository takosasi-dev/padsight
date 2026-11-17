// 初回の案内と「このソフトについて」で同じ文を出すための、すること・しないことの文(画面設計書 §4.10)。
namespace PadSight.App;

public static class Texts
{
    public static readonly string[] Does =
    [
        "つないだゲームパッドの入力を読み、画面の端の小さいパネルに出します(読むだけです)",
        "パネルはゲームの上に重なりますが、クリックは下のゲームに届き、ゲームのフォーカスは取りません",
        "セッションごとに、長さと入力の回数を、この PC の中に残します",
    ];

    public static readonly string[] DoesNot =
    [
        "パッドに何も送りません(振動、ランプ、モードの切り替え)",
        "ゲームのプログラム・メモリ・窓には触りません。ゲームの中に描きません",
        "キーボードとマウスの入力は読みません(登録したホットキーだけを受け取ります)",
        "押した順番や時刻は残しません。通信しません",
    ];

    public static string[] HowToPlay(string? toggle, string? arrange) =>
    [
        "排他フルスクリーンのゲームの上には出ません。ボーダーレスかウィンドウで遊んでください",
        $"パネルの出し入れは {toggle ?? "(未設定)"}、置き場所を変えるのは {arrange ?? "(未設定)"} です(設定で変えられます)",
    ];

    public const string AntiCheat = "アンチチートのあるゲームで問題が起きないことを、このソフトは約束できません。気になるゲームでは使わないでください";

    public const string Fullscreen = "排他フルスクリーンの上には出ません。ボーダーレスかウィンドウで遊んでください";

    public const string HidHide = "出ないパッドがあるときは、パッドを隠す道具(HidHide)の設定を確かめてください";
}
