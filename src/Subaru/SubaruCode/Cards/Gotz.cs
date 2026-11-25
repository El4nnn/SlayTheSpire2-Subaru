using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 狮子骑士·哥兹：普通，1 费，攻击（群体）。[九神将]。
/// 对所有敌人造成 6 点伤害；你的卡组中每有一张[九神将]，伤害 +2。
/// 升级：基础 6→8，每张加成 2→3。
///
/// 计数方式照原版「新月长矛」：用 CalculationBase / ExtraDamage / CalculatedDamage
/// 这一组动态变量，卡面显示的数字会在渲染时按当前卡组重算。
///
/// ⚠️ 2026-11-17 踩过的坑：**那个「每张加成」的变量必须用 <c>ExtraDamageVar</c>**，不能用
/// <c>IntVar("ExtraDamage", …)</c>。原版 <c>CalculatedDamageVar</c> 每次算数都走
/// <c>CalculatedVar.Calculate</c> → <c>GetExtraVar()</c> → <c>DynamicVarSet.get_ExtraDamage()</c>，
/// 而后者是 `(ExtraDamageVar)_vars["ExtraDamage"]` 的**硬转**：
///   InvalidCastException: Unable to cast IntVar to ExtraDamageVar
/// 抛在 <c>NCard.UpdateVisuals</c> 里 → 卡牌奖励界面 <c>_Ready</c> 直接中断，表现是
/// 「奖励只有一张卡、且没有跳过按钮」。只有屏幕上出现这张牌才会触发，所以平时看不出来。
/// 顺带：只用 ExtraDamage 当唯一来源，卡面显示和实际伤害就不会各算一套。
/// </summary>
public sealed class Gotz() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary><c>data/cutin.json</c> 里的狮子骑士·哥兹立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "gotz";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(6m),
        new ExtraDamageVar(2m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CountNineGenerals),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(2m);   // 6 → 8
        DynamicVars.ExtraDamage.UpgradeValueBy(1m);       // 2 → 3
    }

    /// <summary>数「你这边有几张[九神将]」——和「新月长矛」一样数当前战斗里的所有牌。</summary>
    private static decimal CountNineGenerals(CardModel card, Creature target)
    {
        var pcs = card?.Owner?.PlayerCombatState;
        if (pcs == null)
            return 0m;

        return pcs.AllCards.Count(c => c is SubaruCard s
                                       && s.CanonicalKeywords.Contains(SubaruKeywords.NineGenerals));
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var count = CountNineGenerals(this, null);
        var damage = DynamicVars.CalculationBase.BaseValue
                     + DynamicVars.ExtraDamage.BaseValue * count;

        // 2026-11-18 削弱：从「群体全额」改成「单体全额 + 其他敌人半额」。
        var splash = damage / 2m;

        Godot.GD.Print($"[Subaru] gotz: {count} nine generals => {damage} to target, {splash} to others");

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        // 溅射：除主目标外的其他敌人各吃一半。走 CreatureCmd.Damage 而不是第二条攻击指令，
        // 免得同一张牌触发两次「攻击牌」类效果（力量/易伤等由引擎各自结算一次）。
        var others = state.HittableEnemies
            .Where(e => !ReferenceEquals(e, cardPlay.Target))
            .Cast<Creature>()
            .ToList();

        if (others.Count == 0 || splash <= 0m)
            return;

        await CreatureCmd.Damage(
            choiceContext, others, splash, ValueProp.Move, Owner?.Creature);
    }
}
