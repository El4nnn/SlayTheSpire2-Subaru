using System.Collections.Generic;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 「本回合内造成的伤害提高 X%」——出伤侧倍率（dealer == Owner），
/// 和浮空/龙鳞那种「受到伤害」的倍率方向相反。到期用玩家回合结束是对的（进攻向）。
///
/// <para>
/// <b>倍率存在 <c>Amount</c> 里，不是实例字段。</b>
/// 之前是 <c>Multiplier</c> 实例字段 + 卡面自己算好倍数写进去，问题是：
/// 死归回档时 BattleSnapshot 只恢复 Amount（<c>live.Amount = saved.Amount</c>），
/// 实例字段一律丢失 → 回档后加成直接归 1（赫克托白打）。
/// </para>
/// <para>
/// 现在的约定：<c>Amount</c> = **累计提高的百分点**。
/// 打一张 30% 的赫克托 → Amount 30（倍率 1.30）；再打一张升级版 50% → 80（倍率 1.80）。
/// 线性叠加，和卡面「本回合造成的伤害提高 30%」一致；图标上的数字也就是当前总百分比。
/// </para>
/// </summary>
public sealed class DamageUpPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "damage_up_power.png".PowerImagePath();

    public override string CustomBigIconPath => "damage_up_power.png".BigPowerImagePath();

    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        if (dealer != Owner)
            return 1m;

        // Amount 是百分点：30 → ×1.30
        return 1m + Amount / 100m;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        if (side != CombatSide.Player)
            return;

        var owner = Owner;
        if (owner == null)
            return;

        await PowerCmd.Remove(this);
    }
}
