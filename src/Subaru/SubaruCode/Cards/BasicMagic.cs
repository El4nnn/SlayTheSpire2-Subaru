using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

// ─────────────────────────────────────────────────────────────
// 基础魔法：只能由「宫廷小丑」生成（参考小刀），不进奖励池 / 商店。
// 四张都是：普通、0 费、[虚无]。
// ─────────────────────────────────────────────────────────────

/// <summary>基础魔法·火：普通，0 费，攻击。[虚无]。对所有敌人造成 10 伤。升级 14。</summary>
public sealed class FireMagic() : SubaruCard(0, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
{
    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Ethereal, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(10m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(state)
            .Execute(choiceContext);
    }
}

/// <summary>基础魔法·风：普通，0 费，攻击。[虚无]。对随机敌人造成 7 伤，2 次。升级 3 次。</summary>
public sealed class WindMagic() : SubaruCard(0, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
{
    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Ethereal, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(7m, ValueProp.Move),
        new IntVar("Hits", 2m),
    ];

    protected override void OnUpgrade() => DynamicVars["Hits"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        var player = Owner;
        if (state == null || player == null)
            return;

        var hits = (int)DynamicVars["Hits"].BaseValue;

        // 每一段都重新随机目标（回旋镖那种），随机走游戏自己的流，SL 结果一致
        for (var i = 0; i < hits; i++)
        {
            var enemies = state.HittableEnemies.ToList();
            if (enemies.Count == 0)
                return;

            var rng = player.RunState?.Rng?.CombatTargets;
            var target = rng != null ? rng.NextItem(enemies) : enemies[0];

            await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                .FromCard(this, cardPlay)
                .Targeting(target)
                .Execute(choiceContext);
        }
    }
}

/// <summary>基础魔法·土：普通，0 费，技能。[虚无]。获得 10 点格挡。升级 16。</summary>
public sealed class EarthMagic() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Ethereal, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(6m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var creature = Owner?.Creature;
        if (creature == null)
            return;

        await CreatureCmd.GainBlock(creature, DynamicVars.Block, cardPlay);
    }
}

/// <summary>基础魔法·水：普通，0 费，技能。[虚无]。回复 3 点生命。升级 5。</summary>
public sealed class WaterMagic() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Ethereal, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new HealVar(3m)];

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(2m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var creature = Owner?.Creature;
        if (creature == null)
            return;

        await CreatureCmd.Heal(creature, DynamicVars.Heal.BaseValue, false);
    }
}
