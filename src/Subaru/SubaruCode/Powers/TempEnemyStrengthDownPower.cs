using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 敌人的「本回合」力量削减（食精灵者）。
///
/// 和 TempStrengthPower 是一对镜像：
///   临时力量 —— 先给玩家 +N 力量，玩家回合结束时扣回去
///   这个     —— 先给敌人 -N 力量，**敌人回合结束**时补回去
///
/// 为什么不是玩家回合结束就还：敌人是在玩家之后才行动的，
/// 玩家回合结束就还原，等于这张牌对敌人这轮的伤害毫无影响。
/// </summary>
public sealed class TempEnemyStrengthDownPower : CustomPowerModel
{
    private const string BaseGameIcon = "res://images/powers/strength_power.png";

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    /// <summary>
    /// 纯记账，不显示图标：真正扣掉的力量已经以「力量 -3」显示在敌人身上，
    /// 再挂一个「力量削减 3」只会让玩家以为被扣了两次。
    /// PowerModel.IsVisible 不是 virtual，能改的是 protected 的 IsVisibleInternal。
    /// </summary>
    protected override bool IsVisibleInternal => false;

    public override string CustomPackedIconPath =>
        Godot.ResourceLoader.Exists(BaseGameIcon) ? BaseGameIcon : "damage_up_power.png".PowerImagePath();

    public override string CustomBigIconPath =>
        Godot.ResourceLoader.Exists(BaseGameIcon) ? BaseGameIcon : "damage_up_power.png".BigPowerImagePath();

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        if (side != CombatSide.Enemy)
            return;

        var owner = Owner;
        if (owner == null)
            return;

        // 施加时是 -Amount，这里按 +Amount 补回去
        await PowerCmd.Apply<StrengthPower>(choiceContext, owner, Amount, owner, null, false);
        await PowerCmd.Remove(this);
    }
}
