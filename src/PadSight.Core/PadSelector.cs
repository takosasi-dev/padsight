// 表示するパッドを選ぶ(仕様書 FR-4・D-23)。
// 「最後に操作」は、別のパッドが操作され、かつ今のパッドに 2 秒操作が無いときに切り替える。
// 「固定」は、覚えた GUID のパッドのうち先につながった物を選び、無ければ未接続のまま。
namespace PadSight.Core;

public sealed class PadSelector
{
    const ulong SwitchAfter = 2 * Values.NsPerSecond;

    public string Mode { get; set; } = "last_used";
    public string? PinnedGuid { get; set; }
    public uint? Selected { get; private set; }

    public void OnOperation(PadState from, ulong ns, IReadOnlyDictionary<uint, PadState> pads)
    {
        if (Mode == "pinned" || Selected == from.Info.InstanceId)
            return;
        if (Selected is not uint cur || !pads.TryGetValue(cur, out var curState)
            || !curState.HasOperated || ns - curState.LastOperationNs >= SwitchAfter)
            Selected = from.Info.InstanceId;
    }

    /// <summary>つながった・外れた・設定が変わったときに選び直す。</summary>
    public void Reselect(IReadOnlyDictionary<uint, PadState> pads)
    {
        if (Mode == "pinned")
        {
            Selected = pads.Values.Where(p => p.Info.Guid == PinnedGuid).OrderBy(p => p.Order).Select(p => (uint?)p.Info.InstanceId).FirstOrDefault();
            return;
        }
        if (Selected is uint cur && pads.ContainsKey(cur))
            return;
        Selected = pads.Values
            .OrderByDescending(p => p.HasOperated).ThenByDescending(p => p.LastOperationNs).ThenBy(p => p.Order)
            .Select(p => (uint?)p.Info.InstanceId).FirstOrDefault();
    }

    /// <summary>ホームの一覧で行を選んだとき、パネルの表示を変えずに「固定」にする。</summary>
    public void Pin(string guid, IReadOnlyDictionary<uint, PadState> pads)
    {
        Mode = "pinned";
        PinnedGuid = guid;
        Reselect(pads);
    }
}
