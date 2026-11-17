// 分解能(仕様書 FR-56・§9.20)。ゆっくり小さく動かしている間の倒し具合 |v| の列から、最小の刻み・揺れ・出た値の数を出す。PadLab の ResolutionTest を移した物。
// 最小の刻みは続いた 2 つの差(0 より大きい物)の最小、揺れは差の標準偏差、値の数は 1/4096 に丸めた種類の数。最初の値から seconds 秒で終わる。
namespace PadSight.Core.Diag;

public sealed record ResolutionResult(bool Complete, float MinStep, float NoiseLevel, int DistinctLevels, double ElapsedSeconds)
{
    /// <summary>最小の刻みがこれ以上なら「粗め」(1/256 段ほど。8 ビットの値を広げたパッドの刻み 0.0078 を拾う。PadSight で足した目安)。</summary>
    public const float CoarseStep = 0.004f;

    /// <summary>-1〜1 を最小の刻みで割った段の数の目安。測れていなければ 0。</summary>
    public int StepsEstimate => MinStep > 0 ? (int)Math.Round(2.0 / MinStep) : 0;

    public string Verdict => DistinctLevels < 20 ? "動きが小さすぎて、測れていない可能性があります。もう少し動かしてください"
        : MinStep >= CoarseStep ? $"刻みが粗めの可能性があります(約 {StepsEstimate} 段相当)。ゆっくりした操作がかくつく場合があります"
        : "細かい刻みで値が出ています(目安)";
}

public sealed class ResolutionTest(bool right, double seconds = 5)
{
    readonly List<float> _diffs = [];
    readonly HashSet<int> _levels = [];
    readonly List<(double Sec, float R)> _trace = [];
    bool _running, _complete;
    ulong? _startNs;
    ulong _lastNs;
    float _prev = float.NaN, _minStep = float.MaxValue;

    public bool Running => _running;

    /// <summary>最初の値が届いたか(届くまでは時間を数えない)。</summary>
    public bool Started => _startNs is not null;

    /// <summary>受け取った |v| の列(最初の値からの秒)。図に描く。</summary>
    public IReadOnlyList<(double Sec, float R)> Trace => _trace;

    public void Start()
    {
        Reset();
        _running = true;
    }

    public void Reset()
    {
        _running = _complete = false;
        _diffs.Clear();
        _levels.Clear();
        _trace.Clear();
        _startNs = null;
        _lastNs = 0;
        _prev = float.NaN;
        _minStep = float.MaxValue;
    }

    /// <summary>時間で打ち切る(動かすのをやめて値が届かなくなったとき)。</summary>
    public void Finish()
    {
        if (!_running)
            return;
        _running = false;
        _complete = _startNs is not null;
    }

    public void Feed(DiagSample s) => Feed(s.Ns, right ? s.Right : s.Left);

    public void Feed(ulong ns, StickValue v)
    {
        if (!_running)
            return;
        float r = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        _startNs ??= ns;
        _lastNs = ns;
        double sec = Values.Since(ns, _startNs.Value) / 1e9;
        _trace.Add((sec, r));
        _levels.Add((int)MathF.Round(r * 4096));
        if (!float.IsNaN(_prev))
        {
            float d = r - _prev;
            _diffs.Add(d);
            float ad = MathF.Abs(d);
            if (ad > 0 && ad < _minStep) _minStep = ad;
        }
        _prev = r;
        if (sec >= seconds)
        {
            _running = false;
            _complete = true;
        }
    }

    public ResolutionResult Result
    {
        get
        {
            float noise = 0;
            if (_diffs.Count > 1)
            {
                float mean = _diffs.Average();
                noise = MathF.Sqrt(_diffs.Sum(d => (d - mean) * (d - mean)) / _diffs.Count);
            }
            return new ResolutionResult(_complete, _minStep == float.MaxValue ? 0 : _minStep, noise, _levels.Count,
                _startNs is null ? 0 : Values.Since(_lastNs, _startNs.Value) / 1e9);
        }
    }
}
