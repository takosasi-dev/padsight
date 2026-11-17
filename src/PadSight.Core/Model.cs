// Core の型(仕様書 §9.3)。パッドの情報、入力の出来事、スナップショット、時計。
// 「鍵」(key)は PadButton の名前、LeftTrigger・RightTrigger、割り当ての無いパッドの B1〜 をまとめた文字列で、
// 入力履歴の行と統計のボタン別の回数で同じ物を使う。
namespace PadSight.Core;

public enum PadKind { Xbox, PlayStation, SwitchPro, JoyConPair, JoyConLeft, JoyConRight, GameCube, Generic, Unmapped }

public enum PadButton
{
    South, East, West, North, Back, Guide, Start, LeftStick, RightStick,
    LeftShoulder, RightShoulder, DpadUp, DpadDown, DpadLeft, DpadRight,
    Misc1, Paddle1, Paddle2, Paddle3, Paddle4, Touchpad
}

public enum PadAxis { LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger }

/// <param name="TypeName">SDL の GamepadType の名前("PS5" など)。割り当て無しは "Unmapped"</param>
/// <param name="FaceLabels">South・East・West・North の刻印(SDL のラベル)。分からなければ null</param>
public sealed record PadInfo(
    uint InstanceId, string Guid, string Name, PadKind Kind,
    IReadOnlySet<PadButton> Buttons,
    bool IsMapped,
    string TypeName = "",
    IReadOnlyList<string?>? FaceLabels = null,
    int RawButtons = 0, int RawAxes = 0, int RawHats = 0);

public abstract record InputEvent(ulong TimestampNs, uint InstanceId);
public sealed record PadAdded(ulong TimestampNs, uint InstanceId, PadInfo Info) : InputEvent(TimestampNs, InstanceId);
public sealed record PadRemoved(ulong TimestampNs, uint InstanceId) : InputEvent(TimestampNs, InstanceId);
public sealed record ButtonChanged(ulong TimestampNs, uint InstanceId, PadButton Button, bool Down) : InputEvent(TimestampNs, InstanceId);
public sealed record AxisChanged(ulong TimestampNs, uint InstanceId, PadAxis Axis, short Raw) : InputEvent(TimestampNs, InstanceId);
public sealed record RawButtonChanged(ulong TimestampNs, uint InstanceId, int Index, bool Down) : InputEvent(TimestampNs, InstanceId);
public sealed record RawAxisChanged(ulong TimestampNs, uint InstanceId, int Index, short Raw) : InputEvent(TimestampNs, InstanceId);
public sealed record RawHatChanged(ulong TimestampNs, uint InstanceId, int Index, byte Value) : InputEvent(TimestampNs, InstanceId);

/// <summary>X は右が +、Y は上が +(§9.4)。</summary>
public readonly record struct StickValue(float X, float Y, short RawX, short RawY)
{
    public static StickValue FromRaw(short rawX, short rawY) => new(Values.Axis(rawX), -Values.Axis(rawY), rawX, rawY);
    public float Magnitude => MathF.Min(1f, MathF.Sqrt(X * X + Y * Y));
    /// <summary>0〜360 度。右が 0、反時計回り。</summary>
    public float AngleDeg => Values.Angle(X, Y);
}

public readonly record struct TrailPoint(ulong Ns, float X, float Y);

/// <param name="NewlyPressed">この行で新しく押した鍵</param>
/// <param name="Held">前の行から押しっぱなしの鍵</param>
public sealed record HistoryRow(
    int Direction,
    IReadOnlyList<string> NewlyPressed,
    IReadOnlyList<string> Held,
    ulong StartNs, ulong? EndNs);

/// <summary>統計のパネルに出す値。</summary>
public sealed record SessionView(
    bool Active, ulong StartNs, long Inputs, int Last60, int Peak,
    IReadOnlyList<KeyValuePair<string, long>> Top5,
    long? PreviousDurationSeconds, long? PreviousInputs);

/// <summary>割り当ての無いジョイスティックの今の値(FR-3)。</summary>
public sealed record RawState(IReadOnlyList<bool> Buttons, IReadOnlyList<float> Axes, IReadOnlyList<byte> Hats);

public enum InputStatus { Running, Reconnecting, Failed }

public sealed record PadSnapshot(
    long Version, PadInfo? Pad, bool Connected,
    IReadOnlySet<PadButton> Pressed, StickValue Left, StickValue Right,
    float LeftTrigger, float RightTrigger,
    IReadOnlyList<HistoryRow> History,
    SessionView Session,
    ulong SdlNsAtPublish, long StopwatchAtPublish)
{
    public IReadOnlyList<PadInfo> Pads { get; init; } = [];
    /// <summary>全部のパッドの左右のスティック(スティック診断でパッドを選ぶため)。鍵は InstanceId。</summary>
    public IReadOnlyDictionary<uint, (StickValue Left, StickValue Right)> Sticks { get; init; } = new Dictionary<uint, (StickValue, StickValue)>();
    public IReadOnlyList<TrailPoint> LeftTrail { get; init; } = [];
    public IReadOnlyList<TrailPoint> RightTrail { get; init; } = [];
    public RawState? Raw { get; init; }
    public InputStatus Status { get; init; } = InputStatus.Running;
    public string? Error { get; init; }
    /// <summary>全部のパッドのボタンの押下の通算(FR-7 の数え方に使う)。</summary>
    public long PressCounter { get; init; }
    /// <summary>最後のボタンの押下の SDL の時刻(NFR-1 の計測)。</summary>
    public ulong LastPressNs { get; init; }
    public bool DuplicateWarning { get; init; }
    /// <summary>gamecontrollerdb.txt を読んだ件数。null は置いていない、-1 は読めなかった。</summary>
    public int? MappingCount { get; init; }
    public bool SessionActive => Session.Active;

    public static PadSnapshot Empty { get; } = new(0, null, false, new HashSet<PadButton>(), default, default, 0, 0, [],
        new SessionView(false, 0, 0, 0, 0, [], null, null), 0, 0);
}

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>テストで差し替える。本番は SDL のイベントの時刻と同じ基準(SDL_GetTicksNS)。</summary>
    ulong MonotonicNs { get; }
}

public static class Values
{
    public const ulong NsPerSecond = 1_000_000_000UL;
    public const ulong NsPerMs = 1_000_000UL;

    public static float Axis(short raw) => MathF.Max(-1f, raw / 32767f);
    public static float Trigger(short raw) => Math.Clamp(raw / 32767f, 0f, 1f);

    public static float Angle(float x, float y)
    {
        float deg = MathF.Atan2(y, x) * 180f / MathF.PI;
        return deg < 0 ? deg + 360f : deg;
    }

    public static string TriggerKey(PadAxis axis) => axis == PadAxis.LeftTrigger ? "LeftTrigger" : "RightTrigger";
    public static string RawKey(int index) => "B" + (index + 1);
}
