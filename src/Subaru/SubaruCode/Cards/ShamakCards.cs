using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 埃尔·纱幕：普通，1 费，技能，目标任意敌人。
/// 使该敌人本回合内失去 2 点[力量]。[阴魔法]。升级：2→3。
///
/// 演进史：
///   1. 最初是「晕眩 1 回合（升级为全体）」，控场太硬。
///   2. 改成纯防御——获得 7 点格挡（升级 10）。但普通稀有度里它和基础「防御」高度重叠。
///   3. 现版本改成「临时力量削减」：1 费换掉敌方本回合 2 点力量，走的是阴魔法
///      「遮蔽 → 让对手打不出伤害」这条线，又不至于像晕眩那样直接封住回合。
///
/// 为什么不是永久削减：普通稀有度 + 1 费的永久力量削减会和「封魔石」（稀有）
/// 抢定位，而且永久削减在长线战斗里滚雪球太强。临时削减只影响这一轮。
///
/// 实现照「食精灵者」（<see cref="SpiritEater"/>）：
///   先给敌人挂 -N 的官方 <see cref="StrengthPower"/>，再挂 <see cref="TempEnemyStrengthDownPower"/> 记账；
///   那个能力在**敌人回合结束时**按 +N 补回去（不是玩家回合结束——敌人是在玩家之后行动的，
///   玩家回合结束就还原等于这张牌毫无作用）。记账能力是隐藏的，敌人状态栏只看到「力量 ↓」。
/// </summary>
public sealed class ElShamak() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<StrengthPower>(2m)];

    protected override void OnUpgrade() => DynamicVars.Strength.UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var down = DynamicVars.Strength.BaseValue;

        await PowerCmd.Apply<StrengthPower>(
            choiceContext, target, -down, Owner.Creature, this, false);
        await PowerCmd.Apply<TempEnemyStrengthDownPower>(
            choiceContext, target, down, Owner.Creature, this, false);

        Godot.GD.Print($"[Subaru] el shamak: {target.Name} -{down} strength this turn");
    }
}
