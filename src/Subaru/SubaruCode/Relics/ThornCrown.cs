using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 荆棘王冠：事件「爱丽丝与荆棘之王」专属遗物（<see cref="RelicRarity.Event"/>，不进随机奖励池）。
///
/// 战斗开始时获得 3 层[荆棘]；每次死归回档后再长 2 层（结算点见
/// <see cref="Rewind.DeathReturnSpike"/>，和「傲慢因子」的死归奖励放在一起）。
/// 回档会把能力还原成存档点里的层数，所以那里只加「这一次死归新长出来的」部分。
///
/// <para><b>2026-11-19 增强</b>：玩家回合结束时，对所有敌人造成等同于当前[荆棘]层数的伤害
/// （<c>Move | Unpowered</c>：算遗物伤害、不吃力量，但吃敌人的格挡/易伤）。</para>
/// </summary>
public class ThornCrown : SubaruRelic
{
    /// <summary>战斗开始时的荆棘层数。</summary>
    public const int ThornsOnCombatStart = 3;

    /// <summary>每次死归后额外获得的荆棘层数。</summary>
    public const int ThornsPerRewind = 2;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override async Task BeforeCombatStart()
    {
        var owner = Owner?.Creature;
        if (owner == null)
            return;

        await PowerCmd.Apply<ThornsPower>(
            new BlockingPlayerChoiceContext(), owner, ThornsOnCombatStart, owner, null, false);

        GD.Print($"[Subaru] thorn crown: +{ThornsOnCombatStart} thorns");
    }

    /// <summary>玩家回合结束：对所有敌人打出「荆棘层数」点伤害。</summary>
    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player)
                return;

            var owner = Owner?.Creature;
            if (owner == null || participants == null || !participants.Contains(owner))
                return;

            var thorns = owner.GetPowerAmount<ThornsPower>();
            var enemies = owner.CombatState?.HittableEnemies?.ToList();

            // 遗物伤害：Move | Unpowered —— 不吃玩家的力量，但仍然走正常伤害管线（格挡/易伤照算）
            if (thorns > 0m && enemies != null && enemies.Count > 0)
            {
                await CreatureCmd.Damage(
                    choiceContext, enemies, thorns, ValueProp.Move | ValueProp.Unpowered, owner);

                GD.Print($"[Subaru] thorn crown: end of turn {thorns} to {enemies.Count} enemies");
            }

            // 多人：没有死归可以叠荆棘，按用户定的方案改成「每回合结束 +1」。
            // 放在伤害之后：本回合那份伤害用的仍然是回合内的层数，新长出来的下一回合才生效。
            if (MultiplayerSupport.IsMultiplayer)
            {
                await PowerCmd.Apply<ThornsPower>(choiceContext, owner, 1m, owner, null, false);
                GD.Print("[Subaru] mp: thorn crown +1 thorns at turn end");
            }
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] thorn crown end-of-turn failed: " + e);
        }
    }
}
