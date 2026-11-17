// 応答カーブ(仕様書 FR-56・§9.20)。中央からゆっくり一定に倒したときの倒し具合 |v| を時間に対して並べ、飛び・戻り・直線からのずれを出す。PadLab の LinearityTest を移した物。
// |v|>0.05 で記録を始め、|v|>0.95 で終わる。続いた 2 つの差が 0.12 を超えたら飛び、0.01 を超えて減ったら戻り。直線からのずれは PadSight で足した(最初と最後を結んだ線から最も離れた量)。
using System.Globalization;
using System.Numerics;

namespace PadSight.Core.Diag;

/// <param name="Curve">X は記録の始めから終わりを 0〜1 にした時間、Y は |v|</param>
/// <param name="MaxDeviation">最初と最後の点を結んだ直線から、Y の向きに最も離れた量</param>
public sealed record LinearityResult(bool Complete, IReadOnlyList<Vector2> Curve, int JumpCount, int ReversalCount, float MaxJump, float MaxDeviation)
{
    /// <summary>判定の文(断定しない)。飛びと戻りの数え方は PadLab の物、文は PadSight で足した目安。</summary>
    public string Verdict
    {
        get
        {
            if (Curve.Count < 2)
                return "";
            var parts = new List<string>();
            if (JumpCount > 0) parts.Add($"途中で値が飛んでいる所が {JumpCount} か所あります。センサーの汚れやすり減りの可能性があります");
            if (ReversalCount > 0) parts.Add($"倒し進めているのに値が戻った所が {ReversalCount} か所あります(倒し方の揺れの場合もあります)");
            if (Curve[^1].Y <= LinearityTest.EndRadius) parts.Add($"最後まで倒しても {Curve.Max(p => p.Y).ToString("0.00", CultureInfo.InvariantCulture)} までしか届きませんでした");
            return parts.Count == 0 ? "飛びや戻りは見られません(目安)" : string.Join("。", parts);
        }
    }
}

public sealed class LinearityTest(bool right, float jumpThreshold = 0.12f)
{
    public const float StartRadius = 0.05f, EndRadius = 0.95f, NoiseFloor = 0.01f;
    readonly List<(ulong Ns, float R)> _curve = [];
    bool _running, _complete;
    int _jumps, _reversals;
    float _maxJump;

    public bool Running => _running;

    public void Start()
    {
        Reset();
        _running = true;
    }

    public void Reset()
    {
        _running = _complete = false;
        _curve.Clear();
        _jumps = _reversals = 0;
        _maxJump = 0;
    }

    /// <summary>時間切れで打ち切る(奥まで届かないスティックもあるため)。</summary>
    public void Finish()
    {
        _running = false;
        _complete = _curve.Count > 1;
    }

    public void Feed(DiagSample s) => Feed(s.Ns, right ? s.Right : s.Left);

    public void Feed(ulong ns, StickValue v)
    {
        if (!_running)
            return;
        float r = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        if (_curve.Count == 0)
        {
            if (r > StartRadius) _curve.Add((ns, r));
            return;
        }
        float d = r - _curve[^1].R;
        _curve.Add((ns, r));
        if (d > jumpThreshold) _jumps++;
        if (d > _maxJump) _maxJump = d;
        if (d < -NoiseFloor) _reversals++;
        if (r > EndRadius)
        {
            _running = false;
            _complete = true;
        }
    }

    public LinearityResult Result
    {
        get
        {
            var curve = new Vector2[_curve.Count];
            float dev = 0;
            if (curve.Length > 0)
            {
                ulong t0 = _curve[0].Ns, span = Values.Since(_curve[^1].Ns, t0);
                for (int i = 0; i < curve.Length; i++)
                    curve[i] = new Vector2(span == 0 ? 0 : (float)(Values.Since(_curve[i].Ns, t0) / (double)span), _curve[i].R);
                Vector2 a = curve[0], b = curve[^1];
                foreach (var p in curve)
                    dev = MathF.Max(dev, MathF.Abs(p.Y - (a.Y + (b.Y - a.Y) * p.X)));
            }
            return new LinearityResult(_complete, curve, _jumps, _reversals, _maxJump, dev);
        }
    }
}
