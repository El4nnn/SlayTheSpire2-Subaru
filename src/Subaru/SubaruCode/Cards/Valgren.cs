using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 黑龙·瓦尔格伦：罕见，2 费，技能（全体）。对所有敌人造成 3 次伤害，再施加[中毒]。
/// 升级：每段 5 → 7，中毒 6 → 8。
///
/// 伤害是 DamageVar + <c>WithHitCount(3)</c>：卡面写「共 3 次」，实际由引擎逐段结算，
/// 力量/易伤/附魔按段生效。它是技能牌，所以吃不到[锋锐]/[本能]（那两个只认攻击牌），
/// 但力量照样吃 —— 因为 DamageVar 的 props 是 <c>Move</c>（powered）。
/// </summary>
public sealed class Valgren() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
{
    private const int Hits = 3;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(5m, ValueProp.Move),
        new PowerVar<PoisonPower>(6m),
        // 段数写成 DynamicVar，卡面 {Hits} 才会跟着代码走（别在文案里写死「3 次」）
        new IntVar("Hits", Hits),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2m);   // 5 → 7
        DynamicVars.Poison.UpgradeValueBy(2m);   // 6 → 8
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        var player = Owner;
        if (state == null || player == null)
            return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(state)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);

        // 伤害结算完再上毒：死的敌人已经不在 HittableEnemies 里，不浪费层数
        var enemies = state.HittableEnemies.ToList();
        foreach (var enemy in enemies)
        {
            await PowerCmd.Apply<PoisonPower>(
                choiceContext, enemy, DynamicVars.Poison.BaseValue, player.Creature, this, false);
        }

        Godot.GD.Print($"[Subaru] valgren: {Hits} hits x {DynamicVars.Damage.BaseValue}, "
                       + $"poison {DynamicVars.Poison.BaseValue} to {enemies.Count} enemies");
    }
}
