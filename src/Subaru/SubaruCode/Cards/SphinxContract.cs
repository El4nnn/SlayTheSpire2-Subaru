using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 大灾·斯芬克斯：先古，2 费，能力。大灾阵营的契约牌。
/// 每造成 14 点伤害，将一张随机[尸兵]加入手牌；这张[尸兵]本回合费用 -1，并获得[消耗]。
/// 升级：**阈值不变**，改成加入手牌的[尸兵]是升级版。
///
/// <para>词条同「契约·贝蒂 / 鲜血帝·文森特」：<see cref="IsGenerateOnly"/>（只由涅奥契约页发放）、
/// [保留][永恒]，并且进了 <c>CardCreationPolicy.IsProtected</c>（战斗内不可被复制）。</para>
///
/// <para><b>升级标记怎么传给能力</b>：能力的实例字段在 <c>BattleSnapshot</c> 回档时会丢
/// （快照只恢复 <c>Amount</c>，见 VincentPower 的注释），所以把「升没升级」**放进 Amount**：
/// <c>Apply(..., 1 或 2, ...)</c>。<c>StackType.Single</c> 不显示数字，玩家看不到这个标记。</para>
/// </summary>
public sealed class SphinxContract() : SubaruCard(2, CardType.Power, CardRarity.Ancient, TargetType.Self)
{
    /// <summary>打出这张牌时播斯芬克斯的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "sphinx";

    /// <summary>只从开局的涅奥契约页发放，不进卡池。</summary>
    public override bool IsGenerateOnly => true;

    // 2026-11-18：只移除[固有]（[保留]留着），改成「第一回合开始后直接进手牌」
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Contract, CardKeyword.Retain, CardKeyword.Eternal];

    public override bool EntersHandAtCombatStart => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Threshold", 15m),
    ];

    // 升级不改阈值：收益转移到「发出来的尸兵是升级版」（由能力的 GrantsUpgraded 判断）
    protected override void OnUpgrade()
    {
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner;
        if (owner?.Creature == null)
            return;

        var threshold = DynamicVars["Threshold"].BaseValue;

        Godot.GD.Print($"[Subaru] sphinx contract played: threshold={threshold}, upgraded={IsUpgraded}");

        // Amount 装的是「升级标记」（1 = 普通 / 2 = 升级），不是阈值
        await PowerCmd.Apply<SphinxPower>(
            choiceContext, owner.Creature, IsUpgraded ? 2m : 1m, owner.Creature, this, false);
    }
}
