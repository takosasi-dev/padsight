// PadProbe: SDL でパッドの一覧とイベントを標準出力に出す(仕様書 §9.13。Phase 0 で入力の取り方を比べるため)。
// ヒントは src\PadSight.Input\SdlHints.cs をリンクして同じ物を使う。パッドへ書き込む関数は呼ばない。
// 出力は標準出力だけ。ファイルにも残さず、利用者の名前も出さない。
using System.Diagnostics;
using System.Text;
using PadSight.Input;
using SDL3;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = "使い方: PadProbe hints | PadProbe list --config A|B|C | PadProbe watch --config A|B|C --seconds 60";

if (args.Length == 0) return Bad();
var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (int i = 1; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)) return Bad();
    opts[args[i][2..]] = args[i + 1];
}

string command = args[0].ToLowerInvariant();
string[] allowed = command switch { "hints" => [], "list" => ["config"], "watch" => ["config", "seconds"], _ => ["?"] };
if (allowed is ["?"] || opts.Keys.Any(k => !allowed.Contains(k, StringComparer.OrdinalIgnoreCase))) return Bad();

string config = opts.GetValueOrDefault("config", "A");
if (config.ToUpperInvariant() is not ("A" or "B" or "C")) return Bad();
int seconds = 60;
if (opts.TryGetValue("seconds", out var s) && (!int.TryParse(s, out seconds) || seconds <= 0)) return Bad();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (command == "hints")
{
    ApplyPreset("A");
    Console.WriteLine($"SDL {SdlHints.SdlVersion()}");
    foreach (var name in SdlHints.AllNames) Console.WriteLine($"{name} = {SDL.GetHint(name) ?? "(未設定)"}");
    return 0;
}

ApplyPreset(config);
Console.WriteLine($"SDL {SdlHints.SdlVersion()} / config {config.ToUpperInvariant()}");
if (!SDL.Init(SDL.InitFlags.Gamepad))
{
    Console.WriteLine($"SDL.Init に失敗しました: {SDL.GetError()}");
    return 1;
}
try
{
    return command == "list" ? List() : Watch();
}
finally
{
    SDL.Quit();
}

int Bad()
{
    Console.WriteLine(Usage);
    return 2;
}

static void ApplyPreset(string name)
{
    var p = SdlHints.Preset(name);
    SdlHints.Apply(p.Wgi, p.HidapiPlayStation, p.HidapiSwitch);
}

int List()
{
    // Windows.Gaming.Input のパッドは、イベントを処理したあとでないと一覧に出ない。
    var until = Environment.TickCount64 + 1500;
    while (!cts.IsCancellationRequested && Environment.TickCount64 < until)
        SDL.WaitEventTimeout(out _, (int)Math.Clamp(until - Environment.TickCount64, 1, 100));

    var ids = SDL.GetJoysticks(out _) ?? [];
    Console.WriteLine($"{ids.Length} 台 (InstanceId / 名前 / 種類 / GUID / 割り当て / ボタン)");
    foreach (var id in ids)
    {
        bool mapped = SDL.IsGamepad(id);
        string name = (mapped ? SDL.GetGamepadNameForID(id) : SDL.GetJoystickNameForID(id)) ?? "(名前なし)";
        Console.WriteLine($"{id}\t{name}\t{SDL.GetGamepadTypeForID(id)}\t{GuidText(id)}\t割り当て:{(mapped ? "あり" : "なし")}\t{(mapped ? Buttons(id) : "-")}");
    }
    return 0;
}

static string GuidText(uint id)
{
    var buf = new byte[33];
    SDL.GUIDToString(SDL.GetJoystickGUIDForID(id), buf, buf.Length);
    int end = Array.IndexOf(buf, (byte)0);
    return Encoding.ASCII.GetString(buf, 0, end < 0 ? buf.Length : end);
}

static string Buttons(uint id)
{
    var pad = SDL.OpenGamepad(id);
    if (pad == IntPtr.Zero) return $"(開けません: {SDL.GetError()})";
    try
    {
        return string.Join(",", Enum.GetValues<SDL.GamepadButton>()
            .Where(b => b >= 0 && b < SDL.GamepadButton.Count && SDL.GamepadHasButton(pad, b)));
    }
    finally
    {
        SDL.CloseGamepad(pad);
    }
}

int Watch()
{
    var pads = new Dictionary<uint, IntPtr>();
    var joys = new Dictionary<uint, IntPtr>(); // 割り当ての無いパッドだけ。Joystick のイベントはここにある物だけ出す
    var clock = Stopwatch.StartNew();
    Console.WriteLine($"{seconds} 秒のあいだイベントを出します(Ctrl+C で止める)。ms / InstanceId / 種類 / 名前 / 値");
    try
    {
        while (!cts.IsCancellationRequested && clock.ElapsedMilliseconds < seconds * 1000L)
        {
            if (!SDL.WaitEventTimeout(out var e, 100)) continue;
            switch ((SDL.EventType)e.Type)
            {
                case SDL.EventType.GamepadAdded:
                {
                    uint id = e.GDevice.Which;
                    if (!pads.ContainsKey(id))
                    {
                        var h = SDL.OpenGamepad(id);
                        if (h != IntPtr.Zero) pads[id] = h;
                    }
                    Print(id, "GamepadAdded", SDL.GetGamepadNameForID(id) ?? "(名前なし)", SDL.GetGamepadTypeForID(id).ToString());
                    break;
                }
                case SDL.EventType.GamepadRemoved:
                {
                    uint id = e.GDevice.Which;
                    if (pads.Remove(id, out var h)) SDL.CloseGamepad(h);
                    Print(id, "GamepadRemoved", "-", "-");
                    break;
                }
                case SDL.EventType.GamepadButtonDown:
                case SDL.EventType.GamepadButtonUp:
                    Print(e.GButton.Which, "GamepadButton", ((SDL.GamepadButton)e.GButton.Button).ToString(), e.GButton.Down ? "1" : "0");
                    break;
                case SDL.EventType.GamepadAxisMotion:
                    Print(e.GAxis.Which, "GamepadAxis", ((SDL.GamepadAxis)e.GAxis.Axis).ToString(), e.GAxis.Value.ToString());
                    break;
                case SDL.EventType.JoystickAdded:
                {
                    uint id = e.JDevice.Which;
                    if (SDL.IsGamepad(id) || joys.ContainsKey(id)) break; // 割り当てのある物は GamepadAdded で開く
                    var h = SDL.OpenJoystick(id);
                    if (h != IntPtr.Zero) joys[id] = h;
                    Print(id, "JoystickAdded", SDL.GetJoystickNameForID(id) ?? "(名前なし)", GuidText(id));
                    break;
                }
                case SDL.EventType.JoystickRemoved:
                {
                    uint id = e.JDevice.Which;
                    if (!joys.Remove(id, out var h)) break;
                    SDL.CloseJoystick(h);
                    Print(id, "JoystickRemoved", "-", "-");
                    break;
                }
                case SDL.EventType.JoystickButtonDown:
                case SDL.EventType.JoystickButtonUp:
                    if (joys.ContainsKey(e.JButton.Which))
                        Print(e.JButton.Which, "JoystickButton", $"button{e.JButton.Button}", e.JButton.Down ? "1" : "0");
                    break;
                case SDL.EventType.JoystickAxisMotion:
                    if (joys.ContainsKey(e.JAxis.Which))
                        Print(e.JAxis.Which, "JoystickAxis", $"axis{e.JAxis.Axis}", e.JAxis.Value.ToString());
                    break;
                case SDL.EventType.JoystickHatMotion:
                    if (joys.ContainsKey(e.JHat.Which))
                        Print(e.JHat.Which, "JoystickHat", $"hat{e.JHat.Hat}", e.JHat.Value.ToString());
                    break;
            }
        }
    }
    finally
    {
        foreach (var h in pads.Values) SDL.CloseGamepad(h);
        foreach (var h in joys.Values) SDL.CloseJoystick(h);
    }
    return 0;

    void Print(uint id, string kind, string name, string value) =>
        Console.WriteLine($"{clock.ElapsedMilliseconds,8}\t{id}\t{kind}\t{name}\t{value}");
}
