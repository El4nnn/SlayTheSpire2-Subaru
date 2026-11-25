using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 过路魔·塞蕾丝缇雅：罕见，2 费，攻击（单体），[消耗]。
/// 造成 N 段伤害，每段 {Times} 点，N = 本场战斗「造成过伤害」的次数。
/// 升级：每段 1 → 3。
///
/// 计数挂在初始遗物的 AfterDamageGiven 上（CombatStats.DamageGiven），
/// 并用存档点快照里的值做死归回滚 —— 所以死归之后 N 会退回存档点那一刻。
///
/// 2026-11-18 改成「N 段 × 每段 Times」：以前是「一次打 N×Times 的总量」，
/// 力量 / 锋利都只加一次。改成分段后它们按段结算（N 段就加 N 次），
/// 和卡面「N × 每段」的写法一致 —— 这是刻意保留的设计，不是 bug。
/// </summary>
public sealed class Celestia() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    public override string? CutInId => "celestia";

    // 用 DamageVar（不是 IntVar）：DamageVar 才会走 UpdateCardPreview，
    // 卡面 {Times:diff()} 才能显示锋利 / 本能之类附魔带来的变化。
    //
    // Hits 是本场战斗「造成过伤害的次数」（= 段数 N），每次卡面渲染现算：
    // 用 <see cref="CelestiaHitsVar"/> 的 ToString 覆盖，和首席骑士那张牌的
    // FirstKnightUsedVar 同一个套路（卡面取一次值就重算，不用去找节点刷新）。
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("Times", 1m, ValueProp.Move),
        new CelestiaHitsVar(),
    ];

    protected override void OnUpgrade() => DynamicVars["Times"].UpgradeValueBy(2m); // 1 → 3

    /// <summary>自己走 SpellVolley.PlayAsync（每发命中单独结算），不要再播通用动画。</summary>
    public override bool HandlesOwnVolley => true;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var perHit = DynamicVars["Times"].BaseValue;
        var hits = CombatStats.DamageGiven;

        Godot.GD.Print($"[Subaru] celestia: N={hits}, per={perHit}");

        if (hits <= 0)
            return;

        // 弹幕结算：每发水晶命中时单独打一段（开关见 SpellVolley.Enabled）
        if (Vfx.SpellVolley.Enabled)
        {
            await Vfx.SpellVolley.PlayAsync(choiceContext, this, cardPlay, cardPlay.Target, hits, perHit);
            return;
        }

        await DamageCmd.Attack(perHit)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount(hits)
            .Execute(choiceContext);
    }
}

/// <summary>
/// 塞蕾丝缇雅的「本次打几段」：卡面 <c>{Hits}</c> 的取值。
/// <see cref="ToString"/> 每次渲染现算（<c>CombatStats.DamageGiven</c> = 本场造成过伤害的次数），
/// 所以手牌 / 抽牌堆里的这张牌会跟着计数一起变，不需要手动刷新节点。
///
/// ⚠️ 必须继承 <c>IntVar</c> 而不是 <c>StringVar</c>：第三方 mod 枚举动态变量时
/// 遇到没登记的字符串变量会抛异常，把整个回合循环搞死（详见 FirstKnightUsedVar 的注释）。
/// </summary>
public sealed class CelestiaHitsVar() : IntVar("Hits", 0m)
{
    public override string ToString() => Rewind.CombatStats.DamageGiven.ToString();
}
