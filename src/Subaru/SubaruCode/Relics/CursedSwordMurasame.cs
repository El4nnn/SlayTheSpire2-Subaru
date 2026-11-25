using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 邪剑·村雨：稀有遗物。
/// 1) 每当有敌人死亡，对其他敌人造成 10 点伤害；
/// 2) 剥夺复活：[幻影]（寄生惧魔）与[接续]（残杀千足虫）一律移除；
///    实验体的复活由 <see cref="MurasameRevivePatch"/> 处理（复活时受 50% 最大生命伤害）。
/// </summary>
public class CursedSwordMurasame : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    private const decimal DeathDamage = 10m;

    /// <summary>敌人死亡时的连锁伤害。</summary>
#if GAME_BETA
    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature target,
        CardModel source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        await ChainDamageAsync(choiceContext, target);
    }
#else
    // 0.107 的钩子签名少了 CardModel source
    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature target,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        await ChainDamageAsync(choiceContext, target);
    }
#endif

    private async Task ChainDamageAsync(PlayerChoiceContext choiceContext, Creature target)
    {
        if (target == null || !target.IsEnemy)
            return;

        var state = target.CombatState;
        if (state == null)
            return;

        // 死掉的那只已经不在 HittableEnemies 里，剩下的都是「其他敌人」
        foreach (var enemy in state.HittableEnemies.ToList())
        {
            // Unpowered：固定 10 点，不吃任何一方的力量
            await CreatureCmd.Damage(
                choiceContext, enemy, DeathDamage,
                ValueProp.Move | ValueProp.Unpowered, target);
        }
    }

    /// <summary>
    /// 剥夺复活不再靠「每回合扫一遍摘 power」——那样会让寄生惧魔的动画回调
    /// <c>GetPower&lt;IllusionPower&gt;().IsReviving</c> 拿到 null 而崩掉整场战斗。
    /// 现在改成拦复活：见 <see cref="Subaru.SubaruCode.Patches.MurasameIllusionRevivePatch"/>。
    /// </summary>
}
