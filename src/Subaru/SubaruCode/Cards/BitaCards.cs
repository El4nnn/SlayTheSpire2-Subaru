using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 穆拉克：普通，**2 费**，技能。获得 1 层[浮空]（受到的伤害减半 1 个回合）。[阴魔法][消耗]。
/// 升级：费用 2→1（不再加层数）。
///
/// 2026-11-17 削弱：浮空改成「层数 = 剩余回合、减伤恒 50%」之后，1 费一张的永久半伤太强，
/// 改成 2 费 + [消耗]，升级只减费。
/// </summary>
public sealed class Murak() : SubaruCard(2, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<FloatPower>(1m)];

    // 升级只减费：2 → 1
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<FloatPower>(
            choiceContext, Owner.Creature, DynamicVars["FloatPower"].BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>布道：普通，0 费，技能。获得 3 点力量，回合结束时失去。升级：3→5。</summary>
public sealed class Sermon() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<StrengthPower>(3m)];

    protected override void OnUpgrade() => DynamicVars.Strength.UpgradeValueBy(2m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 临时力量：先加力量，再挂「回合结束扣回去」的状态（原版 TemporaryStrengthPower 不保证可用）
        await PowerCmd.Apply<StrengthPower>(
            choiceContext, Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this, false);
        await PowerCmd.Apply<TempStrengthPower>(
            choiceContext, Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this, false);
    }
}
