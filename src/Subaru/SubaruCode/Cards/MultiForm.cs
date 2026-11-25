using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 复数形态：罕见，1 费，技能。[尸兵]。
/// 下一张打出的[尸兵]多打 1 次（即打两次）。升级：「多打 2 次」（打三次）。
///
/// <para>数值走 <c>PowerVar&lt;MultiFormPower&gt;</c>：Amount 就是「多打几次」，
/// 卡面用 <c>{MultiFormPower:diff()}</c>，实际加成也读同一个值。</para>
///
/// <para>消费逻辑在 <c>MultiFormPlayCountPatch</c>（给 <c>Hook.ModifyCardPlayCount</c> 挂 postfix）。</para>
/// </summary>
public sealed class MultiForm() : SubaruCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<MultiFormPower>(1m),
    ];

    protected override void OnUpgrade() => DynamicVars["MultiFormPower"].UpgradeValueBy(1m);   // 1 → 2

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner;
        if (owner?.Creature == null)
            return;

        var extra = DynamicVars["MultiFormPower"].BaseValue;

        Godot.GD.Print($"[Subaru] multi form played: next corpse soldier +{extra} play(s)");

        await PowerCmd.Apply<MultiFormPower>(
            choiceContext, owner.Creature, extra, owner.Creature, this, false);
    }
}
