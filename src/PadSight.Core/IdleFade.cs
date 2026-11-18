// FR-61: 触らない間はパネルを薄くする(仕様書 §9.21)。最後に触られた時刻だけを持ち、薄さは時刻から決める。
namespace PadSight.Core;

public sealed class IdleFade
{
    public const double Dim = 0.3;
    public const ulong FadeNs = 500_000_000;
    const float TriggerActive = 0.05f;

    ulong _lastActiveNs;
    HistoryRow? _top;
    bool _seen;

    /// <summary>パネルの不透明度に掛ける値(Dim〜1)。毎フレーム、今のスナップショットで呼ぶ。</summary>
    public double Factor(PadSnapshot s, ulong nowNs, int seconds, double deadzone)
    {
        var top = s.History.Count > 0 ? s.History[0] : null;
        bool active = s.Pressed.Count > 0
            || s.Left.Magnitude > deadzone || s.Right.Magnitude > deadzone
            || s.LeftTrigger > TriggerActive || s.RightTrigger > TriggerActive
            || !ReferenceEquals(top, _top);
        _top = top;
        if (active || !_seen)
        {
            _lastActiveNs = nowNs;
            _seen = true;
        }
        if (seconds <= 0 || !s.Connected)
            return 1;
        ulong idle = Values.Since(nowNs, _lastActiveNs);
        ulong wait = (ulong)seconds * Values.NsPerSecond;
        if (idle <= wait)
            return 1;
        double t = Math.Min(1.0, (idle - wait) / (double)FadeNs);
        return 1 - (1 - Dim) * t;
    }
}
