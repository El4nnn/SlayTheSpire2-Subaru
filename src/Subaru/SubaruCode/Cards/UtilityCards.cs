using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>手机：普通，0 费，技能。抽 1 张，获得 3 点[活力]。[消耗]。
/// 升级：抽 1→2、活力 3→4。</summary>
public sealed class Keitai() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Draw", 1m),
        new IntVar("Vigor", 3m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars["Draw"].UpgradeValueBy(1m);
        DynamicVars["Vigor"].UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        var draws = (int)DynamicVars["Draw"].BaseValue;
        for (var i = 0; i < draws; i++)
            await CardPileCmd.Draw(choiceContext, player);

        await PowerCmd.Apply<VigorPower>(
            choiceContext, player.Creature, DynamicVars["Vigor"].BaseValue, player.Creature, this, false);
    }
}

/// <summary>
/// 宫廷小丑：稀有，3 费，能力。[腐蚀]。
/// 每回合开始时，从四张[基础魔法]里选一张加入手牌。升级：给的是「基础魔法+」。
/// （0.2.43 重做：原来是「每打出一张技能牌，随机敌人 5 伤」）
/// </summary>
public sealed class RoswaalFriend() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    public override string? CutInId => "roswaal";

    /// <summary>
    /// 卡面要写「基础魔法+」——**不能**用 <c>StringVar("Plus", "")</c> 那种「升级换文本」的写法。
    ///
    /// 第三方 mod（玩家的 CombatSolver）会给每张牌算选择键，枚举 <c>DynamicVars</c> 时走
    /// <c>SemanticStateFieldPolicy.ClassifyString</c>，遇到没登记过的**字符串**动态变量就抛
    /// `InvalidOperationException: 未分类的字符串状态字段`，异常冒到 <c>SetupPlayerTurn</c>
    /// 会让战斗直接卡死。所以改成「文案里直接写清升级后是什么」，不占用字符串动态变量。
    /// </summary>
    // table-upgrade: 给强化版基础魔法
    // 升级只改行为（给的是基础魔法+），没有数值变量要调
    protected override void OnUpgrade()
    {
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        await PowerCmd.Apply<RoswaalPower>(
            choiceContext, player.Creature, 1m, player.Creature, this, false);

        var power = player.Creature.Powers.OfType<RoswaalPower>().FirstOrDefault();
        if (power != null)
            power.Upgraded = IsUpgraded;

        Godot.GD.Print($"[Subaru] roswaal friend played (upgraded={IsUpgraded})");
    }
}

/// <summary>多重吟唱：稀有，1 费，技能。本回合下一张非腐蚀技能牌触发 2 次。[消耗]。升级：3 次。</summary>
public sealed class Meteor() : SubaruCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Times", 2m)];

    protected override void OnUpgrade() => DynamicVars["Times"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<MeteorPower>(
            choiceContext, Owner.Creature, DynamicVars["Times"].BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>神龙·波尔肯尼卡：罕见，2 费，技能。20 格挡 + 本回合受伤 −30%。[消耗]。升级 20→30。</summary>
public sealed class Volcanica() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>神龙的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "volcanica";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(20m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(10m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null)
            return;

        await CreatureCmd.GainBlock(player.Creature, DynamicVars.Block, cardPlay);
        await PowerCmd.Apply<VolcanicaPower>(
            choiceContext, player.Creature, 1m, player.Creature, this, false);
    }
}

/// <summary>门：普通，0 费，技能。抽 2 张，失去 3 点生命。升级 抽 2→3。</summary>
public sealed class Gate() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Draw", 2m),
        new HpLossVar(3m),
    ];

    protected override void OnUpgrade() => DynamicVars["Draw"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null)
            return;

        var draws = (int)DynamicVars["Draw"].BaseValue;
        for (var i = 0; i < draws; i++)
            await CardPileCmd.Draw(choiceContext, player);

        // 强开一次门＝用身体换资源
        await CreatureCmd.Damage(
            choiceContext,
            new[] { player.Creature },
            DynamicVars.HpLoss.BaseValue,
            ValueProp.Unblockable,
            player.Creature);
    }
}
