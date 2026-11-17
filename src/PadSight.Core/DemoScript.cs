// デモの入力(仕様書 FR-40・§9.16)。12 秒で 1 周する決まった動きを、仮のパッドのイベントとして作る。
// State(t) は周の始まりからの経過 t だけで決まり、DemoPlayer は前回との差をイベントにする。
namespace PadSight.Core;

public static class DemoScript
{
    public const uint Id = 0xDE000001;
    public const ulong CycleNs = 12 * Values.NsPerSecond;

    public static readonly PadInfo Info = new(Id, "demo", "デモ(PadSight)", PadKind.Xbox,
        new HashSet<PadButton>(Enum.GetValues<PadButton>()), true, "XBOXONE");

    /// <summary>経過 t(ns、周の中)での、押しているボタンと 6 本の軸の生の値(SDL の向き。Y は下が +)。</summary>
    public static (HashSet<PadButton> Pressed, short[] Axes) State(ulong tNs)
    {
        double t = (tNs % CycleNs) / (double)Values.NsPerSecond;
        var pressed = new HashSet<PadButton>();
        var axes = new short[6];
        if (t < 2)
            SetStick(axes, 0, t / 2 * 360, 1.0); // 左スティックを反時計回りに 1 周
        else if (t < 3)
        {
            // ↓ → ↘ → → → X(波動拳の形)。X を → と同じ時刻にすると、↓ を離す行と X の行が 0 フレームで分かれて見づらい
            if (t < 2.2) pressed.Add(PadButton.DpadDown);
            if (t is >= 2.1 and < 2.35) pressed.Add(PadButton.DpadRight);
            if (t is >= 2.25 and < 2.35) pressed.Add(PadButton.West);
        }
        else if (t < 5)
        {
            if ((t - 3) % 0.1 < 0.05) pressed.Add(PadButton.South); // 1 秒に 10 回
        }
        else if (t < 6.5)
        {
            double f = t < 6 ? t - 5 : (6.5 - t) * 2;
            axes[(int)PadAxis.RightTrigger] = (short)Math.Round(f * short.MaxValue);
            axes[(int)PadAxis.LeftTrigger] = (short)Math.Round(f * short.MaxValue / 2);
        }
        else if (t < 8.5)
            SetStick(axes, 2, Math.Floor((t - 6.5) / 0.25) * 45, 1.0); // 右スティックを 8 方向
        else if (t < 9.7)
            pressed.Add(((t - 8.5) / 0.3) switch
            {
                < 1 => PadButton.LeftShoulder,
                < 2 => PadButton.RightShoulder,
                < 3 => PadButton.North,
                _ => PadButton.East,
            });
        return (pressed, axes);
    }

    static void SetStick(short[] axes, int first, double deg, double mag)
    {
        double rad = deg * Math.PI / 180;
        axes[first] = (short)Math.Round(Math.Cos(rad) * mag * short.MaxValue);
        axes[first + 1] = (short)Math.Round(-Math.Sin(rad) * mag * short.MaxValue); // 上が + なので、SDL の向きでは反転
    }
}

/// <summary>デモを始めた時刻からの経過で、前回との差をイベントにする。1 本のスレッドから呼ぶ。</summary>
public sealed class DemoPlayer(ulong startNs)
{
    readonly HashSet<PadButton> _pressed = [];
    readonly short[] _axes = new short[6];

    public InputEvent Added() => new PadAdded(startNs, DemoScript.Id, DemoScript.Info);

    public List<InputEvent> Advance(ulong nowNs)
    {
        var (pressed, axes) = DemoScript.State(nowNs - startNs);
        var list = new List<InputEvent>();
        foreach (var b in _pressed.Except(pressed).ToList())
        {
            _pressed.Remove(b);
            list.Add(new ButtonChanged(nowNs, DemoScript.Id, b, false));
        }
        foreach (var b in pressed.Except(_pressed).ToList())
        {
            _pressed.Add(b);
            list.Add(new ButtonChanged(nowNs, DemoScript.Id, b, true));
        }
        for (int i = 0; i < 6; i++)
        {
            if (axes[i] == _axes[i])
                continue;
            _axes[i] = axes[i];
            list.Add(new AxisChanged(nowNs, DemoScript.Id, (PadAxis)i, axes[i]));
        }
        return list;
    }
}
