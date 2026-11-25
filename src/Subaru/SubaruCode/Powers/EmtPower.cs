using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// E·M·T 的「本回合」效果：
/// 1) 受到的伤害减少 50%
/// 2) 本回合内受过伤害 → 下回合开始时 +1 能量
///
/// 到期时机：**敌人回合结束**（不是玩家回合结束）。
/// 这是防御向能力，必须盖住敌人的这次行动；玩家回合结束就摘的话，
/// 敌人攻击完全吃不到减伤（2026-11-17 修，和浮空/龙鳞同一个 bug）。
///
/// 「受过伤害」这个标记不能用实例字段：
/// 死归回档时 BattleSnapshot 只恢复 Amount（实例字段全丢），
/// 用字段的话「受伤后死归」会白丢下回合那 1 点能量。
/// 所以借 Amount 当**隐藏数据位**——
///   Amount = 1 → 本回合还没受过伤
///   Amount = 2 → 受过伤了
/// 本能力是 <see cref="PowerStackType.Single"/>（图标上不显示数字），
/// 玩家看不到这个 1/2，和文森特契约用 Amount 藏「是否升级」是同一套做法。
/// </summary>
public sealed class EmtPower : CustomPowerModel
{
    /// <summary>Amount = 1 → 未受伤；2 → 已受伤。</summary>
    private const int DamagedFlag = 2;

    public override PowerType Type => PowerType.Buff;
    // 效果固定（50% 减伤 + 1 能量），层数无意义 → Single（也就不会把 1/2 显示出来）
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "emt_power.png".PowerImagePath();

    public override string CustomBigIconPath => "emt_power.png".BigPowerImagePath();

    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        // 返回的是倍率（引擎自己乘），不是算好的伤害
        if (target != Owner)
            return 1m;

        return 0.5m;
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature == Owner && delta < 0)
            Amount = DamagedFlag;

        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        try
        {
            if (side != CombatSide.Enemy)
                return;

            var owner = Owner;
            if (owner == null)
                return;

            if (Amount >= DamagedFlag)
            {
                TurnStartQueue.AddEnergy(1);
                Godot.GD.Print("[Subaru] emt: took damage, +1 energy next turn");
            }

            await PowerCmd.Remove(this);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] emt end-of-turn failed: " + e);
        }
    }
}
