using System.Collections.Generic;
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
/// 天剑·劳安：普通，2 费，攻击（单体）。[尸兵]。
/// 造成 13 点伤害；**可以无限次升级**（参考塔一「灼热攻击」）。
/// 升级量：第 1 次 +4、第 2 次 +6、第 3 次 +8……每次比上一次多 2。
/// 卡名后缀：0 级无、1 级「+」、2 级「+2」、3 级「+3」……
///
/// <para><b>无限升级怎么开</b>：引擎 <c>IsUpgradable = CurrentUpgradeLevel &lt; MaxUpgradeLevel</c>，
/// 所以只要把 <c>MaxUpgradeLevel</c> 覆写成一个很大的数，篝火/事件就能反复升级同一张牌。
/// <c>UpgradeInternal</c> 的顺序是「先 CurrentUpgradeLevel++，再 OnUpgrade()」，
/// 所以 OnUpgrade 里拿到的等级就是「第几次升级」。</para>
///
/// <para><b>伤害为什么不走 UpgradeValueBy</b>：<c>UpgradeInternal</c> 升完会调
/// <c>DynamicVars.RecalculateForUpgradeOrEnchant()</c>，多次累加的裸值可能被重算回去。
/// 改成 <c>CalculatedDamageVar</c> + 乘数函数「按当前等级现算」：
/// 累计加成 = 4k + k(k-1) = k² + 3k（k = 等级），
/// 于是 0/1/2/3 级分别是 13 / 17 / 23 / 31（每次增量 4/6/8）。卡面与实际同一个算式。</para>
///
/// <para><b>2026-11-23 更正（上面那套已废）</b>：用户报「升级后卡面数字不变」。
/// 根因在引擎 IL：<c>CalculatedVar::Calculate</c> 是
/// <c>CalculationBase.BaseValue + CalculationExtra.BaseValue * multiplier</c>，
/// 而 <c>CalculatedVar::RecalculateForUpgradeOrEnchant</c> 只在
/// <c>CalculationBase</c> 变了的时候才同步 <c>BaseValue</c> 并打 <c>WasJustUpgraded</c>。
/// 也就是说**升级必须落在 DynamicVars 上**，只改「等级→乘数」引擎看不见，卡面永远停在 13。
/// 现在改成：<c>OnUpgrade</c> 直接 <c>CalculationBase.UpgradeValueBy(2k+2)</c>（k = 刚升到的等级），
/// 乘数固定 0，伤害与卡面都用 <c>CalculationBase.BaseValue</c> 这一个来源。</para>
///
/// <para>⚠️ 乘数函数必须 static（<c>WithMultiplier</c> 会检查），即使是常量 0 也得写成 static。</para>
/// </summary>
public sealed class Luan() : SubaruCard(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary>升级上限：本质是「无上限」，给个大数只是为了让 <c>IsUpgradable</c> 一直为 true。</summary>
    private const int MaxLevel = 99;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    public override string? CutInId => "luan";

    public override int MaxUpgradeLevel => MaxLevel;

    // table-upgrade: 可无限升级；第 k 次 +（2k+2）：+4、+6、+8…
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(13m),
        new ExtraDamageVar(1m),
        // 乘数固定 0：伤害全在 CalculationBase 里，升级才能被引擎的 Recalculate 看见
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(ZeroBonus),
    ];

    /// <summary>
    /// 第 k 次升级 +（2k+2）：1 级 +4、2 级 +6、3 级 +8……（<c>UpgradeInternal</c> 是
    /// 先 <c>CurrentUpgradeLevel++</c> 再调 <c>OnUpgrade</c>，所以这里拿到的就是 k）。
    ///
    /// <para>必须改 <c>CalculationBase</c> 本身：引擎的 <c>CalculatedVar.RecalculateForUpgradeOrEnchant</c>
    /// 只在它变化时才同步并刷新卡面（2026-11-23 用户报「升级后数字不变」的根因）。</para>
    /// </summary>
    protected override void OnUpgrade()
    {
        var k = CurrentUpgradeLevel;
        DynamicVars.CalculationBase.UpgradeValueBy(2m * k + 2m);
    }

    /// <summary>乘数占位（<c>CalculatedDamageVar</c> 强制要求一个乘数，给 0 即「不加成」）。</summary>
    private static decimal ZeroBonus(CardModel card, Creature target) => 0m;

    /// <summary>
    /// 卡名后缀：0 级无、1 级「+」、2 级「+2」、3 级「+3」……
    ///
    /// <para>引擎默认给「+1 / +2 / +3」（<c>Title</c> 里直接拼 <c>CurrentUpgradeLevel</c>），
    /// 但塔一的灼热攻击第一次升级是光秃秃一个「+」，所以这里自己接管。</para>
    /// </summary>
    public override string Title
    {
        get
        {
            var baseTitle = TitleLocString.GetFormattedText();
            var level = CurrentUpgradeLevel;

            if (level <= 0)
                return baseTitle;

            return level == 1 ? baseTitle + "+" : baseTitle + "+" + level;
        }
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        System.ArgumentNullException.ThrowIfNull(cardPlay.Target);

        // 伤害与卡面同一个来源：升级时被 UpgradeValueBy 抬高的 CalculationBase
        var damage = DynamicVars.CalculationBase.BaseValue;

        Godot.GD.Print($"[Subaru] luan played: level={CurrentUpgradeLevel}, damage={damage}");

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }
}
