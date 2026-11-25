using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 浮空：受到的伤害**固定减半**（×0.5），与层数无关。
///
/// 层数 = <b>还能保几回合</b>，不是「叠多硬」：
///   · 每层 = 1 个回合的 50% 减伤，回合结束掉 1 层，掉到 0 就消失
///   · 所以穆拉克（升级 2 层）= 1 回合 / 2 回合，卡蜜拉同理，柯尔·雷欧尼斯 1 层 = 1 回合
///
/// 掉层的时机放在**敌方回合结束**，不是玩家回合结束：
///   浮空是防御向的，必须盖住敌人这一轮的攻击；玩家回合结束就掉层等于对敌人这次攻击无效
///   （2026-11-17 玩家报过「只在自己回合生效」）。
///
/// 为什么不再按层数做 <c>Math.Pow(0.5, Amount)</c>：
///   旧版是指数减伤（1 层 ×0.5、2 层 ×0.25、4 层 ×1/16），穆拉克又是 1 费普通、无[消耗]，
///   叠起来等于无敌。现在层数只买时间，不买强度（2026-11-17 用户定）。
/// </summary>
public sealed class FloatPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    // Counter：图标上的数字就是「还剩几回合」
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    // 白鸟（取自本体 soar_power 的图案，已作为独立 PNG 放进 mod，不再依赖本体路径）。
    public override string CustomPackedIconPath => "float_power.png".PowerImagePath();

    public override string CustomBigIconPath => "float_power.png".BigPowerImagePath();

    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        // 只看「有没有浮空」，不看几层 —— 固定 50%
        if (target != Owner || Amount <= 0)
            return 1m;

        return 0.5m;
    }

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

        // 每回合掉 1 层（= 消耗掉一个回合的减伤）
        Amount -= 1;
        if (Amount <= 0)
            await PowerCmd.Remove(this);
    }
}
