using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

// ─────────────────────────────────────────────────────────────
// 大罪魔女：全部是「权能」，全部付遗香代价
// ─────────────────────────────────────────────────────────────

/// <summary>强欲魔女·艾姬多娜：稀有，3 费，能力。每打出一张技能牌抽 1。权能 3。升级：费用 3→2。
/// 2026-11-19：能力改成可叠层后，费用 2→3。</summary>
public sealed class EchidnaWitch() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>打出这张牌时播艾姬多娜的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "echidna";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 5;

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<EchidnaPower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);
    }
}

/// <summary>暴食魔女·达芙妮：罕见，2 费，能力。每回合开始失去 1 生命、抽 Amount 张。权能 12、腐蚀。
/// 升级：抽牌 1→2（不再减费）。</summary>
public sealed class DaphneWitch() : SubaruCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary><c>data/cutin.json</c> 里的暴食魔女立绘（2026-11-22 用户提供；棺材整体保留）。</summary>
    public override string? CutInId => "daphne";

    public override HashSet<CardKeyword> CanonicalKeywords =>
    [
        .. base.CanonicalKeywords,
        SubaruKeywords.Authority,
        SubaruKeywords.Corrosion,
    ];

    protected override int Authority => 12;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<DaphnePower>(1m)];

    protected override void OnUpgrade() => DynamicVars["DaphnePower"].UpgradeValueBy(1m); // 抽 1 → 2

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DaphnePower>(
            choiceContext, Owner.Creature, DynamicVars["DaphnePower"].BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>
/// 嫉妒魔女·莎缇拉：稀有，3 费，能力。每次死归后全体 15×死归次数。权能 18、腐蚀。
/// 升级：费用 3→2、每次伤害 15→17（0.1.3 起不再给[固有]）。
/// </summary>
public sealed class SatellaWitch() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
    [
        .. base.CanonicalKeywords,
        SubaruKeywords.Authority,
        SubaruKeywords.Corrosion,
    ];

    protected override int Authority => 18;

    // 卡面上的 15/17：实际结算在 DeathReturnSpike.ApplyRewindBlessing，那边读 SatellaPower.DamagePerRewind
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(15m, ValueProp.Move)];

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
        DynamicVars.Damage.UpgradeValueBy(2m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var perRewind = DynamicVars.Damage.BaseValue;

        await PowerCmd.Apply<SatellaPower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);

        // 把这张牌（可能已升级）的每次伤害写到能力上；能力带【腐蚀】不随死归重建，字段能活下来
        var power = Owner.Creature.Powers.OfType<SatellaPower>().FirstOrDefault();
        if (power != null)
            power.DamagePerRewind = perRewind;

        GD.Print($"[Subaru] satella witch: per-rewind damage={perRewind}");
    }
}

/// <summary>愤怒魔女·密涅瓦：罕见，1 费，攻击（群体）。回复 3 生命，全体 8 伤。权能 12。升级：伤害 +4、回复 +1。</summary>
public sealed class MinervaWitch() : SubaruCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
{
    /// <summary><c>data/cutin.json</c> 里的愤怒魔女立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "minerva";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8m, ValueProp.Move),
        new HealVar(3m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars.Heal.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        if (player == null || state == null)
            return;

        // 破坏之后是再生：先回复
        await CreatureCmd.Heal(player.Creature, DynamicVars.Heal.BaseValue, false);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(state)
            .Execute(choiceContext);
    }
}

/// <summary>怠惰魔女·赛赫麦特：稀有，2 费，攻击。23 伤；目标意图不是攻击时改 33。权能 5。升级：23→27、33→39。</summary>
public sealed class SekhmetWitch() : SubaruCard(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary><c>data/cutin.json</c> 里的怠惰魔女立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "sekhmet";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 5;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(23m, ValueProp.Move),
        new IntVar("Heavy", 33m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars["Heavy"].UpgradeValueBy(6m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var intendsToAttack = target.Monster?.GetIntents()
            ?.Any(i => i.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) ?? false;

        var damage = intendsToAttack ? DynamicVars.Damage.BaseValue : DynamicVars["Heavy"].BaseValue;

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);
    }
}

/// <summary>傲慢魔女·提丰：普通，1 费，攻击。7 伤；目标生命低于你已损失的生命 → 致命。权能 3。</summary>
public sealed class TyphonWitch() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary><c>data/cutin.json</c> 里的傲慢魔女立绘（2026-11-22 用户提供，半身）。</summary>
    public override string? CutInId => "typhon";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var player = Owner;
        if (player == null)
            return;

        var target = cardPlay.Target;
        var lost = player.Creature.MaxHp - player.Creature.CurrentHp;

        // 断罪：目标的生命低于你已失去的生命 → 直接抹除（和阿尔·纱幕共用一套斩杀口径）
        if (lost > 0 && Combat.ExecuteKill.CanKill(target, lost))
        {
            await Combat.ExecuteKill.KillAsync(new[] { target }, "typhon");
            return;
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);
    }
}

/// <summary>
/// 色欲魔女·卡蜜拉：罕见，**2 费**，技能。获得 1 浮空（= 1 个回合的 50% 减伤）；全体敌人永久 -2 力量。权能 4。
/// 升级：浮空 1→2（改版后 = 多一个回合的减伤，不再加强度）。
/// 2026-11-17 削弱：1 费 → 2 费（浮空改成「层数 = 回合、减伤恒 50%」后它比穆拉克全面更强）。
/// </summary>
public sealed class CarmillaWitch() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary><c>data/cutin.json</c> 里的色欲魔女立绘（2026-11-22 用户提供，半身）。</summary>
    public override string? CutInId => "carmilla";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<FloatPower>(1m),
        new PowerVar<StrengthPower>(2m),
    ];

    protected override void OnUpgrade() => DynamicVars["FloatPower"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        if (player == null || state == null)
            return;

        await PowerCmd.Apply<FloatPower>(
            choiceContext, player.Creature, DynamicVars["FloatPower"].BaseValue, player.Creature, this, false);

        var loss = DynamicVars.Strength.BaseValue;
        foreach (var enemy in state.HittableEnemies)
        {
            await PowerCmd.Apply<StrengthPower>(
                choiceContext, enemy, -loss, player.Creature, this, false);
        }
    }
}
