using System;
using System.Collections.Generic;
using System.Linq;
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
/// 柯尔·雷欧尼斯（狮子之心）· <b>减伤窗口</b>。这张牌拆成两个能力，一个能力只表示一件事：
///
/// <list type="bullet">
/// <item><see cref="CorLeonisGuardPower"/>（本类）= <b>窗口</b>：存在即减伤，消失即窗口关闭。</item>
/// <item><see cref="CorLeonisDebtPower"/> = <b>债务</b>：Amount 就是还欠多少伤害。</item>
/// </list>
///
/// <para><b>为什么拆开（2026-11-20 重写）</b>：旧版把「欠债 / 升级 / 已武装」三个状态打包进
/// 同一个 <c>Amount</c> 的比特里，于是出现了两个治不掉的问题：
/// ① <c>已武装</c>（窗口已关、等还债）在界面上没有任何区别 —— 图标还亮着，玩家挨打却是满伤，
/// 玩家只能按图标判断，而图标在骗人；
/// ② <c>Armed</c> 位跟着 <c>Amount</c> 一起被死归快照整体写回，回档后可能残留成"已武装"，
/// 这一回合就静默不减伤。
/// 拆开之后：窗口开着 = 有这个能力，窗口一关就 <see cref="PowerCmd.Remove"/>，图标随即消失，
/// 不再存在"图标在但内部已关"的状态。</para>
///
/// <para><b>窗口的边界</b>：打出 → <b>紧接着那个敌方回合结束</b>。
/// 武装必须在敌方回合结束（2026-11-17 IL 实证）：<c>AfterSideTurnEnd(Player)</c> 跑在敌人出手
/// <b>之前</b>，挂在玩家回合结束会在敌人行动前就把窗口关掉（浮空 <see cref="FloatPower"/> 踩过同一个坑）。</para>
///
/// <para><b>多人（2026-11-19 用户定）</b>：减伤对所有玩家方角色生效 —— 自己 50%、队友 30%；
/// 债务只记 <b>自己</b> 掉的血。</para>
/// </summary>
public sealed class CorLeonisGuardPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;

    // 不显示数字：这个能力的层数没有意义
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    // 复用浮空的白鸟图案（本能力是防御向 buff，和浮空同族）
    public override string CustomPackedIconPath => "float_power.png".PowerImagePath();
    public override string CustomBigIconPath => "float_power.png".BigPowerImagePath();

    /// <summary>窗口能力的 Amount 恒为 1（只是"存在"的标记，不承载任何状态）。</summary>
    public static int InitialAmount() => 1;

    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        if (Amount <= 0)
            return 1m;

        // 还债打出去的那一下不能再被自己减伤（否则债务越还越少）
        if (CorLeonisDebtPower.Settling)
            return 1m;

        // target 必须判空：这个钩子在「卡牌预览刷新」里也会被调用，那条链路上 target 是 null
        // （2026-11-20 日志实证：直接调 target.IsPlayer 会 NRE，且异常在
        //  NCardPlayQueue.UpdateCardBeforeExecution 里把整个 PlayCardAction 打断）
        if (target == null || !target.IsPlayer)
            return 1m;

        return ReferenceEquals(target, Owner) ? 0.5m : 0.7m;
    }

    /// <summary>
    /// 记账：窗口内自己掉的血 = 这一下"没在本回合付掉"的部分。
    /// 债务写进 <see cref="CorLeonisDebtPower"/> 的 Amount（它是字面数值，死归快照会原样还原）。
    /// </summary>
    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature != Owner || delta >= 0)
            return Task.CompletedTask;

        var debtPower = Owner?.Powers.OfType<CorLeonisDebtPower>().FirstOrDefault();
        if (debtPower == null)
        {
            // 正常不会发生（卡牌把两个能力一起挂上）；真发生了只能记日志，别静默吞掉
            Godot.GD.PushWarning("[Subaru] cor leonis: debt power missing, hp loss not recorded");
            return Task.CompletedTask;
        }

        var loss = (int)Math.Ceiling(-delta);
        debtPower.AddDebt(loss);
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        if (side != CombatSide.Enemy)
            return;

        var debt = Owner?.Powers.OfType<CorLeonisDebtPower>().FirstOrDefault()?.Debt ?? 0;
        Godot.GD.Print($"[Subaru] cor leonis: guard down (debt={debt})");
        await PowerCmd.Remove(this);
    }
}

/// <summary>
/// 柯尔·雷欧尼斯的<b>债务</b>：Amount = 还欠的伤害 + 1
/// （+1 是因为引擎在 <c>Amount &lt;= 0</c> 时会把能力直接摘掉，而"还欠 0"是合法状态）。
///
/// <para>结算时机：<b>窗口（Guard）不在场时的玩家回合结束</b>。
/// 少一个"武装"状态机：Guard 在不在就是窗口开没开，不需要再拿一个比特去记"我是不是已经过了本回合"。</para>
///
/// <para>升级版走 <see cref="CorLeonisDebtHalvedPower"/>（结算时减半），
/// 用子类而不是比特位 —— 升级是配置，写一次就不该再变，犯不着和债务挤同一个整数。</para>
/// </summary>
public class CorLeonisDebtPower : CustomPowerModel
{
    /// <summary>结算进行中：让 Guard 放过这一下伤害。</summary>
    public static bool Settling { get; private set; }

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "power.png".PowerImagePath();
    public override string CustomBigIconPath => "power.png".BigPowerImagePath();

    /// <summary>还欠多少伤害。</summary>
    public int Debt => Math.Max(0, Amount - 1);

    /// <summary>结算时是否减半（升级版覆写）。</summary>
    protected virtual bool Halved => false;

    /// <summary>累加债务。每一下伤害写一次，走 Amount 的 setter 让界面数字跟着刷新。</summary>
    public void AddDebt(int hpLoss)
    {
        if (hpLoss <= 0)
            return;

        Amount = Amount + hpLoss;
        Godot.GD.Print($"[Subaru] cor leonis: debt +{hpLoss} -> {Debt}");
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

        // 窗口还开着 = 这一回合又打了一张牌，减伤还在继续，债务留到下一次
        if (owner.Powers.OfType<CorLeonisGuardPower>().Any())
        {
            Godot.GD.Print($"[Subaru] cor leonis: debt held ({Debt}), guard still up");
            return;
        }

        var debt = Debt;
        await PowerCmd.Remove(this);

        if (Halved)
            debt /= 2;
        if (debt <= 0)
            return;

        Godot.GD.Print($"[Subaru] cor leonis: settle {debt} damage (halved={Halved})");
        Settling = true;
        try
        {
            // 债务可以被格挡（2026-11-17 用户定）：Move 走正常伤害流程，Unpowered 不吃力量加成
            await CreatureCmd.Damage(
                choiceContext, owner, debt,
                ValueProp.Move | ValueProp.Unpowered, null);
        }
        finally
        {
            Settling = false;
        }
    }
}

/// <summary>升级版雷欧尼斯的债务：结算时减半。</summary>
public sealed class CorLeonisDebtHalvedPower : CorLeonisDebtPower
{
    protected override bool Halved => true;
}
