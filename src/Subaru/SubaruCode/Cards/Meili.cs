using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 魔兽使·梅莉：<b>多人模式专属</b>，1 费，罕见，技能，[消耗]。
/// 使一名玩家在本回合内**免疫所有敌人造成的伤害**（升级：费用 1→0）。
///
/// <para>目标用 <see cref="TargetType.AnyAlly"/>：引擎的 <c>CardModel.IsValidTarget</c> 对 AnyAlly 的判定是
/// 「target.Side == 自己的 Side」，所以队友和**自己**都能选（召唤物同侧也能选，实际不会有人这么用）。</para>
///
/// <para>只覆盖伤害：减益拦不住（<c>TryModifyPowerAmountReceived</c> 是 internal virtual，外部 override 不了），详见能力类的注释。</para>
///
/// <para>「本回合」的边界由 <see cref="BeastTamerGuardPower"/> 自己数：能力挂在目标身上，
/// 到**下一个玩家回合开始**才移除 —— 也就是把敌方回合也覆盖进去（敌人正是在那时出手）。</para>
///
/// <para>为什么不进单人牌池：单人没有队友，这张牌只能给自己，等于一个 1 费单体无敌，破坏原有数值。
/// 卡池过滤见 <c>SubaruCardPool</c>。</para>
/// </summary>
public sealed class Meili() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyAlly)
{
    /// <summary>梅莉（魔兽使）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "meili";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    /// <summary>升级只降费：1 → 0。</summary>
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target;
        if (target == null)
            return;

        await PowerCmd.Apply<BeastTamerGuardPower>(
            choiceContext, target, 1m, Owner.Creature, this, false);

        GD.Print($"[Subaru] mp: meili guard applied to {target.Name}");
    }
}
