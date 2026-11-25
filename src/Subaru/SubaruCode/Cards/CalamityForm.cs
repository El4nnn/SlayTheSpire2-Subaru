using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 大灾形态：稀有，3 费，能力（自身）。每打出一张[尸兵]，给予所有敌人 7 层[中毒]。
/// 升级：费用 3 → 2（<c>EnergyCost.UpgradeBy</c>，中毒层数不变）。
///
/// <para><b>阵营归属</b>：这张牌属于大灾阵营，但**不是**[尸兵]，所以不能靠
/// <c>CorpseSoldier</c> 词条来做路线过滤（那会把它自己塞进斯芬克斯的随机池、
/// 也会被「尸兵费用 -1」之类效果命中）。改用 <see cref="IsAllowedOnPath"/> 直接按路线拦。</para>
/// </summary>
public sealed class CalamityForm() : SubaruCard(3, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>打出这张牌时播斯芬克斯的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "sphinx";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<CalamityFormPower>(7m)];

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1); // 3 → 2

    public override bool IsAllowedOnPath(Runs.SubaruPath path)
        => path is Runs.SubaruPath.None or Runs.SubaruPath.Calamity;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null)
            return;

        await PowerCmd.Apply<CalamityFormPower>(
            choiceContext, player.Creature,
            DynamicVars["CalamityFormPower"].BaseValue,
            player.Creature, this, false);
    }
}
