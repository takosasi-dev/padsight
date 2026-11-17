// 入力履歴の方向の行の並びから、ボタンの行に付けるコマンド(236・623 など)を見分ける(仕様書 FR-51・§9.19)。
// 同じ方向が続いた物は 1 つにまとめ、ボタンの行の始まりから 20/60 秒より前に始まった物は見ない。長いコマンドを先に見る。
// 結果は行に持たせて画面に出すだけで、保存もログもしない(INV-8)。
namespace PadSight.Core;

public static class MotionDetector
{
    /// <summary>コマンドを探す範囲(60 分の 1 秒が単位。§9.19 で固定)。</summary>
    public const int WindowFrames = 20;
    public const ulong WindowNs = WindowFrames * Values.NsPerSecond / 60;

    // 先に見る物から並べる。同じ段で両方が含まれるときは、最後の方向の行が新しい方を採る(同じなら先に書いた方)
    static readonly (string Name, int[] Seq)[][] Tiers =
    [
        [("41236", [4, 1, 2, 3, 6]), ("63214", [6, 3, 2, 1, 4])],
        [("623", [6, 2, 3]), ("421", [4, 2, 1])],
        [("236", [2, 3, 6]), ("214", [2, 1, 4])],
        [("22", [2, 5, 2])], // 下・中立・下。5 を飛ばさずに見る
    ];

    /// <summary>rows は新しい順で、rows[0] がボタンを押した行。成立した物の名前、無ければ null。</summary>
    public static string? Detect(IReadOnlyList<HistoryRow> rows)
    {
        if (rows.Count == 0)
            return null;
        ulong from = rows[0].StartNs >= WindowNs ? rows[0].StartNs - WindowNs : 0;
        // 同じ方向が続いた物を 1 つにまとめ(始まりは一番古い行)、古い順に並べる
        var runs = new List<(int Dir, ulong Start)>();
        foreach (var r in rows)
        {
            if (runs.Count > 0 && runs[^1].Dir == r.Direction)
                runs[^1] = (r.Direction, r.StartNs);
            else
                runs.Add((r.Direction, r.StartNs));
        }
        runs.Reverse();
        // 始まりが範囲の前の物は使わない。古い順なので、範囲に入った所から後ろが全部使える
        int first = runs.FindIndex(x => x.Start >= from);
        if (first < 0)
            return null;
        var withNeutral = runs.Skip(first).Select(x => x.Dir).ToList();
        var noNeutral = withNeutral.Where(d => d != 5).ToList();
        foreach (var tier in Tiers)
        {
            string? best = null;
            int bestEnd = -1;
            foreach (var (name, seq) in tier)
            {
                int end = MatchEnd(seq.Contains(5) ? withNeutral : noNeutral, seq);
                if (end > bestEnd)
                    (best, bestEnd) = (name, end);
            }
            if (best is not null)
                return best;
        }
        return null;
    }

    /// <summary>seq が dirs に順に(間に別の物があってよい)含まれていれば、最後の要素に当たった位置。無ければ -1。
    /// 最後の位置はできるだけ早い物を返す(同じ段の比べ合いで、より新しく終わった方を選ぶため)。</summary>
    static int MatchEnd(List<int> dirs, int[] seq)
    {
        int k = 0;
        for (int i = 0; i < dirs.Count; i++)
        {
            if (dirs[i] == seq[k] && ++k == seq.Length)
                return i;
        }
        return -1;
    }
}
