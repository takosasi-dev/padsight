// スティックのずれの見張り(仕様書 FR-52・§9.19)。250 ミリ秒ごとの値を受け、触っていない間の値を集めて、60 秒たまったら 1 回だけ判定する。
// 値はメモリだけに持つ。時刻は SDL の ns で受け取り、自分では時計を読まない(INV-13)。
namespace PadSight.Core;

/// <param name="Right">右のスティックなら true</param>
/// <param name="Mean">触っていない間の平均の大きさ(スティック診断の「平均のずれ」と同じ、平均の位置の大きさ)</param>
public sealed record DriftWarning(uint PadId, string PadName, bool Right, double Mean, double Deadzone)
{
    public string Key => $"drift{PadId}{(Right ? "R" : "L")}";
}

public sealed class DriftWatch
{
    public const double TouchMagnitude = 0.25;   // これ以上倒していれば触っている
    public const double StillRange = 0.02;       // 直前 3 秒の各軸の最大と最小の差がこれ以下なら止まっている
    public const ulong WindowNs = 3 * Values.NsPerSecond;
    public const ulong DecideNs = 60 * Values.NsPerSecond;
    /// <summary>1 回で足す時間の上限。タイマーが止まっていた間(スリープ、デモ)を触っていない時間に数えない。</summary>
    public const ulong MaxStepNs = Values.NsPerSecond;

    sealed class Stick
    {
        public readonly Queue<(ulong Ns, float X, float Y)> Recent = new();
        public ulong LastNs, UntouchedNs;
        public double SumX, SumY;
        public int Count;
        public bool Decided;
    }

    readonly Dictionary<(uint Pad, bool Right), Stick> _sticks = [];

    /// <summary>1 台のパッドの今の値を足す。判定が出たときだけ警告を返す(左右で最大 2 つ)。</summary>
    public IReadOnlyList<DriftWarning> Feed(ulong ns, uint padId, string padName, StickValue left, StickValue right, double deadzone)
    {
        List<DriftWarning>? found = null;
        foreach (var (isRight, v) in new[] { (false, left), (true, right) })
            if (Step(ns, padId, isRight, v, deadzone) is double mean)
                (found ??= []).Add(new DriftWarning(padId, padName, isRight, mean, deadzone));
        return found ?? (IReadOnlyList<DriftWarning>)[];
    }

    double? Step(ulong ns, uint padId, bool isRight, StickValue v, double deadzone)
    {
        if (!_sticks.TryGetValue((padId, isRight), out var st))
            _sticks[(padId, isRight)] = st = new Stick();
        if (st.Decided)
            return null;
        bool first = st.Recent.Count == 0;
        ulong step = first || ns <= st.LastNs ? 0 : ns - st.LastNs;
        if (step > MaxStepNs)
        {
            st.Recent.Clear(); // 間が空いたら、前の値で「止まっている」と見ない
            step = 0;
        }
        st.LastNs = ns;
        st.Recent.Enqueue((ns, v.X, v.Y));
        while (st.Recent.Peek().Ns + WindowNs < ns)
            st.Recent.Dequeue();

        if (v.Magnitude >= TouchMagnitude || !Still(st.Recent))
            return null;
        st.UntouchedNs += step;
        st.SumX += v.X;
        st.SumY += v.Y;
        st.Count++;
        if (st.UntouchedNs < DecideNs)
            return null;
        st.Decided = true; // 判定はパッドとスティックごとに 1 回
        double mx = st.SumX / st.Count, my = st.SumY / st.Count;
        double mean = Math.Min(1.0, Math.Sqrt(mx * mx + my * my));
        return mean > deadzone ? mean : null;
    }

    static bool Still(Queue<(ulong Ns, float X, float Y)> recent)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var (_, x, y) in recent)
        {
            minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y); maxY = MathF.Max(maxY, y);
        }
        return maxX - minX <= StillRange && maxY - minY <= StillRange;
    }

    /// <summary>今つながっているパッドだけを残す。外れたパッドは忘れ、その InstanceId を返す。</summary>
    public IReadOnlyList<uint> Retain(IReadOnlyCollection<uint> present)
    {
        var gone = _sticks.Keys.Select(k => k.Pad).Where(p => !present.Contains(p)).Distinct().ToList();
        foreach (var p in gone)
        {
            _sticks.Remove((p, false));
            _sticks.Remove((p, true));
        }
        return gone;
    }
}
