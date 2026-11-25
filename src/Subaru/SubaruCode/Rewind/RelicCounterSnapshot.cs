using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Godot;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 需要随[死归]回档的遗物计数器。
/// 遗物是跨战斗的对象，计数器天然不会被战斗快照覆盖，所以单独开一个接口：
/// 实现了它的遗物，在抓快照时记一次数值，回档时写回去。
/// </summary>
public interface IRewindCounterRelic
{
    /// <summary>当前计数值（回档要还原到这个字段）。</summary>
    int RewindCounter { get; set; }
}

public static class RelicCounterSnapshot
{
    private static readonly Dictionary<string, int> Counters = new();

    public static void Capture(Player player)
    {
        Counters.Clear();
        if (player == null)
            return;

        foreach (var relic in player.Relics.OfType<IRewindCounterRelic>())
        {
            var id = (relic as RelicModel)?.Id.ToString() ?? relic.GetType().Name;
            Counters[id] = relic.RewindCounter;
            GD.Print($"[Subaru] relic counter captured: {id}={relic.RewindCounter}");
        }
    }

    public static void Restore(Player player)
    {
        if (player == null || Counters.Count == 0)
            return;

        foreach (var relic in player.Relics.OfType<IRewindCounterRelic>())
        {
            var id = (relic as RelicModel)?.Id.ToString() ?? relic.GetType().Name;
            if (!Counters.TryGetValue(id, out var value))
                continue;

            relic.RewindCounter = value;
            GD.Print($"[Subaru] relic counter restored: {id}={value}");
        }
    }

    public static void Clear() => Counters.Clear();
}
