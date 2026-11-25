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
/// 魂婚术：罕见，1 费，技能。获得 1 层[灵体]。[消耗]。升级：增加[保留]（消耗保留）。
/// </summary>
public sealed class SoulMarriage() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary><c>data/cutin.json</c> 里的极彩色·夜鸣立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "yonaki";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<IntangiblePower>(1m)];

    // 升级只「加」保留，不摘掉消耗
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<IntangiblePower>(
            choiceContext, Owner.Creature, DynamicVars["IntangiblePower"].BaseValue,
            Owner.Creature, this, false);
    }
}
