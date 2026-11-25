using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「腐蚀」牌的用量记账。
///
/// 规则：带[腐蚀]的牌整场战斗只能打出一次；打出之后如果再死归，
/// 这张牌从本场战斗移除（战斗结束回到牌组，下一场又能用一次）。
/// 它授予的能力不受影响——那部分由 Corrosion/ICorrodedPower 负责跨轮回保留。
///
/// 没有这条，死归把牌堆还原之后就能反复打出腐蚀牌、无限叠加能力。
/// </summary>
public static class CorrodedCards
{
    private static readonly HashSet<CardModel> Used = new();

    /// <summary>这张牌是不是带[腐蚀]的牌。</summary>
    public static bool IsCorroded(CardModel? card)
    {
        return card is SubaruCard subaru && subaru.CanonicalKeywords.Contains(SubaruKeywords.Corrosion);
    }

    /// <summary>打出时记账。</summary>
    public static void MarkUsed(CardModel? card)
    {
        if (!IsCorroded(card) || card == null)
            return;

        if (Used.Add(card))
            GD.Print("[Subaru] corroded card used: " + card.Id);
    }

    /// <summary>本场战斗是否已经打过这张牌。</summary>
    public static bool IsUsed(CardModel? card)
    {
        return card != null && Used.Contains(card);
    }

    /// <summary>战斗结束后清账（由 DeathReturnSpike.ResetForCombat 调用）。</summary>
    public static void Reset()
    {
        Used.Clear();
    }

    /// <summary>
    /// 死归回档之后调用：把「本场打过的腐蚀牌」从战斗里清干净。
    ///
    /// 两种形态：
    ///   · 能力牌：打出后实例已被销毁，BattleSnapshot 不会重建它 → 这里查不到活实例，什么都不用做
    ///   · 技能/攻击牌：实例还活着（在弃牌堆里）→ 丢进**消耗堆**（战斗结束照原版规则回牌组），
    ///     绝不能 Add 到 Deck：牌组里原本那张一直在，塞进去就是复制（0.1.4 之前「越死归越多张」就是这么来的）。
    /// </summary>
    public static async Task Purge(Player player)
    {
        if (Used.Count == 0)
            return;

        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return;

        foreach (var card in Used.ToList())
        {
            if (card == null)
                continue;

            // 只按引用找：同原型的另一张合法副本不该被牵连
            var live = pcs.AllCards.FirstOrDefault(c => c != null && ReferenceEquals(c, card));
            if (live == null)
            {
                GD.Print("[Subaru] corroded card confirmed gone: " + card.Id);
                continue;
            }

            // 已经在消耗堆里就不用再搬
            if (pcs.ExhaustPile.Cards.Any(c => c != null && ReferenceEquals(c, live)))
                continue;

            try
            {
                await CardPileCmd.Add(live, PileType.Exhaust, CardPilePosition.Top, null, false);
                GD.Print("[Subaru] corroded card moved to exhaust after rewind: " + live.Id);
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] corroded purge failed: " + e);
            }
        }
    }
}
