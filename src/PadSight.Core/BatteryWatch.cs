// FR-64: 電池が少ないと知らせる(仕様書 §9.21)。20% と 10% を切ったら 1 回ずつ。充電・25% 以上・外れたパッドで忘れる。
namespace PadSight.Core;

public sealed class BatteryWatch
{
    static readonly int[] Levels = [20, 10];
    const int Rearm = 25;

    /// <summary>InstanceId → 知らせた一番低い段(20 か 10)。</summary>
    readonly Dictionary<uint, int> _told = [];

    /// <summary>今の電池の写しを見て、新しく知らせる (パッド, 割合) を返す。</summary>
    public List<(uint Id, int Percent)> Feed(IReadOnlyDictionary<uint, PadPower> power)
    {
        foreach (var id in _told.Keys.Where(id => !power.ContainsKey(id)).ToList())
            _told.Remove(id);
        var list = new List<(uint, int)>();
        foreach (var (id, p) in power)
        {
            if (p.Power != "OnBattery" || p.Percent < 0 || p.Percent >= Rearm)
            {
                if (p.Power is "Charging" or "Charged" || p.Percent >= Rearm)
                    _told.Remove(id);
                continue;
            }
            int level = Levels.Where(l => p.Percent <= l).DefaultIfEmpty(0).Min();
            if (level == 0 || (_told.TryGetValue(id, out int told) && told <= level))
                continue;
            _told[id] = level;
            list.Add((id, p.Percent));
        }
        return list;
    }
}
