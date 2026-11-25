using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 幽暗纱幕：罕见，2 费，技能。1 个敌人晕眩 1 回合；对所有敌人 3 易伤 + 3 虚弱。
/// [贝蒂][消耗]。升级：虚弱 3→5。
/// </summary>
public sealed class GloomShamak() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<VulnerablePower>(3m),
        new PowerVar<WeakPower>(3m),
    ];

    protected override void OnUpgrade() => DynamicVars.Weak.UpgradeValueBy(2m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null || CombatState == null)
            return;

        // 晕眩只对选定的 1 个目标（群体晕眩等于群体无敌，太强）
        if (cardPlay.Target != null)
        {
            StunHelper.ApplyStun(cardPlay.Target);
        }

        // 易伤 / 虚弱 仍然是全体
        foreach (var enemy in CombatState.HittableEnemies)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext, enemy, DynamicVars.Vulnerable.BaseValue, player.Creature, this, false);
            await PowerCmd.Apply<WeakPower>(
                choiceContext, enemy, DynamicVars.Weak.BaseValue, player.Creature, this, false);
        }
    }
}

/// <summary>
/// 乌尔·纱幕：稀有，3 费，攻击。移除目标所有[人工制品]与格挡；40 伤害；1 易伤 + 1 虚弱。
/// [贝蒂][消耗]。升级：伤害 40→54。
/// </summary>
public sealed class UlShamak() : SubaruCard(3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(16m, ValueProp.Move),
        new PowerVar<VulnerablePower>(2m),
        new PowerVar<WeakPower>(2m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars.Vulnerable.UpgradeValueBy(1m);
        DynamicVars.Weak.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var player = Owner;

        // 人工制品会吃掉后面的减益，先清掉
        var artifacts = target.Powers.Where(p => p is ArtifactPower).ToList();
        foreach (var artifact in artifacts)
            await PowerCmd.Remove(artifact);

        if (target.Block > 0)
            await CreatureCmd.LoseBlock(choiceContext, target, target.Block, Owner.Creature);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);

        await PowerCmd.Apply<VulnerablePower>(choiceContext, target, DynamicVars.Vulnerable.BaseValue, player.Creature, this, false);
        await PowerCmd.Apply<WeakPower>(choiceContext, target, DynamicVars.Weak.BaseValue, player.Creature, this, false);
    }
}

/// <summary>
/// 阿尔·纱幕：稀有，3 费，攻击。斩杀所有生命值低于「Factor × 本场战斗已打出技能牌数」的敌人，
/// Factor 基础 4、升级 5。[阴魔法][消耗]。卡面显示实时阈值。
///
/// Factor 做成 DynamicVar 而不是 `IsUpgraded ? 5 : 4`：卡面能显示 4→5 的升级差异，
/// 卡牌表也能自动推出「系数 +1」。
/// </summary>
public sealed class AlShamak() : SubaruCard(3, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    // 卡面直接显示当前斩杀线（Factor × 已打出技能数）：跟着战斗实时变化
    //
    // ⚠️ 最后那个 mult 参数**必须是 1，不能是 0**：BaseLib 的 MakeCalculatedVar 会生成
    // CalculationBase(=baseVal) 与 CalculationExtra(=mult) 两个变量，而原版
    // CalculatedVar.Calculate = CalculationBase + CalculationExtra × 乘数函数。
    // 两个都给 0 的话卡面永远显示「当前 0」（2026-11-17 实测踩到）。
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Factor", 4m),
        .. MakeCalculatedVar("Threshold", 0, (card, target) => ThresholdNow(card), 1)
    ];

    /// <summary>
    /// 斩杀线 = Factor × 本场已打出技能数。
    ///
    /// <para><b>卡面显示和实战判定必须走这一个函数</b>：以前两处各写一遍一样的算式，
    /// 只要哪天改了一边，玩家就会遇到「写着能斩、实际没斩」——2026-11-20 玩家反馈
    /// 「写着 4×12 却没斩杀 30 血敌人」就是这类口径漂移最容易背锅的地方。</para>
    /// </summary>
    private static int ThresholdNow(CardModel card) =>
        (int)(card.DynamicVars["Factor"].BaseValue * CombatStats.SkillsPlayed);

    protected override void OnUpgrade() => DynamicVars["Factor"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        var factor = DynamicVars["Factor"].BaseValue;
        var threshold = ThresholdNow(this);
        Godot.GD.Print($"[Subaru] al shamak: skills={CombatStats.SkillsPlayed}, factor={factor}, threshold={threshold}");

        // 候选集 / 抹除 / 日志全部走共用入口（和傲慢魔女·提丰同一套口径）。
        // 这里**不能**自己用 HittableEnemies 过滤 —— 见 Combat/ExecuteKill.cs 顶部的说明。
        await Combat.ExecuteKill.KillBelowAsync(state, threshold, "al shamak", verbose: true);
    }
}
