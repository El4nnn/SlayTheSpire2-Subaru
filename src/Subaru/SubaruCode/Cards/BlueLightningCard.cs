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
/// 蓝色闪电：稀有，3 费，能力（群体）。对所有敌人施加 1 层[缓慢]。升级：费用 3→2。
/// </summary>
public sealed class BlueLightning() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.AllEnemies)
{
    /// <summary><c>data/cutin.json</c> 里的蓝色闪电·塞西鲁斯立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "cecilus";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<SlowPower>(1m)];

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        foreach (var enemy in state.HittableEnemies)
            await PowerCmd.Apply<SlowPower>(
                choiceContext, enemy, DynamicVars["SlowPower"].BaseValue, Owner.Creature, this, false);
    }
}
