using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 飞龙将·玛德琳：罕见，2 费，攻击（单体）。造成 15 点伤害；
/// [力量]在这张牌上发挥 3 倍效果（升级 5 倍）。
///
/// 引擎对攻击牌只按 1 倍加一次力量，所以多出来的 (倍数-1) 倍要自己并进基础伤害：
///     实际伤害 = 15 + 力量 × 1（引擎）+ 力量 × (倍数-1)（自己补）= 15 + 力量 × 倍数
///
/// 卡面显示的伤害必须跟着一起算，否则卡面写 15、实际打出 15+力量×3。
/// 做法照哥兹，用 CalculationBase / ExtraDamage / CalculatedDamage 这一组变量：
///   · CalculationBase = 15（基础伤害）
///   · ExtraDamage     = 1（占位，真正的倍数由 WithMultiplier 提供）
///   · 乘数函数        = 力量 × (倍数-1)
/// 于是 <c>CalculatedVar.Calculate</c> = 15 + 力量 × (倍数-1)，
/// 而 <c>CalculatedDamageVar.UpdateCardPreview</c> 还会再走一次 <c>ModifyDamage</c>
/// （引擎自动加的那 1 倍力量），所以预览/卡面数字正好等于实际伤害 15 + 力量 × 倍数。
///
/// ⚠️ `ExtraDamage` 必须用 <c>ExtraDamageVar</c>，不能用 <c>IntVar("ExtraDamage", …)</c>：
/// 原版 <c>DynamicVarSet.get_ExtraDamage</c> 是硬转，抛 <c>InvalidCastException</c>
/// 会打断 <c>NCard.UpdateVisuals</c>，表现为卡牌奖励界面只剩一张卡、没有跳过按钮。
/// （2026-11-17 在哥兹身上踩过，详见 docs\交接_技术问题_2026-11-18.md）
/// </summary>
public sealed class Madeleine() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>立绘 cut-in（2026-11-22 接）：飞龙将·玛德琳，攻击牌但只接立绘，不接斩击。</summary>
    public override string? CutInId => "madeleine";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(15m),
        new ExtraDamageVar(1m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(ExtraStrengthFactor),
        new IntVar("StrengthMult", 3m),
    ];

    // 升级只改倍数：3 → 5
    protected override void OnUpgrade() => DynamicVars["StrengthMult"].UpgradeValueBy(2m);

    /// <summary>
    /// 卡面与实际共用的算式：力量里「多出来的那 (倍数-1) 份」。
    /// 必须 static —— 原版 <c>WithMultiplier</c> 会检查乘数函数是静态的。
    /// </summary>
    private static decimal ExtraStrengthFactor(CardModel card, Creature target)
    {
        var creature = card?.Owner?.Creature;
        if (creature == null)
            return 0m;

        var mult = card.DynamicVars["StrengthMult"].BaseValue;
        return creature.GetPowerAmount<StrengthPower>() * (mult - 1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var mult = DynamicVars["StrengthMult"].BaseValue;
        var strength = Owner.Creature.GetPowerAmount<StrengthPower>();

        // 只把「多出来的 (倍数-1) 份」并进基础伤害，剩下那 1 份由引擎照常加。
        var bonus = strength * (mult - 1m);
        var damage = DynamicVars.CalculationBase.BaseValue + bonus;

        Godot.GD.Print($"[Subaru] madeleine: base {DynamicVars.CalculationBase.BaseValue} + strength {strength} x{mult} => {damage}, engine adds {strength} more");

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }
}
