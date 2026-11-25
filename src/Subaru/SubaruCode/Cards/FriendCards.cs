using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>蓝之友：罕见，1 费。5 格挡 + 回复 4 生命。升级：5→8，4→5。</summary>
public sealed class BlueFriend() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>菲利斯（青·菲利斯）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "felix";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(5m, ValueProp.Move),
        new HealVar(4m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
        DynamicVars.Heal.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue, false);
    }
}

/// <summary>治愈魔法：普通，0 费。回复 5 生命。[贝蒂][消耗]。升级：取消消耗。</summary>
public sealed class HealingMagic() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new HealVar(5m)];

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue, false);
    }
}

/// <summary>
/// 柯尔·雷欧尼斯：普通，1 费，技能，[权能 4][消耗]。
/// **每回合只能打出 1 张**（打出后本回合手里其它同名牌，无论升不升级，都不可打出）。
/// 本回合受到的伤害减少 50%，**被减掉的那一半**转移到下个玩家回合结束时补给自己。
/// 升级：转移回来的伤害减半。
///
/// 逻辑拆在两个能力里：<see cref="CorLeonisGuardPower"/> 只管"减伤窗口开没开"，
/// <see cref="CorLeonisDebtPower"/> 只管"还欠多少伤害"。这张牌负责把两者挂上去 + 记「本回合已打出」。
/// </summary>
public sealed class CorLeonis() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, CardKeyword.Exhaust];

    protected override int Authority => 4;

    // 升级只影响能力里的「转移伤害减半」，卡面本身没有可改的数值
    protected override void OnUpgrade()
    {
    }

    /// <summary>本回合已经打出过一张 → 手里其它同名牌不可打出（升级版也算）。</summary>
    private bool PlayedThisTurn =>
        Rewind.CorLeonisTurnLedger.PlayedThisTurn(Owner?.Creature?.CombatState, Owner);

    protected override bool IsPlayable
    {
        get
        {
            if (PlayedThisTurn)
                return false;

            return base.IsPlayable;
        }
    }

    /// <summary>引擎的自动打出只问 ShouldPlay，不问 IsPlayable，这里补一层。</summary>
    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card != null && ReferenceEquals(card, this) && PlayedThisTurn)
            return false;

        return base.ShouldPlay(card, autoPlayType);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 先记账：本回合剩下的同名牌立刻变成不可打出
        Rewind.CorLeonisTurnLedger.MarkPlayed(Owner?.Creature?.CombatState, Owner);

        // 2026-11-20 拆成两个能力：窗口（Guard）只表示"减伤还在"，债务（Debt）的 Amount 就是欠多少。
        // 旧版把三个状态挤在一个 Amount 的比特里，重复 Apply 会把编码写坏（1 + 1 = 2 被读成
        // 「已升级 + 已武装」），所以那时只能靠"不重复施加"绕开；现在两个能力各自单调，不需要绕。

        // 1) 窗口：已经在就不重复施加（它的 Amount 恒为 1，只是个存在标记）
        if (Owner.Creature.Powers.OfType<CorLeonisGuardPower>().FirstOrDefault() == null)
        {
            await PowerCmd.Apply<CorLeonisGuardPower>(
                choiceContext, Owner.Creature,
                CorLeonisGuardPower.InitialAmount(),
                Owner.Creature, this, false);
            Godot.GD.Print($"[Subaru] cor leonis: guard up (upgraded={IsUpgraded})");
        }
        else
        {
            Godot.GD.Print("[Subaru] cor leonis: guard refreshed (already up)");
        }

        // 2) 债务：没有就挂一个空的（Amount=1 → 还欠 0）；已有就继续往同一个里记。
        //    升级版走子类 CorLeonisDebtHalvedPower —— 结算时减半，不用比特位记"升级"。
        if (Owner.Creature.Powers.OfType<CorLeonisDebtPower>().FirstOrDefault() == null)
        {
            if (IsUpgraded)
                await PowerCmd.Apply<CorLeonisDebtHalvedPower>(
                    choiceContext, Owner.Creature, 1, Owner.Creature, this, false);
            else
                await PowerCmd.Apply<CorLeonisDebtPower>(
                    choiceContext, Owner.Creature, 1, Owner.Creature, this, false);
        }
    }
}

/// <summary>剑鬼之友：罕见，2 费。8 伤害；若目标意图为攻击，获得 13 格挡。升级：8→12，13→19。</summary>
public sealed class SwordDemonFriend() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>龙剑系：和莱茵哈鲁特同一套演出（2026-11-22 用户定「他们的特效与莱茵哈鲁特相同」）。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>专属命中音，关掉通用每段命中音。</summary>
    public override bool HasCustomHitSfx => true;

    /// <summary><c>data/cutin.json</c> 里的剑鬼立绘。</summary>
    public override string? CutInId => "sworddemon";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8m, ValueProp.Move),
        new BlockVar(13m, ValueProp.Move),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars.Block.UpgradeValueBy(6m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;

        // 先判断意图（打完之后意图可能已经变了）
        var intendsToAttack = target.Monster?.GetIntents()
            ?.Any(i => i.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) ?? false;

        // 和剑圣同一刀
        await Vfx.ReinhardSlash.PlayAsync(Owner, cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);

        if (intendsToAttack)
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }
}

/// <summary>
/// 剑圣之友：稀有，X 费。造成 12 ×（X + Bonus）点伤害，Bonus 基础 1、升级 2（即 X+1 → X+2）。
/// X 费靠 HasEnergyCostX 让引擎自己处理，实际支付额从 cardPlay.Resources.EnergySpent 取。
///
/// Bonus 做成 DynamicVar 而不是 `IsUpgraded ? 2 : 1`：这样卡面能显示 1→2 的升级差异，
/// 卡牌表也能自动推出「段数 +1」。
/// </summary>
public sealed class SwordSaintFriend() : SubaruCard(0, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>龙剑斩线是专属演出，停掉角色默认攻击动作（2026-11-22 用户定）。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>出刀 / 击杀音都是专属的，关掉通用的每段命中音（2026-11-22 用户定）。</summary>
    public override bool HasCustomHitSfx => true;

    /// <summary>打出「剑圣·莱茵哈鲁特」时播莱茵哈鲁特的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "reinhard";

    protected override bool HasEnergyCostX => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(12m, ValueProp.Move),
        new IntVar("Bonus", 1m),
    ];

    protected override void OnUpgrade() => DynamicVars["Bonus"].UpgradeValueBy(1m);

    // 升级只影响「每段伤害的次数」，没有数值可改
    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var x = cardPlay.Resources.EnergySpent;
        var hits = x + (int)DynamicVars["Bonus"].BaseValue;

        // 龙剑：整屏竖切，**一张牌只演一次**（不管几段伤害）—— 2026-11-22 用户定。
        await Vfx.ReinhardSlash.PlayAsync(Owner, cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount(hits)
            .Execute(choiceContext);

        Godot.GD.Print($"[Subaru] sword saint friend: x={x}, bonus={DynamicVars["Bonus"].BaseValue}, hits={hits}");
    }
}
