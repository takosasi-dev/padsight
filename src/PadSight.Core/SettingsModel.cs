// settings.json の型、既定値、範囲の確かめ(仕様書 §9.14)。
// 範囲の外・形の違う値は、その項目だけ既定値に戻し、戻した項目の名前を返す(値は返さない)。
// ファイルの読み書きは App が行い、ここは文字列だけを扱う(INV-13)。
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PadSight.Core;

public sealed class PanelSettings
{
    public bool Visible { get; set; } = true;
    public int Scale { get; set; } = 100;
    public int Opacity { get; set; } = 100; // 2026-11-17 に見づらいとの声で 90 から上げた
    public int BackgroundOpacity { get; set; } = 85; // 2026-11-17 に見づらいとの声で 40 から上げた
    public string? Monitor { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
}

public sealed class HistorySettings
{
    public int Rows { get; set; } = 20;
    /// <summary>60 / 120 / 144 / 240。0 はミリ秒("ms")。</summary>
    public int FrameBase { get; set; } = 60;
    public string DirectionSource { get; set; } = "both";
    public double StickThreshold { get; set; } = 0.50;
    public string DirectionStyle { get; set; } = "arrow";
    /// <summary>null は "default"。</summary>
    public List<string>? Buttons { get; set; }
}

public sealed class Settings
{
    public static readonly string[] PanelNames = ["pad", "history", "stick", "stats"];
    public static readonly string[] StatsRowNames = ["session", "inputs", "last60", "peak", "peak1s", "top5"];
    public static readonly string[] SkinNames = ["auto", "xbox", "playstation", "switch_pro", "joycon_pair", "arcade", "generic"];
    public static readonly int[] FrameBases = [60, 120, 144, 240, 0];

    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "system";
    public bool VisibleOnStart { get; set; } = true;
    public string PressedColor { get; set; } = "#FFC83D";
    public string LabelStyle { get; set; } = "pad";
    /// <summary>パネルの配色テーマ(FR-39)。</summary>
    public string PanelTheme { get; set; } = "classic";
    public string CustomBackground { get; set; } = "#000000";
    public string CustomLine { get; set; } = "#FFFFFF";
    public string CustomText { get; set; } = "#FFFFFF";
    public static readonly string[] PanelThemes = ["classic", "neon", "light", "contrast", "custom"];
    public Dictionary<string, PanelSettings> Panels { get; set; } = PanelNames.ToDictionary(n => n, _ => new PanelSettings());
    public string SelectionMode { get; set; } = "last_used";
    public string? PinnedGuid { get; set; }
    public Dictionary<string, string> Devices { get; set; } = [];
    public HistorySettings History { get; set; } = new();
    public int TrailMs { get; set; } = 500;
    public double Deadzone { get; set; } = 0.10;
    public bool ShowRaw { get; set; }
    public double TriggerThreshold { get; set; } = 0.50;
    public int SessionIdleMinutes { get; set; } = 10;
    public List<string> StatsRows { get; set; } = [.. StatsRowNames];
    public string? GrindLogGame { get; set; }
    public string GrindLogType { get; set; } = "パッド練習";
    public string? HotkeyToggle { get; set; } = "Ctrl+Alt+G";
    public string? HotkeyArrange { get; set; } = "Ctrl+Alt+J";
    public string? HotkeyClearHistory { get; set; } = "Ctrl+Alt+H";
    public string InputWgi { get; set; } = "default";
    public bool InputHidapiPlayStation { get; set; } = true;
    public bool InputHidapiSwitch { get; set; } = true;
    /// <summary>FR-44: 起動したときに新しい版を確かめる。</summary>
    public bool UpdateCheckOnStart { get; set; } = true;

    /// <summary>FR-42: 名前 → { overlay, history_rows, stats_rows }。値は当てるときに Parse で確かめる。</summary>
    public Dictionary<string, JsonObject> Profiles { get; set; } = [];
    public const int MaxProfiles = 20;

    public static bool IsProfileName(string name) => name.Trim().Length is >= 1 and <= 30;

    /// <summary>今の overlay と行の設定を、その名前で残す。名前が不正か、21 個目なら false。</summary>
    public bool SaveProfile(string name)
    {
        name = name.Trim();
        if (!IsProfileName(name) || (!Profiles.ContainsKey(name) && Profiles.Count >= MaxProfiles))
            return false;
        var root = JsonNode.Parse(ToJson())!;
        Profiles[name] = new JsonObject
        {
            ["overlay"] = root["overlay"]!.DeepClone(),
            ["history_rows"] = History.Rows,
            ["stats_rows"] = root["stats"]!["rows"]!.DeepClone(),
        };
        return true;
    }

    /// <summary>プロファイルを当てた設定を返す(自分は変えない)。範囲の外の値はその項目だけ既定に戻る。</summary>
    public Settings WithProfile(string name, out List<string> resetKeys)
    {
        var root = JsonNode.Parse(ToJson())!.AsObject();
        var p = Profiles[name];
        root["overlay"] = p["overlay"]!.DeepClone();
        if (p["history_rows"] is { } hr) root["history"]!["rows"] = hr.DeepClone();
        if (p["stats_rows"] is { } sr) root["stats"]!["rows"] = sr.DeepClone();
        return Parse(root.ToJsonString(), out resetKeys, out _);
    }

    public static IReadOnlyList<string> AllHistoryButtonKeys { get; } =
        [.. Enum.GetNames<PadButton>(), "LeftTrigger", "RightTrigger"];

    /// <summary>history.buttons が "default" のときに出す鍵(十字キーとホームを除く全部と、トリガー)。</summary>
    public static IReadOnlyList<string> DefaultHistoryButtons { get; } =
        AllHistoryButtonKeys.Where(k => !k.StartsWith("Dpad", StringComparison.Ordinal) && k != nameof(PadButton.Guide)).ToList();

    public static Settings Parse(string json, out List<string> resetKeys, out bool newerSchema)
    {
        var r = new Reader(JsonNode.Parse(json) as JsonObject ?? throw new JsonException("not an object"));
        var s = new Settings();
        newerSchema = r.Int("schemaVersion", 1, int.MaxValue, 1) > 1;
        s.Theme = r.Choice("theme", ["system", "light", "dark"], s.Theme);
        s.VisibleOnStart = r.Bool("overlay.visible_on_start", true);
        s.PressedColor = r.Str("overlay.pressed_color", s.PressedColor, IsColor);
        s.LabelStyle = r.Choice("overlay.label_style", ["pad", "xbox", "playstation"], s.LabelStyle);
        s.PanelTheme = r.Choice("overlay.panel_theme", PanelThemes, s.PanelTheme);
        s.CustomBackground = r.Str("overlay.custom_colors.background", s.CustomBackground, IsColor);
        s.CustomLine = r.Str("overlay.custom_colors.line", s.CustomLine, IsColor);
        s.CustomText = r.Str("overlay.custom_colors.text", s.CustomText, IsColor);
        foreach (var n in PanelNames)
        {
            var p = s.Panels[n];
            string b = $"overlay.panels.{n}.";
            p.Visible = r.Bool(b + "visible", true);
            p.Scale = r.Int(b + "scale", 50, 300, 100, 10);
            p.Opacity = r.Int(b + "opacity", 20, 100, 100);
            p.BackgroundOpacity = r.Int(b + "background_opacity", 0, 100, 85);
            p.Monitor = r.NullableStr(b + "monitor", _ => true);
            p.X = r.NullableDouble(b + "x");
            p.Y = r.NullableDouble(b + "y");
        }
        s.SelectionMode = r.Choice("selection.mode", ["last_used", "pinned"], s.SelectionMode);
        s.PinnedGuid = r.NullableStr("selection.pinned_guid", _ => true);
        if (r.Get("devices") is JsonObject devs)
            foreach (var kv in devs)
            {
                var skin = (kv.Value as JsonObject)?["skin"] is JsonValue sv && sv.TryGetValue<string>(out var str) ? str : null;
                if (skin is not null && SkinNames.Contains(skin)) s.Devices[kv.Key] = skin;
                else r.Reset("devices." + kv.Key);
            }
        var h = s.History;
        h.Rows = r.Int("history.rows", 5, 40, 20);
        var fb = r.Get("history.frame_base");
        if (fb is JsonValue fv && fv.TryGetValue<string>(out var fs) && fs == "ms") h.FrameBase = 0;
        else h.FrameBase = r.Int("history.frame_base", 60, 240, 60, allowed: [60, 120, 144, 240]);
        h.DirectionSource = r.Choice("history.direction_source", ["both", "dpad", "stick"], h.DirectionSource);
        h.StickThreshold = r.Double("history.stick_threshold", 0.20, 0.90, 0.50);
        h.DirectionStyle = r.Choice("history.direction_style", ["arrow", "number"], h.DirectionStyle);
        var hb = r.Get("history.buttons");
        if (hb is JsonArray arr)
        {
            var keys = arr.Select(x => x is JsonValue v && v.TryGetValue<string>(out var k) ? k : null).ToList();
            if (keys.All(k => k is not null && AllHistoryButtonKeys.Contains(k))) h.Buttons = keys!;
            else r.Reset("history.buttons");
        }
        else if (!(hb is null || (hb is JsonValue dv && dv.TryGetValue<string>(out var ds) && ds == "default")))
            r.Reset("history.buttons");
        s.TrailMs = r.Int("stick.trail_ms", 0, 2000, 500);
        s.Deadzone = r.Double("stick.deadzone", 0.0, 0.5, 0.10);
        s.ShowRaw = r.Bool("stick.show_raw", false);
        s.TriggerThreshold = r.Double("trigger_threshold", 0.10, 0.90, 0.50);
        s.SessionIdleMinutes = r.Int("stats.session_idle_minutes", 1, 60, 10);
        var rows = r.Get("stats.rows");
        if (rows is JsonArray ra)
        {
            var keys = ra.Select(x => x is JsonValue v && v.TryGetValue<string>(out var k) ? k : null).ToList();
            if (keys.All(k => k is not null && StatsRowNames.Contains(k)) && keys.Distinct().Count() == keys.Count) s.StatsRows = keys!;
            else r.Reset("stats.rows");
        }
        else if (rows is not null)
            r.Reset("stats.rows");
        s.GrindLogGame = r.NullableStr("export.grindlog_game", v => v.Trim().Length is >= 1 and <= 30);
        s.GrindLogType = r.Str("export.grindlog_type", s.GrindLogType, v => v.Trim().Length is >= 1 and <= 20);
        s.HotkeyToggle = r.Hotkey("hotkeys.toggle", s.HotkeyToggle);
        s.HotkeyArrange = r.Hotkey("hotkeys.arrange", s.HotkeyArrange);
        s.HotkeyClearHistory = r.Hotkey("hotkeys.clear_history", s.HotkeyClearHistory);
        s.InputWgi = r.Choice("input.wgi", ["default", "on", "off"], s.InputWgi);
        s.InputHidapiPlayStation = r.Bool("input.hidapi_playstation", true);
        s.InputHidapiSwitch = r.Bool("input.hidapi_switch", true);
        s.UpdateCheckOnStart = r.Bool("update.check_on_start", true);
        // 形の違うプロファイルはそれだけ捨てる。ログには名前を出さない(利用者が付けた名前なので)
        var profs = r.Get("profiles");
        if (profs is JsonObject po)
        {
            foreach (var (name, node) in po)
            {
                if (s.Profiles.Count < MaxProfiles && IsProfileName(name) && node is JsonObject p && p["overlay"] is JsonObject)
                    s.Profiles[name.Trim()] = (JsonObject)p.DeepClone();
                else
                    r.Reset("profiles");
            }
        }
        else if (profs is not null)
            r.Reset("profiles");
        s.SchemaVersion = 1;
        resetKeys = r.ResetKeys;
        return s;
    }

    public string ToJson()
    {
        var panels = new JsonObject();
        foreach (var n in PanelNames)
        {
            var p = Panels[n];
            panels[n] = new JsonObject
            {
                ["visible"] = p.Visible, ["scale"] = p.Scale, ["opacity"] = p.Opacity, ["background_opacity"] = p.BackgroundOpacity,
                ["monitor"] = p.Monitor, ["x"] = p.X, ["y"] = p.Y,
            };
        }
        var devices = new JsonObject();
        foreach (var kv in Devices)
            devices[kv.Key] = new JsonObject { ["skin"] = kv.Value };
        var o = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["theme"] = Theme,
            ["overlay"] = new JsonObject
            {
                ["visible_on_start"] = VisibleOnStart, ["pressed_color"] = PressedColor, ["label_style"] = LabelStyle,
                ["panel_theme"] = PanelTheme,
                ["custom_colors"] = new JsonObject { ["background"] = CustomBackground, ["line"] = CustomLine, ["text"] = CustomText },
                ["panels"] = panels,
            },
            ["selection"] = new JsonObject { ["mode"] = SelectionMode, ["pinned_guid"] = PinnedGuid },
            ["devices"] = devices,
            ["history"] = new JsonObject
            {
                ["rows"] = History.Rows,
                ["frame_base"] = History.FrameBase == 0 ? JsonValue.Create("ms") : JsonValue.Create(History.FrameBase),
                ["direction_source"] = History.DirectionSource,
                ["stick_threshold"] = History.StickThreshold,
                ["direction_style"] = History.DirectionStyle,
                ["buttons"] = History.Buttons is null ? JsonValue.Create("default") : new JsonArray([.. History.Buttons.Select(b => (JsonNode)b)]),
            },
            ["stick"] = new JsonObject { ["trail_ms"] = TrailMs, ["deadzone"] = Deadzone, ["show_raw"] = ShowRaw },
            ["trigger_threshold"] = TriggerThreshold,
            ["stats"] = new JsonObject
            {
                ["session_idle_minutes"] = SessionIdleMinutes,
                ["rows"] = new JsonArray([.. StatsRows.Select(x => (JsonNode)x)]),
            },
            ["export"] = new JsonObject { ["grindlog_game"] = GrindLogGame, ["grindlog_type"] = GrindLogType },
            ["hotkeys"] = new JsonObject { ["toggle"] = HotkeyToggle, ["arrange"] = HotkeyArrange, ["clear_history"] = HotkeyClearHistory },
            ["input"] = new JsonObject { ["wgi"] = InputWgi, ["hidapi_playstation"] = InputHidapiPlayStation, ["hidapi_switch"] = InputHidapiSwitch },
            ["update"] = new JsonObject { ["check_on_start"] = UpdateCheckOnStart },
            ["profiles"] = new JsonObject(Profiles.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value.DeepClone()))),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public Settings Clone() => Parse(ToJson(), out _, out _);

    public IReadOnlyList<string> HistoryButtonKeys => History.Buttons ?? DefaultHistoryButtons;

    public static bool IsColor(string v) => Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$");

    /// <summary>「修飾キー+キー」の形か。修飾キーの無い組み合わせは不可(FR-12)。</summary>
    public static bool IsValidHotkey(string v)
    {
        var parts = v.Split('+');
        return parts.Length >= 2
            && parts[..^1].All(m => m is "Ctrl" or "Alt" or "Shift" or "Win")
            && parts[..^1].Distinct().Count() == parts.Length - 1
            && Regex.IsMatch(parts[^1], "^([A-Z0-9]|F([1-9]|1[0-9]|2[0-4]))$");
    }

    sealed class Reader(JsonObject root)
    {
        public List<string> ResetKeys { get; } = [];

        public JsonNode? Get(string path)
        {
            JsonNode? n = root;
            foreach (var p in path.Split('.'))
            {
                if (n is not JsonObject o || !o.TryGetPropertyValue(p, out n))
                    return null;
            }
            return n;
        }

        // 無い項目は既定値で、ログには出さない。あって形が違えば戻した項目として数える。
        T Value<T>(string path, T def, Func<JsonValue, (bool ok, T v)> read)
        {
            var n = Get(path);
            if (n is null && !Exists(path))
                return def;
            if (n is JsonValue v)
            {
                var (ok, val) = read(v);
                if (ok) return val;
            }
            Reset(path);
            return def;
        }

        bool Exists(string path)
        {
            int dot = path.LastIndexOf('.');
            var parent = dot < 0 ? root : Get(path[..dot]);
            return parent is JsonObject o && o.ContainsKey(path[(dot + 1)..]);
        }

        public void Reset(string path) => ResetKeys.Add(path);

        public int Int(string path, int min, int max, int def, int step = 1, int[]? allowed = null) => Value(path, def, v =>
            v.TryGetValue<double>(out var d) && d == Math.Floor(d) && d >= min && d <= max && ((int)d - min) % step == 0
            && (allowed is null || allowed.Contains((int)d)) ? (true, (int)d) : (false, def));

        public double Double(string path, double min, double max, double def) => Value(path, def, v =>
            v.TryGetValue<double>(out var d) && d >= min - 1e-9 && d <= max + 1e-9 ? (true, Math.Round(d, 2)) : (false, def));

        public bool Bool(string path, bool def) => Value(path, def, v => v.TryGetValue<bool>(out var b) ? (true, b) : (false, def));

        public string Str(string path, string def, Func<string, bool> ok) => Value(path, def, v =>
            v.TryGetValue<string>(out var s) && ok(s) ? (true, s.Trim()) : (false, def));

        public string Choice(string path, string[] choices, string def) => Str(path, def, choices.Contains);

        public string? NullableStr(string path, Func<string, bool> ok)
        {
            if (Get(path) is null) return null;
            return Value<string?>(path, null, v => v.TryGetValue<string>(out var s) && ok(s) ? (true, s.Trim()) : (false, null));
        }

        public double? NullableDouble(string path)
        {
            if (Get(path) is null) return null;
            return Value<double?>(path, null, v => v.TryGetValue<double>(out var d) && double.IsFinite(d) ? (true, d) : (false, null));
        }

        public string? Hotkey(string path, string? def)
        {
            if (Exists(path) && Get(path) is null) return null; // null は「使わない」
            return Value(path, def, v => v.TryGetValue<string>(out var s) && IsValidHotkey(s) ? (true, (string?)s) : (false, def));
        }
    }
}
