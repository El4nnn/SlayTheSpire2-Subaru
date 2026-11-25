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
/// 不完全 E·M·T：普通，1 费，技能。[阴魔法]。获得 8 点格挡。升级：8→12。
///
/// 和贝蒂那张 E·M·T 不同：这张是普通的卡池牌，不进「生成专属」集合，
/// 能吃「阴魔法」的减费，也不带[消耗]。
/// </summary>
public sealed class IncompleteEmt() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m); // 8 → 12

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }
}
