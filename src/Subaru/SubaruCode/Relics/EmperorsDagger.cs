using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 帝王匕首：帝国军（文森特）阵营专属遗物。
///
/// <para>获得方式：在涅奥接受「契约·文森特」（帝国军路线）时直接发放，**不进任何遗物池**
/// （见 <see cref="Patches.RewardExclusionPatch"/> 的池子出口过滤）。</para>
///
/// <para>效果：**每个玩家回合开始时**获得 2 点[活力]（<see cref="VigorPower"/>）。
/// 挂钩点和 <see cref="BeatriceGuard"/> 一样用 <see cref="AfterSideTurnStart"/>，
/// 只是那边是多人补偿、这边是常驻。</para>
/// </summary>
public class EmperorsDagger : SubaruRelic
{
    /// <summary>只有涅奥契约会发，不进奖励池 / 商店。</summary>
    public override RelicRarity Rarity => RelicRarity.Event;

    /// <summary>每回合开始时获得的活力层数。</summary>
    public const int VigorPerTurn = 2;

    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player)
            return;

        try
        {
            var creature = Owner?.Creature;
            if (creature == null)
                return;

            await PowerCmd.Apply<VigorPower>(
                new BlockingPlayerChoiceContext(), creature, VigorPerTurn, creature, null, false);

            GD.Print($"[Subaru] emperors dagger: +{VigorPerTurn} vigor (turn start)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] emperors dagger failed: " + e);
        }
    }
}
