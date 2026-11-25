using System;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 大灾形态（能力）：每打出一张[尸兵]，给予所有敌人 <c>Amount</c> 层[中毒]。
///
/// 出牌判定由能力自己收 <c>AfterCardPlayed</c>（模型监听者，不需要 Harmony）——
/// 和 <c>SkillPlayCountPatch</c> 挂在同一个 hook 上，但那边是给阿尔·纱幕统计技能数。
/// 重复打出会叠加 <c>Amount</c>（<see cref="PowerStackType.Counter"/>），所以打两张就是每张尸兵 14 层。
/// </summary>
public sealed class CalamityFormPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "calamity_form_power.png".PowerImagePath();

    public override string CustomBigIconPath => "calamity_form_power.png".BigPowerImagePath();

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            var state = owner?.CombatState;
            var card = cardPlay?.Card;

            if (owner == null || state == null || card == null)
                return;

            if (!card.CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier))
                return;

            // 只认这个能力持有者自己打出的牌
            if (!ReferenceEquals(card.Owner, owner.Player))
                return;

            var enemies = state.HittableEnemies.ToList();
            foreach (var enemy in enemies)
            {
                await PowerCmd.Apply<PoisonPower>(
                    choiceContext, enemy, Amount, owner, (CardModel?)null, false);
            }

            GD.Print($"[Subaru] calamity form: {card.Id} => poison {Amount} to {enemies.Count} enemy(ies)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] calamity form failed: " + e);
        }
    }
}
