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
/// 拉米亚·戈德温：罕见，1 费，能力。[尸兵]。
/// 获得 3 点[力量]。升级：3→4。
///
/// <para>和「纱幕」的虚弱/易伤同一套：数值走 <c>PowerVar&lt;StrengthPower&gt;</c>，
/// 卡面用 <c>{StrengthPower:diff()}</c>，升级只动 <c>DynamicVars.Strength</c>，
/// 卡面数字与实际授予的层数同源。</para>
///
/// <para>稀有度：需求里没写，按原版「灼热攻击 / Inflame」那一档（1 费 +3 力量）定为**罕见**，
/// 要改说一声。</para>
/// </summary>
public sealed class Lamia() : SubaruCard(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    public override string? CutInId => "lamia";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<StrengthPower>(3m),
    ];

    protected override void OnUpgrade() => DynamicVars.Strength.UpgradeValueBy(1m);   // 3 → 4

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner;
        if (owner?.Creature == null)
            return;

        var amount = DynamicVars.Strength.BaseValue;

        Godot.GD.Print($"[Subaru] lamia played: +{amount} strength");

        await PowerCmd.Apply<StrengthPower>(
            choiceContext, owner.Creature, amount, owner.Creature, this, false);
    }
}
