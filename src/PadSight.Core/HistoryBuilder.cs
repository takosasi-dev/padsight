// 入力履歴の行を作る(仕様書 §9.7・D-10)。方向が変わったか、ボタンが新しく押されたときだけ 1 行を足す。
// 行の長さは SDL のイベントの時刻(ns)の差から、フレーム数かミリ秒の文字列に直す。メモリには 200 行だけ持つ。
namespace PadSight.Core;

public sealed class HistoryBuilder
{
    public const int Capacity = 200;

    readonly List<HistoryRow> _rows = []; // 新しい順
    int _dir = 5;
    HashSet<string> _pressed = [];

    public IReadOnlyList<HistoryRow> Rows => _rows;

    /// <summary>状態を受ける。行が増えたら真。</summary>
    public bool Update(ulong ns, int direction, IReadOnlyCollection<string> pressed)
    {
        var newly = pressed.Where(k => !_pressed.Contains(k)).ToList();
        bool changed = direction != _dir || newly.Count > 0;
        if (changed)
        {
            var held = pressed.Where(_pressed.Contains).ToList();
            if (_rows.Count > 0 && _rows[0].EndNs is null)
                _rows[0] = _rows[0] with { EndNs = ns };
            _rows.Insert(0, new HistoryRow(direction, newly, held, ns, null));
            if (_rows.Count > Capacity)
                _rows.RemoveAt(_rows.Count - 1);
        }
        _dir = direction;
        _pressed = [.. pressed];
        return changed;
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
}
