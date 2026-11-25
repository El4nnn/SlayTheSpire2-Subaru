using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 进化：大灾（尸兵）阵营专属遗物。
///
/// <para>获得方式：在涅奥接受「契约·斯芬克斯」（大灾路线）时直接发放，**不进任何卡池**
/// （见 <see cref="Patches.RewardExclusionPatch"/> 的池子出口过滤）。</para>
///
/// <para>效果：**每场战斗开始时**，获得「最大生命值 ÷ 10」点[格挡]（向下取整）。
/// 和贝蒂的守护是同一类的「阵营赠礼」，只是触发点不同。
/// 走 <see cref="BeforeCombatStart"/>（和 <see cref="ThornCrown"/> 同一个挂钩点），
/// 所以在第一回合抽牌前就已经有盾了。</para>
/// </summary>
public class Evolution : SubaruRelic
{
    /// <summary>只有涅奥契约会发，不进奖励池 / 商店。</summary>
    public override RelicRarity Rarity => RelicRarity.Event;

    /// <summary>每多少点最大生命值换 1 点格挡。</summary>
    public const int HpPerBlock = 10;

    public override async Task BeforeCombatStart()
    {
        try
        {
            var creature = Owner?.Creature;
            if (creature == null)
                return;

            var block = (decimal)(creature.MaxHp / HpPerBlock);
            if (block <= 0)
                return;

            await CreatureCmd.GainBlock(creature, block, ValueProp.Unpowered, null, false);
            GD.Print($"[Subaru] evolution: +{block} block (max hp {creature.MaxHp})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] evolution failed: " + e);
        }
    }
}
