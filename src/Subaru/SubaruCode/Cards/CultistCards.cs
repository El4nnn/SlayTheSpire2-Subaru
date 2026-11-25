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

namespace Subaru.SubaruCode.Cards;

// ─────────────────────────────────────────────────────────────
// 大罪司教：全部是「权能」，全部付遗香代价
// ─────────────────────────────────────────────────────────────

/// <summary>强欲司教·狮子心脏：稀有，3 费，攻击。33 伤无视格挡。权能 5。升级 33→45。</summary>
/// <remarks>
/// 「无视格挡」必须写 <c>Move | Unblockable</c>，不能只写 <c>Unblockable</c>：
/// 引擎的 <c>IsPoweredAttack</c> 要求带 <c>Move</c>（`HasFlag(Move) &amp;&amp; !HasFlag(Unpowered)`），
/// 只写 <c>Unblockable</c> 会被判成「非攻击」—— 力量、易伤、虚弱、锋利、本能
/// 在 <c>Hook.ModifyDamage</c> 里全被跳过（`StrengthPower` 返回 0、`VulnerablePower` 返回 1）。
/// </remarks>
public sealed class Regulus() : SubaruCard(3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>强欲司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "regulus";

    private const ValueProp Props = ValueProp.Move | ValueProp.Unblockable;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 5;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(33m, Props)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(12m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var cmd = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target);

        cmd.DamageProps = Props;
        await cmd.Execute(choiceContext);
    }
}

/// <summary>暴食司教·月蚀：罕见，2 费，攻击。12 伤；杀敌则回 8 血。权能 5。升级 12→16。</summary>
public sealed class Ley() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>暴食司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "ley";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 5;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(12m, ValueProp.Move),
        new HealVar(8m),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var player = Owner;
        if (player == null)
            return;

        var target = cardPlay.Target;

        var cmd = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);

        var killed = cmd.Results.SelectMany(l => l).Any(r => r.WasTargetKilled) || target.IsDead;
        if (!killed)
            return;

        // 吃掉记忆 → 回复生命
        await CreatureCmd.Heal(player.Creature, DynamicVars.Heal.BaseValue, false);

        Godot.GD.Print("[Subaru] ley: devoured target");
    }
}

/// <summary>色欲司教·变异：普通，1 费，攻击。2 伤 3 次；获得等同于实际造成伤害的格挡。权能 3。升级 次数 3→4。</summary>
public sealed class Capella() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary>色欲司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "capella";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(2m, ValueProp.Move),
        new IntVar("Hits", 3m),
    ];

    protected override void OnUpgrade() => DynamicVars["Hits"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var player = Owner;
        if (player == null)
            return;

        var cmd = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);

        var dealt = cmd.Results
            .SelectMany(list => list)
            .Sum(r => (decimal)r.TotalDamage);

        if (dealt <= 0)
            return;

        await CreatureCmd.GainBlock(player.Creature, dealt, ValueProp.Move, cardPlay, false);
        Godot.GD.Print($"[Subaru] capella: morph block {dealt}");
    }
}

/// <summary>愤怒司教·洗魂：稀有，2 费，能力。敌人受伤时其他敌人也受 3 伤。权能 5、腐蚀。升级 3→5。</summary>
public sealed class Sirius() : SubaruCard(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>愤怒司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "sirius";

    public override HashSet<CardKeyword> CanonicalKeywords =>
    [
        .. base.CanonicalKeywords,
        SubaruKeywords.Authority,
        SubaruKeywords.Corrosion,
    ];

    protected override int Authority => 12;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<SiriusPower>(3m)];

    protected override void OnUpgrade() => DynamicVars["SiriusPower"].UpgradeValueBy(2m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<SiriusPower>(
            choiceContext, Owner.Creature, DynamicVars["SiriusPower"].BaseValue, Owner.Creature, this, false);
    }
}
