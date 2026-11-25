using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 去诅咒：普通，0 费，技能。[贝蒂][保留][消耗]。升级：1 → 2 张。
/// 消耗最多 1 张[状态]牌或[诅咒]牌；[魔女瘴气]除外——
/// 它是魔女的权能，不是普通诅咒，也只能被明确写它的牌（英雄 / 首席骑士 / 契约·贝蒂）清掉。
/// </summary>
public sealed class RemoveCurse() : SubaruCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Retain, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Count", 1m)];

    protected override void OnUpgrade() => DynamicVars["Count"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        // 手里没有能清的牌就不弹选择框
        if (!pcs.Hand.Cards.Any(IsRemovable))
        {
            Godot.GD.Print("[Subaru] remove curse: nothing to remove");
            return;
        }

        var max = (int)DynamicVars["Count"].BaseValue;
        var prompt = new LocString("static_hover_tips", "SUBARU-REMOVE_CURSE.text");
        var prefs = new CardSelectorPrefs(prompt, 0, max);

        var picked = await CardSelectCmd.FromHand(
            choiceContext, player, prefs, c => c != this && IsRemovable(c), this);

        if (picked == null)
            return;

        var count = 0;
        foreach (var card in picked.ToList())
        {
            await CardCmd.Exhaust(choiceContext, card, false, false);
            count++;
        }

        Godot.GD.Print($"[Subaru] remove curse: exhausted {count} status/curse");
    }

    /// <summary>状态牌 / 诅咒牌，但[魔女瘴气]不吃普通解咒。</summary>
    private static bool IsRemovable(CardModel card) =>
        card is not CurseWitchMiasma
        && (card.Type == CardType.Status || card.Type == CardType.Curse);
}
