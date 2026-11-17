// 入力履歴の行を作る(仕様書 §9.7・D-10)。方向が変わったか、ボタンが新しく押されたときだけ 1 行を足す。
// 同時押しは窓の中なら一番上の行にまとめ(FR-50)、ボタンの行にはコマンドの印を付ける(FR-51・MotionDetector)。
// 行の長さは SDL のイベントの時刻(ns)の差から、フレーム数かミリ秒の文字列に直す。メモリには 200 行だけ持つ。
namespace PadSight.Core;

public sealed class HistoryBuilder
{
    public const int Capacity = 200;

    readonly List<HistoryRow> _rows = []; // 新しい順
    int _dir = 5;
    HashSet<string> _pressed = [];

    public IReadOnlyList<HistoryRow> Rows => _rows;

    /// <summary>同時押しをまとめる窓(ns。FR-50)。一番上の行の始まりからこの時間以内(境目を含む)の押下をまとめる。0 はまとめない。</summary>
    public ulong MergeWindowNs { get; set; }

    /// <summary>ボタンの行にコマンドの印を付けるか(FR-51)。</summary>
    public bool Motions { get; set; }

    /// <summary>merge_frames(60 分の 1 秒が単位)を窓の ns に直す(§9.19。端数は切り捨て)。</summary>
    public static ulong MergeWindowOf(int mergeFrames) => (ulong)Math.Max(0, mergeFrames) * Values.NsPerSecond / 60;

    /// <summary>状態を受ける。行が増えたか、一番上の行にまとめたら真。</summary>
    public bool Update(ulong ns, int direction, IReadOnlyCollection<string> pressed)
    {
        var newly = pressed.Where(k => !_pressed.Contains(k)).ToList();
        bool changed = direction != _dir || newly.Count > 0;
        if (changed && direction == _dir && CanMerge(ns, newly))
        {
            // 行は書き換えずに作り直す(スナップショットと止めた画面が前の行をそのまま持っているため)
            var top = _rows[0];
            _rows[0] = top with { NewlyPressed = [.. top.NewlyPressed, .. newly], MergeGapNs = ns - top.StartNs };
        }
        else if (changed)
        {
            var held = pressed.Where(_pressed.Contains).ToList();
            if (_rows.Count > 0 && _rows[0].EndNs is null)
                _rows[0] = _rows[0] with { EndNs = ns };
            _rows.Insert(0, new HistoryRow(direction, newly, held, ns, null));
            if (Motions && newly.Count > 0)
                _rows[0] = _rows[0] with { Motion = MotionDetector.Detect(_rows) };
            if (_rows.Count > Capacity)
                _rows.RemoveAt(_rows.Count - 1);
        }
        _dir = direction;
        _pressed = [.. pressed];
        return changed;
    }

    // 方向が変わらずに押された物を、ボタンの押下で始まった一番上の行に足せるか。
    // 同じボタンを離して押し直した物は、別の押下として新しい行にする
    bool CanMerge(ulong ns, List<string> newly)
    {
        if (MergeWindowNs == 0 || newly.Count == 0 || _rows.Count == 0)
            return false;
        var top = _rows[0];
        return top.EndNs is null && top.NewlyPressed.Count > 0 && ns >= top.StartNs && ns - top.StartNs <= MergeWindowNs
            && !newly.Any(k => top.NewlyPressed.Contains(k) || top.Held.Contains(k));
    }

    /// <summary>出ている行とメモリの行を消す(FR-21)。今の方向と押している物は覚えたまま。</summary>
    public void Clear() => _rows.Clear();

    /// <summary>行の長さを文字列にする。frameBase が 0 ならミリ秒。</summary>
    public static string FormatLength(ulong lengthNs, int frameBase)
    {
        if (frameBase <= 0)
        {
            ulong ms = (lengthNs + Values.NsPerMs / 2) / Values.NsPerMs;
            return ms > 99_999 ? "99999+" : ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        // round(len × base ÷ 1e9)、0.5 は切り上げ
        ulong frames = (ulong)Math.Floor(lengthNs * (double)frameBase / Values.NsPerSecond + 0.5);
        return frames > 999 ? "999+" : frames.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>まとめた同時押しのずれの文字(FR-50)。数は行の長さと同じ丸め。ミリ秒のときは単位を付けて、フレーム数と見分ける。</summary>
    public static string FormatGap(ulong gapNs, int frameBase) =>
        "ずれ " + FormatLength(gapNs, frameBase) + (frameBase <= 0 ? "ms" : "");
}
