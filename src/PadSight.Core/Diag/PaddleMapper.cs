// パドルの確認(仕様書 FR-55・§9.20)。「パドル n だけを押してください」→ 最初の押下から 100 ミリ秒の間に押されたボタンを、そのパドルの届き先として残す。PadLab の PaddleMapper を移した。
// DiagSample はイベントのときしか来ないので、画面は描画のたびに Tick(今の時刻)を呼び、10 秒の待ちと 100 ミリ秒の窓を閉じる。
// SDL の Paddle1〜4 は Mapped に入れず PhysicalSeen に立てる(PadLab の物理ビットと同じ扱い)。Arrived には届いた順に全部入る。
namespace PadSight.Core.Diag;

public enum PaddlePhase { Idle, Waiting, Captured, NoResponse, Invalid, Done }

/// <param name="Arrived">届いたボタン(届いた順)。空なら反応なし</param>
public sealed record PaddleMapping(int PaddleIndex, IReadOnlyList<PadButton> Arrived)
{
    /// <summary>パドル(Paddle1〜4)でないボタン。PadLab の「割り当て」。</summary>
    public IReadOnlyList<PadButton> Mapped => [.. Arrived.Where(b => !PaddleMapper.IsPaddle(b))];

    /// <summary>SDL のパドル(Paddle1〜4)として届いた物があるか。</summary>
    public bool PhysicalSeen => Arrived.Any(PaddleMapper.IsPaddle);
}

public sealed record PaddleState(PaddlePhase Phase, int CurrentPaddle, IReadOnlyList<PaddleMapping> Mappings, double WaitedSeconds);

public sealed class PaddleMapper(int paddleCount, double timeoutSeconds = 10, double captureWindowMs = 100)
{
    readonly ulong _timeoutNs = (ulong)(timeoutSeconds * 1e9);
    readonly ulong _windowNs = (ulong)(captureWindowMs * Values.NsPerMs);
    readonly SortedDictionary<int, PaddleMapping> _mappings = [];
    readonly List<PadButton> _arrived = [];
    PaddlePhase _phase;
    int _current;
    ulong? _waitStartNs, _captureStartNs;
    ulong _lastNs;
    uint _prevBits;

    public int PaddleCount => paddleCount;

    public static bool IsPaddle(PadButton b) => b is >= PadButton.Paddle1 and <= PadButton.Paddle4;

    public void Begin()
    {
        _mappings.Clear();
        _current = 1;
        BeginWait();
    }

    public void Next()
    {
        if (_phase is not (PaddlePhase.Captured or PaddlePhase.NoResponse or PaddlePhase.Invalid))
            return;
        _mappings.TryAdd(_current, new PaddleMapping(_current, []));
        if (_current >= paddleCount)
        {
            _phase = PaddlePhase.Done;
            return;
        }
        _current++;
        BeginWait();
    }

    public void Retry()
    {
        if (_phase is PaddlePhase.Idle or PaddlePhase.Done)
            return;
        _mappings.Remove(_current);
        BeginWait();
    }

    /// <summary>Begin・Next・Retry の直後は、押しっぱなしの物も新しく押した物として受ける(PadLab と同じ)。</summary>
    void BeginWait()
    {
        _phase = PaddlePhase.Waiting;
        _waitStartNs = _captureStartNs = null;
        _arrived.Clear();
        _prevBits = 0;
    }

    public void Feed(DiagSample s) => Feed(s.Ns, s.Buttons);

    /// <summary>新しい入力が無くても時間を進める(描画のたびに今の時刻で呼ぶ)。</summary>
    public void Tick(ulong nowNs) => Feed(nowNs, _prevBits);

    public void Feed(ulong ns, uint buttons)
    {
        if (_phase != PaddlePhase.Waiting)
            return;
        ns = Math.Max(ns, _lastNs); // 時刻が戻ったら止めておく(ulong の引き算が回らないように)
        _waitStartNs ??= ns;
        _lastNs = ns;
        uint rising = buttons & ~_prevBits;
        _prevBits = buttons;

        if (_captureStartNs is not ulong cap)
        {
            if (rising == 0)
            {
                if (ns - _waitStartNs.Value >= _timeoutNs) Finish(PaddlePhase.NoResponse);
                return;
            }
            _captureStartNs = ns;
        }
        else if (ns - cap > _windowNs)
        {
            Finish(PaddlePhase.Captured);
            return;
        }

        for (int bit = 0; bit < 32; bit++)
        {
            if ((rising & (1u << bit)) == 0)
                continue;
            var id = (PadButton)bit;
            if (IsPaddle(id))
            {
                if (!_arrived.Contains(id)) _arrived.Add(id);
                continue;
            }
            if (_arrived.Contains(id))
            {
                Finish(PaddlePhase.Invalid); // 同じボタンが 2 回(パドル 1 本の 1 回の押下ではない)
                return;
            }
            _arrived.Add(id);
        }
        if (_arrived.Count(b => b < PadButton.Misc1) >= 3)
            Finish(PaddlePhase.Invalid); // 標準のボタンが 3 つ以上(ほかのボタンに触っている)
    }

    void Finish(PaddlePhase phase)
    {
        _phase = phase;
        if (phase != PaddlePhase.Invalid)
            _mappings[_current] = new PaddleMapping(_current, [.. _arrived]);
    }

    public void Reset()
    {
        BeginWait();
        _phase = PaddlePhase.Idle;
        _mappings.Clear();
        _current = 0;
        _lastNs = 0;
    }

    public PaddleState State => new(_phase, _current, [.. _mappings.Values],
        _waitStartNs is ulong w ? (_lastNs - w) / 1e9 : 0);
}
