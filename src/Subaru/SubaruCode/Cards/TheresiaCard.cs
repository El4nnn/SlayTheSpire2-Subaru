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
/// 死神的加护：稀有，3 费，能力。[腐蚀]。
/// 己方回合结束时，若敌人身上有[虚弱]或[易伤]，把对应层数推到 99；
/// 并给所有敌人叠 [Poison] 层[中毒]（2026-11-21 用户新增：3 层，升级后 4 层）。
/// 升级：费用 3 → 2，中毒 3 → 4。
/// </summary>
public sealed class Theresia() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>特蕾西亚（死神的加护）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "theresia";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<PoisonPower>(3m)];

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);                   // 3 → 2
        DynamicVars.Poison.UpgradeValueBy(1m);      // 3 → 4
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<TheresiaPower>(
            choiceContext, Owner.Creature, DynamicVars.Poison.BaseValue, Owner.Creature, this, false);
    }
}
