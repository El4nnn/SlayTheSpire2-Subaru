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

// ─────────────────────────────────────────────────────────────
// 三大魔兽 & 三英杰
// ─────────────────────────────────────────────────────────────

/// <summary>白鲸·消失之雾：罕见，2 费，攻击（群体）。全体 11 伤 + 2 易伤 + 2 虚弱。[消耗]。升级 11→15。</summary>
public sealed class Hakugei() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(11m, ValueProp.Move),
        new PowerVar<VulnerablePower>(2m),
        new PowerVar<WeakPower>(2m),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        if (player == null || state == null)
            return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(state)
            .Execute(choiceContext);

        foreach (var enemy in state.HittableEnemies)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext, enemy, DynamicVars.Vulnerable.BaseValue, player.Creature, this, false);
            await PowerCmd.Apply<WeakPower>(
                choiceContext, enemy, DynamicVars.Weak.BaseValue, player.Creature, this, false);
        }
    }
}

/// <summary>
/// 黑蛇·疫病：罕见，0 费，技能。全体 3 易伤 + 3 虚弱 + 3 中毒；你失去 5 生命。
/// 升级：易伤 3→4、虚弱 3→4、中毒 3→5（自伤不变）。
/// </summary>
public sealed class Kurohebi() : SubaruCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<VulnerablePower>(3m),
        new PowerVar<WeakPower>(3m),
        new PowerVar<PoisonPower>(3m),
        new IntVar("SelfDamage", 5m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Vulnerable.UpgradeValueBy(1m);
        DynamicVars.Weak.UpgradeValueBy(1m);
        DynamicVars.Poison.UpgradeValueBy(2m); // 3 → 5
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        if (player == null || state == null)
            return;

        foreach (var enemy in state.HittableEnemies)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext, enemy, DynamicVars.Vulnerable.BaseValue, player.Creature, this, false);
            await PowerCmd.Apply<WeakPower>(
                choiceContext, enemy, DynamicVars.Weak.BaseValue, player.Creature, this, false);
            await PowerCmd.Apply<PoisonPower>(
                choiceContext, enemy, DynamicVars.Poison.BaseValue, player.Creature, this, false);
        }

        await CreatureCmd.Damage(choiceContext, player.Creature, DynamicVars["SelfDamage"].BaseValue,
            ValueProp.Unblockable, player.Creature);
    }
}

/// <summary>初代剑圣·雷德：稀有，2 费，攻击。6 伤 6 次；升级改成 8 次（伤害不变）。</summary>
public sealed class Reid() : SubaruCard(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>龙剑系：和莱茵哈鲁特同一套演出（2026-11-22 用户定「他们的特效与莱茵哈鲁特相同」）。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>专属命中音（出刀音由 <see cref="Vfx.ReinhardSlash"/> 播），关掉通用每段命中音。</summary>
    public override bool HasCustomHitSfx => true;

    /// <summary><c>data/cutin.json</c> 里的初代剑圣立绘。</summary>
    public override string? CutInId => "reid";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6m, ValueProp.Move),
        new IntVar("Hits", 6m),
    ];

    // 升级只加段数：6 → 8
    protected override void OnUpgrade() => DynamicVars["Hits"].UpgradeValueBy(2m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        // 和剑圣同一刀：一张牌只演一次（6/8 段也是切一刀）
        await Vfx.ReinhardSlash.PlayAsync(Owner, cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);
    }
}

/// <summary>贤者·夏乌拉：罕见，2 费，攻击。16 伤；本场第一次打出改为 2 次。升级 16→22。</summary>
public sealed class Shaula() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>夏乌拉有专属打击/终结演出，停掉默认攻击动作。</summary>
    public override bool HasCustomAttackVfx => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(16m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(6m);

    /// <summary>
    /// 这张牌有自己的演出（橘色接管 + 白射线 + 爆开），不要通用弹幕。
    ///
    /// <para>2026-11-21 用户定的流程：打出 → （本回合第一次 / 终结时才播立绘出场）→
    /// 橘色接管 + 白射线 → 命中爆开、结算伤害 → 收场。终结时底色转灰白、射线变黑、敌人变黑剪影。</para>
    ///
    /// <para>「本场第一次打出**此牌**」按牌实例算（两张同名牌各自算第一次）；
    /// 立绘出场走全局策略（<see cref="Vfx.CutIn.MayShow"/>）：每角色每回合一次、
    /// 每回合最多 N 个不同角色，终结那一击例外。</para>
    /// </summary>
    public override bool HandlesOwnVolley => true;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;

        // 本场第一次打出「这一张牌」→ 2 段
        var firstPlay = CombatStats.IsShaulaFirstPlay(this);
        CombatStats.MarkShaulaPlayed(this);

        var hits = firstPlay ? 2 : 1;
        var damage = DynamicVars.Damage.BaseValue;

        // 先算这一击会不会打死 / 是不是终结（决定立绘要不要出场、以及配色）
        var plan = Vfx.ShaulaStrike.Prepare(Owner, target, damage * hits);

        // 立绘出场：每角色每回合一次、每回合最多 N 个不同角色（设置页「角色 cut-in」可调）；
        // 终结那一击不受名额限制（用户 2026-11-21 定）。
        var showCutIn = plan.Finisher
            ? Vfx.CutIn.MayShowSignature("shaula")
            : Vfx.CutIn.MayShow("shaula");

        if (showCutIn)
            await Vfx.CutIn.PlayToHitAsync(Owner, "shaula");

        // 橘色接管 + 白射线
        var strike = Vfx.ShaulaStrike.Start(plan);
        if (strike != null)
            await strike.Impact;

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .WithHitCount(hits)
            .Execute(choiceContext);

        // **结算之后**再判终结：结算前的估算会被力量/易伤加伤打歪（实测「终结了还在放紫白」）。
        var killed = target.IsDead || target.CurrentHp <= 0;
        var finisher = killed && Vfx.ShaulaStrike.IsFinisherNow(Owner, target);
        strike?.NotifyDamageDealt(killed, finisher);
    }
}

// ─────────────────────────────────────────────────────────────
// 杂项
// ─────────────────────────────────────────────────────────────

/// <summary>猎肠者·艾尔莎：普通，1 费，攻击。9 伤；目标血量低于一半改 14。升级 9→12 / 14→18。</summary>
public sealed class Elsa() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary>艾尔莎（猎肠者）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "elsa";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(9m, ValueProp.Move),
        new IntVar("Heavy", 14m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        DynamicVars["Heavy"].UpgradeValueBy(4m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var low = target.CurrentHp * 2 < target.MaxHp;
        var damage = low ? DynamicVars["Heavy"].BaseValue : DynamicVars.Damage.BaseValue;

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);
    }
}

/// <summary>选我吧：罕见，3 费，能力。每回合开始抽 Amount 张、随机弃 Amount 张。[腐蚀]。
/// 升级：1 → 2。（0.2.43 重做：原来是 1 费技能「减 10 层遗香 + 抽 1」）</summary>
public sealed class ChooseMe() : SubaruCard(3, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<ChooseMePower>(1m)];

    protected override void OnUpgrade() => DynamicVars["ChooseMePower"].UpgradeValueBy(1m); // 1 → 2

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        await PowerCmd.Apply<ChooseMePower>(
            choiceContext, player.Creature, DynamicVars["ChooseMePower"].BaseValue, player.Creature, this, false);
    }
}
