using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 白蜘蛛·奇夏：稀有，1 费（升级 0 费），技能，自身。[九神将]。
/// 不论其他[九神将]牌在哪个牌堆，把它们全部放进手牌 ——
/// **但已经进[消耗牌堆]的白蜘蛛不再召回**（2026-11-18 削弱：否则两张白蜘蛛可以无限互相召回）。
///
/// 写法照原版「征召上前」（SummonForth）：扫 PlayerCombatState.AllCards，
/// 挑出不在手上的目标，再一次性 CardPileCmd.Add 到手上。
/// </summary>
public sealed class Kishia() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary><c>data/cutin.json</c> 里的白蜘蛛立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "kishia";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals, CardKeyword.Exhaust];

    // 升级只减费：1 → 0
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (pcs == null)
            return;

        var hand = pcs.Hand.Cards;
        var exhaust = pcs.ExhaustPile.Cards;

        var others = pcs.AllCards
            .Where(c => c != null
                        && !ReferenceEquals(c, this)
                        && c is SubaruCard subaru
                        && subaru.CanonicalKeywords.Contains(SubaruKeywords.NineGenerals)
                        && !hand.Contains(c)
                        // 削弱：消耗牌堆里的白蜘蛛不再被召回
                        && !(c is Kishia && exhaust.Contains(c)))
            .ToList();

        if (others.Count == 0)
        {
            Godot.GD.Print("[Subaru] kishia: no other nine generals to fetch");
            return;
        }

        await CardPileCmd.Add(others, PileType.Hand, CardPilePosition.Top, null, false);

        Godot.GD.Print($"[Subaru] kishia: fetched {others.Count} nine generals to hand");
    }
}
