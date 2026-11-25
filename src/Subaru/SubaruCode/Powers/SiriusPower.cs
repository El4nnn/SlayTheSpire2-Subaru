using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 愤怒司教·洗魂：权能「感觉共有」——强迫他人共有自己的感觉，连死亡都一起死。
/// 每当 1 个敌人受到伤害，其他敌人也受到等量的一部分。
/// 用 static 标志防重入：反弹的伤害会再次触发本钩子，不挡就是无限递归。
/// </summary>
public sealed class SiriusPower : CustomPowerModel, ICorrodedPower
{
    private static bool _reacting;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "sirius_power.png".PowerImagePath();
    public override string CustomBigIconPath => "sirius_power.png".BigPowerImagePath();

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature dealer,
        CardModel cardSource)
    {
        if (_reacting)
            return;

        var owner = Owner;
        if (owner == null || target == owner)
            return;

        if (result.TotalDamage <= 0)
            return;

        var state = owner.CombatState;
        if (state == null)
            return;

        var others = state.HittableEnemies.Where(e => e != target).ToList();
        if (others.Count == 0 || Amount <= 0)
            return;

        // 伤害吃到玩家的力量：引擎不会给「非卡牌伤害」自动加力量，这里手动算
        var strength = owner.GetPowerAmount<StrengthPower>();
        var damage = (decimal)Amount + strength;
        if (damage < 0)
            damage = 0;

        _reacting = true;
        try
        {
            await CreatureCmd.Damage(
                choiceContext, others, damage, ValueProp.Move, owner);
            GD.Print($"[Subaru] sirius: soul wash {damage} (base {Amount} + str {strength}) to {others.Count}");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] sirius failed: " + e);
        }
        finally
        {
            _reacting = false;
        }
    }
}
