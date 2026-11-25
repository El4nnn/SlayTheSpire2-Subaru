using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 魔兽使·梅莉的护佑：目标在本回合内**免疫所有敌人造成的伤害**。
///
/// <para>伤害侧：<see cref="ModifyDamageMultiplicative"/> 对「敌方来源」直接返回 0
/// （dealer 为 null 的伤害不算敌人效果，放行；同侧来源也放行）。</para>
///
/// <para><b>减益侧没有做</b>：唯一能"在施加前否决"的入口是
/// <c>AbstractModel.TryModifyPowerAmountReceived</c>，但它是 <c>internal virtual</c>
/// （apilist 显示 virtual，外部程序集却无法 override，编译期 CS0115），
/// 所以第三方 mod 拦不住"敌人给你挂减益"。要做到只能走后置：等能力加上去再
/// <c>PowerCmd.Remove</c>（会有一帧可见、也可能触发别人的"施加时"效果），
/// 目前按用户口径先只做伤害免疫，文案也写成"免疫所有敌人造成的伤害"。</para>
///
/// <para>持续时间：能力挂在目标身上，到**下一个玩家回合开始**才移除 —— 这样敌方回合也在保护范围内
/// （敌人正是在敌方回合出手）。不用 <c>AfterSideTurnEnd</c> 是为了避免"敌人还没打就过期"。</para>
/// </summary>
public sealed class BeastTamerGuardPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "meili.png".PowerImagePath();
    public override string CustomBigIconPath => "meili.png".BigPowerImagePath();

    /// <summary>敌人打过来的伤害归零；自己人/无来源的伤害不拦。</summary>
    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        if (target != Owner)
            return 1m;

        if (dealer == null || ReferenceEquals(dealer, target))
            return 1m;

        // 同侧（队友/自己）打过来的不算"敌人的效果"
        if (dealer.Side == target.Side)
            return 1m;

        return 0m;
    }

    /// <summary>下一个玩家回合开始：保护结束。</summary>
    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player)
            return;

        GD.Print($"[Subaru] mp: meili guard expired on {Owner?.Name}");
        await PowerCmd.Remove(this);
    }
}
