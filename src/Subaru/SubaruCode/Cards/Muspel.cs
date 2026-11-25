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
/// 石块·姆斯佩尔：罕见，2 费，技能。获得 15 点格挡。升级：15→20。
///
/// 额外规则：本回合如果打出过[食精灵者]，这张牌本回合费用为 0。
/// 由食精灵者自己用原生 CardEnergyCost.SetThisTurn(0) 改写手牌里这些牌的费用，
/// 引擎的 EndOfTurnCleanup 会在回合结束时自动清掉。
/// </summary>
public sealed class Muspel() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(15m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5m); // 15 → 20

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
    }
}
