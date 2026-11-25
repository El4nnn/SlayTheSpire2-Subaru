using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 腐蚀牌「本场战斗只能用一次」的兜底：
/// 已经打出去、并被死归丢进消耗堆的腐蚀牌，不能靠「从消耗堆取回」这类效果重新拿到手里。
///
/// 两层：
///   1) 选择层：从消耗堆选牌时把腐蚀牌从可选项中剔除（否则会出现「能点、点了没反应」）。
///   2) 搬运层：万一有别的路径直接搬（不经过选牌界面），在 CardPileCmd.Add 这里挡掉。
///
/// 为什么两层都要：拦具体效果会漏（魔女瘴气那次就漏了引擎内部的第二条搬运路径），
/// 所以真正的保险放在最底层的搬运函数上；选择层只是为了体验。
/// </summary>
internal static class CorrodedExhaustGuard
{
    internal static bool Blocks(CardModel card, PileType target)
    {
        if (card == null)
            return false;

        if (!CorrodedCards.IsCorroded(card))
            return false;

        // 只在「从消耗堆搬出去」时拦：本来就在别处的同名牌不受影响
        var from = card.Pile?.Type;
        return from == PileType.Exhaust && target != PileType.Exhaust;
    }
}

/// <summary>选择层：从消耗堆选牌时剔除腐蚀牌。4 参数重载内部会转调 5 参数版本，所以只需拦这一个。</summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile),
    new[] { typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>) })]
public static class CorrodedExhaustFilterPatch
{
    // 参数一律按「位置」取，不按名字：游戏小版本更新会改形参名（0.113.1 就把 pileType 改成了 newPileType），
    // 按名字取会让 Harmony 直接抛「Parameter not found」，整个补丁类连挂都挂不上。
    private static void Prefix(CardPile pile, [HarmonyArgument(4)] ref Func<CardModel, bool> filter)
    {
        try
        {
            if (pile == null || pile.Type != PileType.Exhaust)
                return;

            var original = filter;
            filter = card => (original == null || original(card)) && !CorrodedCards.IsCorroded(card);
            GD.Print("[Subaru] exhaust selection: corroded cards filtered out");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] exhaust selection filter failed: " + e.Message);
        }
    }
}

/// <summary>搬运层：拦「腐蚀牌从消耗堆搬到其他牌堆」。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
    new[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
public static class CorrodedExhaustRetrievalPatch
{
    private static bool Prefix(CardModel card, [HarmonyArgument(1)] PileType targetPile, ref Task<CardPileAddResult> __result)
    {
        if (!CorrodedExhaustGuard.Blocks(card, targetPile))
            return true;

        GD.Print("[Subaru] exhaust retrieval blocked (corroded card): " + card.Id);
        __result = Task.FromResult(default(CardPileAddResult));
        return false;
    }
}

/// <summary>同上，按 CardPile 目标的重载。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
    new[] { typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
public static class CorrodedExhaustRetrievalPilePatch
{
    private static bool Prefix(CardModel card, [HarmonyArgument(1)] CardPile pile, ref Task<CardPileAddResult> __result)
    {
        if (pile == null || !CorrodedExhaustGuard.Blocks(card, pile.Type))
            return true;

        GD.Print("[Subaru] exhaust retrieval blocked (corroded card -> pile): " + card.Id);
        __result = Task.FromResult(default(CardPileAddResult));
        return false;
    }
}

/// <summary>批量搬运的重载（IEnumerable + PileType）：把腐蚀牌从列表里剔掉再放行。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
    new[] { typeof(System.Collections.Generic.IEnumerable<CardModel>), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
public static class CorrodedExhaustRetrievalManyPatch
{
    private static bool Prefix(
        [HarmonyArgument(0)] ref System.Collections.Generic.IEnumerable<CardModel> cards,
        [HarmonyArgument(1)] PileType targetPile,
        ref Task<System.Collections.Generic.IReadOnlyList<CardPileAddResult>> __result)
    {
        if (cards == null)
            return true;

        var list = cards as System.Collections.Generic.IList<CardModel> ?? new System.Collections.Generic.List<CardModel>(cards);
        var filtered = new System.Collections.Generic.List<CardModel>(list.Count);
        var blocked = 0;

        foreach (var card in list)
        {
            if (CorrodedExhaustGuard.Blocks(card, targetPile))
            {
                blocked++;
                continue;
            }

            filtered.Add(card);
        }

        if (blocked == 0)
            return true;

        GD.Print($"[Subaru] exhaust retrieval blocked on {blocked} corroded card(s)");

        cards = filtered;
        if (filtered.Count == 0)
        {
            __result = Task.FromResult<System.Collections.Generic.IReadOnlyList<CardPileAddResult>>(
                Array.Empty<CardPileAddResult>());
            return false;
        }

        return true;
    }
}
