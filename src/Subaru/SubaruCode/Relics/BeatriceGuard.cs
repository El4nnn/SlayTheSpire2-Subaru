using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 贝蒂的守护：阴魔法阵营专属遗物。
///
/// 获得方式：在涅奥接受「契约·贝蒂」（阴魔法路线）时直接发放，
/// **不进任何卡池**（<see cref="Patches.RewardExclusionPatch"/> 里把池子出口过滤掉了）。
///
/// 效果：每次死归后，立即获得「[魔女遗香]层数 ÷ 10」点格挡（向下取整）。
/// 结算点在 <c>DeathReturnSpike.RestoreToSavepointAsync</c> 的「还原之后」——
/// 放在还原之前会被快照里的格挡值直接盖掉。
///
/// <para><b>2026-11-19 多人版</b>：多人没有死归，这条效果永远不触发，
/// 按用户定的方案改成「<b>每个玩家回合开始都发放一次</b>」，公式不变（遗香 ÷ 10）。
/// 单机仍然走死归那条路，两者互斥（这里只在 <see cref="MultiplayerSupport.IsMultiplayer"/> 时生效）。</para>
///
/// 图标暂时用占位符（沿用阳剑的图），等美术稿出来再换。
/// </summary>
public class BeatriceGuard : SubaruRelic
{
    /// <summary>只有涅奥契约会发，不进奖励池/商店。</summary>
    public override RelicRarity Rarity => RelicRarity.Event;

    /// <summary>每多少层[魔女遗香]换 1 点格挡。</summary>
    public const int FragrancePerBlock = 10;

    /// <summary>多人：每个玩家回合开始给一次「遗香 ÷ 10」格挡。</summary>
    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (!MultiplayerSupport.IsMultiplayer)
            return; // 单机走死归后那条路（见 DeathReturnSpike.RestoreToSavepointAsync）

        if (side != CombatSide.Player)
            return;

        try
        {
            var creature = Owner?.Creature;
            if (creature == null)
                return;

            var fragrance = creature.Powers
                .FirstOrDefault(p => p is WitchFragrancePower)?.Amount ?? 0m;

            var block = fragrance / FragrancePerBlock;
            if (block <= 0)
                return;

            await CreatureCmd.GainBlock(creature, block, ValueProp.Unpowered, null, false);
            GD.Print($"[Subaru] mp: beatrice guard +{block} block (fragrance {fragrance})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] mp beatrice guard failed: " + e);
        }
    }
}
