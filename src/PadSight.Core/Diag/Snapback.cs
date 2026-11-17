// スナップバック(仕様書 FR-56・§9.20)。倒して離したときに反対側へ振れる大きさと、中央に戻るまでの時間を測る。PadLab の SnapbackTest を移した物。
// |v|>0.8 で「倒し」、その後 |v|<0.15 か倒した向きと逆になった瞬間を「離し」、|v|<0.1 が 50 ミリ秒続いた所までを戻る時間とする。
// SDL は値が変わったときだけ届くので、止まった後は Hold で時刻だけ進める(INV-13: 自分では時計を読まない)。
namespace PadSight.Core.Diag;

public sealed record SnapbackRelease(double ReturnMs, float Overshoot, float ReleaseAngleDeg);

/// <param name="Complete">目標の回数に届いた(または Finish で打ち切った)</param>
public sealed record SnapbackResult(bool Complete, int Releases, int TargetReleases, double MeanReturnMs, double MaxReturnMs, float MaxOvershoot, IReadOnlyList<SnapbackRelease> Samples)
{
    /// <summary>反対側への振れがこれ以上なら「大きめ」(PadLab が中央に戻ったとみなす半径)。</summary>
    public const float OvershootNotable = 0.1f;

    /// <summary>判定の文(断定しない。PadSight で足した目安)。</summary>
    public string Verdict => Releases == 0 ? ""
        : MaxOvershoot >= OvershootNotable ? "離したときに反対側へ大きめに振れています。逆向きの入力が一瞬入る可能性があります(目安)"
        : "反対側への振れは小さめです(目安)";
}

public sealed class SnapbackTest(bool right, int targetReleases = 5)
{
    const float DeflectRadius = 0.8f, ReleaseRadius = 0.15f, SettleRadius = 0.1f;
    const ulong SettleNs = 50 * Values.NsPerMs;
    enum State { Idle, Deflected, Returning }

    readonly List<SnapbackRelease> _samples = [];
    bool _running, _complete;
    State _state;
    float _dirX, _dirY, _releaseAngle, _overshoot;
    float _lastX, _lastY;
    ulong _releaseNs;
    ulong? _settleStartNs;

    public bool Running => _running;

    public void Start()
    {
        Reset();
        _running = true;
    }

    public void Reset()
    {
        _running = _complete = false;
        _state = State.Idle;
        _samples.Clear();
        _settleStartNs = null;
        _overshoot = _lastX = _lastY = 0;
    }

    /// <summary>集めた分で打ち切る(時間切れ)。</summary>
    public void Finish()
    {
        _running = false;
        _complete = _samples.Count > 0;
    }

    public void Feed(DiagSample s) => Feed(s.Ns, right ? s.Right : s.Left);

    /// <summary>最後の値のまま ns まで時間が進んだことにする(値が変わらないと SDL から何も届かないため)。</summary>
    public void Hold(ulong ns) => Feed(ns, _lastX, _lastY);

    public void Feed(ulong ns, StickValue v) => Feed(ns, v.X, v.Y);

    void Feed(ulong ts, float x, float y)
    {
        if (!_running)
            return;
        _lastX = x;
        _lastY = y;
        float r = MathF.Sqrt(x * x + y * y); // PadLab と同じく 1 で切らない
        if (_state == State.Idle)
        {
            if (r > DeflectRadius) { _state = State.Deflected; SetDir(x, y, r); }
            return;
        }
        float dot = x * _dirX + y * _dirY;
        if (_state == State.Deflected)
        {
            if (r > DeflectRadius) { SetDir(x, y, r); return; }
            if (r >= ReleaseRadius && dot >= 0) return;
            _state = State.Returning;
            _releaseNs = ts;
            _overshoot = 0;
            _settleStartNs = null;
        }
        // 戻っている
        if (r > DeflectRadius) { _state = State.Deflected; SetDir(x, y, r); return; }
        if (-dot > _overshoot) _overshoot = -dot;
        if (r >= SettleRadius) { _settleStartNs = null; return; }
        _settleStartNs ??= ts;
        if (Values.Since(ts, _settleStartNs.Value) < SettleNs) return;
        _samples.Add(new SnapbackRelease(Values.Since(_settleStartNs.Value, _releaseNs) / (double)Values.NsPerMs, _overshoot, _releaseAngle));
        _state = State.Idle;
        if (_samples.Count >= targetReleases)
        {
            _running = false;
            _complete = true;
        }
    }

    void SetDir(float x, float y, float r)
    {
        _dirX = x / r;
        _dirY = y / r;
        _releaseAngle = Values.Angle(x, y);
    }

    public SnapbackResult Result
    {
        get
        {
            var s = _samples.ToArray();
            return new SnapbackResult(_complete, s.Length, targetReleases,
                s.Length == 0 ? 0 : s.Average(x => x.ReturnMs),
                s.Length == 0 ? 0 : s.Max(x => x.ReturnMs),
                s.Length == 0 ? 0 : s.Max(x => x.Overshoot), s);
        }
    }
}
