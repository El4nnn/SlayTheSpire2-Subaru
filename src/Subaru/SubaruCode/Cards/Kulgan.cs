using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 八臂·库尔刚：罕见，2 费，攻击（单体）。[帝国军]。
/// 造成 3 点伤害 4 次，无视[格挡]，**吃力量**。升级：每段 3→5（段数不变，还是 4 段）。
///
/// <para><b>为什么 props 要写 <c>Move | Unblockable</c></b>：
/// 力量在 <c>StrengthPower.ModifyDamageAdditive</c> 里过 <c>ValuePropExtensions.IsPoweredAttack</c>，
/// 判据是 <c>HasFlag(Move=8) &amp;&amp; !HasFlag(Unpowered=4)</c>。
/// 只写 <c>Unblockable=2</c> 的话既不格挡也不吃力量（「最优骑士·尤利乌斯」就是那样）。
/// 这里要「无视格挡 + 吃力量」，所以两个标志都要：<c>Move | Unblockable</c>。</para>
///
/// <para>执行时还要显式给 <c>cmd.DamageProps</c> —— 光在 <c>DamageVar</c> 上标 props
/// 不会自动带到 <c>DamageCmd.Attack</c>（卡面预览用的是 DamageVar 的 props，两者必须一致，
/// 否则会出现「卡面显示 X、实际打出 Y」）。</para>
///
/// <para>多段用 <c>WithHitCount(4)</c>：每段各自结算力量/易伤。</para>
/// </summary>
public sealed class Kulgan() : SubaruCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    public override string? CutInId => "kulgan";

    private const int Hits = 4;

    /// <summary>无视格挡 + 吃力量（详见类注释）。</summary>
    private const ValueProp Props = ValueProp.Move | ValueProp.Unblockable;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(3m, Props),
        // 段数写成 DynamicVar，卡面 {Hits} 才会跟着代码走（别在文案里写死「4 次」）
        new IntVar("Hits", Hits),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);   // 3 → 5

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var cmd = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue);

        cmd.DamageProps = Props;

        await cmd.Execute(choiceContext);
    }
}
