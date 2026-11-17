// ボタンの網羅(仕様書 FR-55・§9.20)。機種のプロファイルの全部のボタンについて、押して離したかを DiagSample から見る。PadLab の ButtonCoverage を移した。
// トリガーは、押したとみなす値以上で押した扱い(PadStateMachine と同じ)。プロファイルに無いボタンが届いたら「未登録」として 1 回だけ足す(PadLab と同じ)。
// 「拡張」は数に入れるが、無くても完了にする(PadProfiles の決まり。PadLab は拡張も要った)。パドルは PaddleMapper が見るので、ここでは足さない。
namespace PadSight.Core.Diag;

/// <summary>まだ・押している(離すのを待っている)・押して離した。</summary>
public enum CoverageMark { NotYet, Held, Done }

/// <param name="Key">PadButton の名前か "LeftTrigger"・"RightTrigger"</param>
/// <param name="Label">プロファイルの名前。未登録の物は Key のまま(画面で表記を決める)</param>
public sealed record CoverageItem(string Key, string Label, bool Extended, bool Unregistered, CoverageMark Mark);

/// <param name="Remaining">拡張でない物のうち、まだ押して離していない数</param>
/// <param name="AllDone">Remaining が 0(「全部のボタンが反応しました」)</param>
public sealed record CoverageState(IReadOnlyList<CoverageItem> Items, int Done, int Remaining, bool AllDone);

public sealed class ButtonCoverage
{
    // PadButton は 0〜20。トリガーは空いている 21・22 のビットに置く
    const int LeftTriggerBit = 21, RightTriggerBit = 22;

    sealed class Item(string key, string label, bool ext, bool unreg, int bit)
    {
        public readonly string Key = key, Label = label;
        public readonly bool Extended = ext, Unregistered = unreg;
        public readonly int Bit = bit;
        public CoverageMark Mark;
    }

    readonly PadProfile _profile;
    readonly float _threshold;
    readonly List<Item> _items = [];

    /// <param name="triggerThreshold">トリガーを押したとみなす値(設定の trigger_threshold)</param>
    public ButtonCoverage(PadProfile profile, float triggerThreshold)
    {
        _profile = profile;
        _threshold = triggerThreshold;
        Reset();
    }

    public void Reset()
    {
        _items.Clear();
        foreach (var b in _profile.Buttons)
            _items.Add(new Item(b.Key, b.Label, b.Extended, false, BitOf(b.Key)));
    }

    public void Feed(DiagSample s)
    {
        uint m = s.Buttons;
        if (s.LeftTrigger >= _threshold) m |= 1u << LeftTriggerBit;
        if (s.RightTrigger >= _threshold) m |= 1u << RightTriggerBit;

        for (int bit = 0; bit <= (int)PadButton.Touchpad; bit++)
        {
            if ((m & (1u << bit)) == 0 || _items.Exists(i => i.Bit == bit))
                continue;
            var b = (PadButton)bit;
            if (b is >= PadButton.Paddle1 and <= PadButton.Paddle4 && b - PadButton.Paddle1 < _profile.PaddleCount)
                continue; // 機種のパドルは「パドルの確認」で見る
            _items.Add(new Item(b.ToString(), b.ToString(), true, true, bit));
        }

        foreach (var i in _items)
        {
            bool down = (m & (1u << i.Bit)) != 0;
            if (down && i.Mark == CoverageMark.NotYet) i.Mark = CoverageMark.Held;
            else if (!down && i.Mark == CoverageMark.Held) i.Mark = CoverageMark.Done;
        }
    }

    public CoverageState State
    {
        get
        {
            var items = _items.Select(i => new CoverageItem(i.Key, i.Label, i.Extended, i.Unregistered, i.Mark)).ToList();
            int remaining = items.Count(i => !i.Extended && i.Mark != CoverageMark.Done);
            return new CoverageState(items, items.Count(i => i.Mark == CoverageMark.Done), remaining, remaining == 0);
        }
    }

    static int BitOf(string key) => key switch
    {
        "LeftTrigger" => LeftTriggerBit,
        "RightTrigger" => RightTriggerBit,
        _ => Enum.TryParse<PadButton>(key, out var b) ? (int)b : throw new ArgumentException("プロファイルのボタンの名前が PadButton にありません: " + key),
    };
}
