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
/// 帕克：普通，1 费，技能。本回合获得 6 点格挡；下回合开始时再获得 6 点格挡。
/// 升级：两段都 6→8。
///
/// 「下回合的格挡」走 TurnStartQueue，和硬撑的脆弱是同一条路：
/// 直接在回合开始钩子里加会被多个钩子重复加一次。
/// </summary>
public sealed class Puck() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    /// <summary>立绘 cut-in（2026-11-23 接入，半身构图，人物约占幕布高度 72%）。</summary>
    public override string? CutInId => "puck";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(2m); // 6 → 8

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        TurnStartQueue.AddBlock((int)DynamicVars.Block.BaseValue);
    }
}
