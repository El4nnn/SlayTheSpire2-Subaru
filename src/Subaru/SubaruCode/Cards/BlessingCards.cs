using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
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
// 加护系列：有加护的角色不付【权能】代价（权能与加护无法共存）
// ─────────────────────────────────────────────────────────────

/// <summary>风视的加护：罕见，1 费。9 伤；目标意图为攻击时改 16。升级 9→12 / 16→22。</summary>
public sealed class CruschFriend() : SubaruCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>库珥修（风视的加护）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "crusch";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(9m, ValueProp.Move),
        new IntVar("Heavy", 16m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        DynamicVars["Heavy"].UpgradeValueBy(6m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var intendsToAttack = target.Monster?.GetIntents()
            ?.Any(i => i.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) ?? false;

        var damage = intendsToAttack ? DynamicVars["Heavy"].BaseValue : DynamicVars.Damage.BaseValue;

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);
    }
}

/// <summary>太阳的加护：稀有，2 费。10 伤；本场每经过一个自己的回合 +6。升级 10→14，+6→+8。</summary>
public sealed class PriscillaFriend() : SubaruCard(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>普莉希拉（太阳的加护）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "priscilla";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(10m, ValueProp.Move),
        new IntVar("Growth", 6m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars["Growth"].UpgradeValueBy(2m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var turns = Owner.PlayerCombatState?.TurnNumber ?? 1;
        var bonus = DynamicVars["Growth"].BaseValue * System.Math.Max(0, turns - 1);
        var damage = DynamicVars.Damage.BaseValue + bonus;

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }
}

/// <summary>传心的加护：普通，0 费。抽 1 张，减少 5 层遗香。升级 抽 1→2，减 5→10。不加腐蚀。</summary>
public sealed class LilianaFriend() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override string? CutInId => "liliana";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Draw", 1m),
        new IntVar("Reduce", 5m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars["Draw"].UpgradeValueBy(1m);
        DynamicVars["Reduce"].UpgradeValueBy(5m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null)
            return;

        var draws = (int)DynamicVars["Draw"].BaseValue;
        for (var i = 0; i < draws; i++)
            await CardPileCmd.Draw(choiceContext, player);

        await FragranceReducer.Reduce(choiceContext, player, DynamicVars["Reduce"].BaseValue);
    }
}

/// <summary>
/// 三分的加护：普通，1 费，技能（自身）。[消耗]。
/// 消耗 2 张手牌，获得 1 点[滑溜]。升级：只要消耗 1 张就给（消耗 2 张也还是 1 点）。
///
/// <para>2026-11-18 重做：原来是「5 格挡 + 本回合受过伤则抽 1」，改成「消耗手牌换滑溜」，
/// 稀有度保持普通。</para>
///
/// <para><b>结算规则（2026-11-18 定）</b>：固定只给 1 点，不给多份。
/// 选牌上限固定 2 张；未升级必须凑满 2 张才触发，升级后 1 张即可（**仍可选 2 张**）；
/// <b>张数不够就什么都不给</b>（不会按实际张数打折结算）。</para>
/// </summary>
public sealed class MimiFriend() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    /// <summary>米米（三分的加护）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "mimi";

    // table-upgrade: 消耗 1 张即可

    // 2026-11-18：本体带[消耗]（一场战斗一张只能打一次）。
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    private const int ExhaustCount = 2;

    /// <summary>固定 1 点滑溜；升级只放宽「需要几张牌」，不改数值。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<SlipperyPower>(1m)];

    // 升级不改数值：效果在 OnPlayCard 里读 IsUpgraded（required 由 2 变 1）。
    protected override void OnUpgrade() { }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        // 升级后**仍然最多可以选 2 张**，只是 1 张就够触发（选 2 张也只给 1 点）。
        // 未升级必须凑满 2 张才触发。
        var required = IsUpgraded ? 1 : ExhaustCount;

        var available = pcs.Hand.Cards.Count(c => c != this);
        var maxPick = Math.Min(ExhaustCount, available);

        if (maxPick <= 0)
        {
            Godot.GD.Print("[Subaru] mimi blessing: no cards in hand, no slippery");
            return;
        }

        // 可选张数上限固定 2（手牌不够就按实际），下限 0：可以不选就确认（那就什么都不给）。
        const int minPick = 0;

        var selected = await CardSelectCmd.FromHand(
            choiceContext,
            player,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, minPick, maxPick),
            c => c != this,
            this);

        var picked = (selected ?? []).ToList();

        foreach (var card in picked)
            await CardCmd.Exhaust(choiceContext, card, false, false);

        // 张数不够（未升级只凑到 1 张）→ 什么都不给
        if (picked.Count < required)
        {
            Godot.GD.Print($"[Subaru] mimi blessing: only {picked.Count}/{required} cards, no slippery");
            return;
        }

        var amount = DynamicVars["SlipperyPower"].BaseValue;
        await PowerCmd.Apply<SlipperyPower>(
            choiceContext, player.Creature, amount, player.Creature, this, false);

        Godot.GD.Print($"[Subaru] mimi blessing: exhausted {picked.Count} (required {required}), slippery {amount}");
    }
}

// ─────────────────────────────────────────────────────────────
// 减香系列：【腐蚀】= 整场一次，防止死归把遗香刷归零
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 艾米莉亚的祝福：稀有，3 费，能力。[腐蚀]。
/// 每回合开始时，按手牌里[魔女瘴气]的张数获得格挡（每张 Amount 点）。升级 5 → 7。
/// （0.2.43 重做：原来是「减 10 层遗香 + 回复 3」，改成稳定防御手段）
/// </summary>
public sealed class EmiliaBlessing() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>艾米莉亚（艾米莉亚的祝福）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "emilia";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EmiliaBlessingPower>(5m)];

    protected override void OnUpgrade() => DynamicVars["EmiliaBlessingPower"].UpgradeValueBy(2m); // 5 → 7

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        await PowerCmd.Apply<EmiliaBlessingPower>(
            choiceContext, player.Creature, DynamicVars["EmiliaBlessingPower"].BaseValue, player.Creature, this, false);
    }
}
