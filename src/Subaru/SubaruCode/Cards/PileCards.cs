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
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 千里眼：普通，1 费，技能。
/// 从抽牌堆中选 1 张加入手牌（不限顶几张）。升级：选 2 张。
/// </summary>
public sealed class RamFriend() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>拉姆（千里眼）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "ram";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Pick", 1m)];

    protected override void OnUpgrade() => DynamicVars["Pick"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        var count = (int)DynamicVars["Pick"].BaseValue;
        var prompt = new LocString("static_hover_tips", "SUBARU-PICK_CARD.text");
        var prefs = new CardSelectorPrefs(prompt, count);

        var picked = await CardSelectCmd.FromCombatPile(choiceContext, pcs.DrawPile, player, prefs);
        if (picked == null)
            return;

        foreach (var card in picked.ToList())
            await CardPileCmd.Add(card, pcs.Hand, CardPilePosition.Top, null, false);
    }
}

/// <summary>
/// 言灵的加护：罕见，1 费，技能。
/// 抽 2 张牌，然后把 1 张手牌放回抽牌堆顶。升级：抽 2→3。
/// </summary>
public sealed class OttoFriend() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>打出「言灵的加护」时播奥托的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "otto";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Draw", 2m)];

    protected override void OnUpgrade() => DynamicVars["Draw"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        var draws = (int)DynamicVars["Draw"].BaseValue;
        for (var i = 0; i < draws; i++)
            await CardPileCmd.Draw(choiceContext, player);

        var prompt = new LocString("static_hover_tips", "SUBARU-PUT_BACK.text");
        var prefs = new CardSelectorPrefs(prompt, 1);

        var picked = await CardSelectCmd.FromHand(choiceContext, player, prefs, null, this);
        if (picked == null)
            return;

        foreach (var card in picked.ToList())
            await CardPileCmd.Add(card, pcs.DrawPile, CardPilePosition.Top, null, false);
    }
}

/// <summary>
/// 福音书：罕见，1 费，技能。获得 2 点[活力]；下回合开始时再获得 4 点。
/// 升级：2→3、4→6（两段分开升，第二次加成更大）。
/// （2026-11-16 重做：原效果是「从抽牌堆选牌入手」，改成两段活力）
/// （2026-11-18 削弱：原来是两段都 5 / 升级都 8，改成 2+4 / 3+6）
/// </summary>
public sealed class Gospel() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Vigor", 2m),      // 立刻获得
        new IntVar("VigorNext", 4m),  // 下回合开始
    ];

    protected override void OnUpgrade()
    {
        DynamicVars["Vigor"].UpgradeValueBy(1m);     // 2 → 3
        DynamicVars["VigorNext"].UpgradeValueBy(2m); // 4 → 6
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        var amount = (int)DynamicVars["Vigor"].BaseValue;
        var next = (int)DynamicVars["VigorNext"].BaseValue;

        await PowerCmd.Apply<VigorPower>(
            choiceContext, player.Creature, amount, player.Creature, this, false);

        // 下回合开始的那一份走队列，避免回合开始钩子重复结算
        Rewind.TurnStartQueue.AddVigor(next);
    }
}
