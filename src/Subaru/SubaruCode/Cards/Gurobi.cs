using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 咒具师·葛路比：普通，1 费，技能，目标任意敌人。
/// 给予该敌人 6 层[中毒]；若该敌人有[虚弱]，额外给予 2 层[中毒]。
/// 升级：6→8、额外 2→3。
/// </summary>
public sealed class Gurobi() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary><c>data/cutin.json</c> 里的咒具师·葛路比立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "gurobi";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<PoisonPower>(6m),
        new IntVar("WeakBonus", 2m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Poison.UpgradeValueBy(2m);          // 6 → 8
        DynamicVars["WeakBonus"].UpgradeValueBy(1m);    // 2 → 3
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;

        await PowerCmd.Apply<PoisonPower>(
            choiceContext, target, DynamicVars.Poison.BaseValue, Owner.Creature, this, false);

        // 目标带[虚弱]时追加
        if (target.HasPower<WeakPower>())
        {
            await PowerCmd.Apply<PoisonPower>(
                choiceContext, target, DynamicVars["WeakBonus"].BaseValue, Owner.Creature, this, false);

            Godot.GD.Print($"[Subaru] gurobi: weak bonus +{DynamicVars["WeakBonus"].BaseValue} poison");
        }
    }
}
