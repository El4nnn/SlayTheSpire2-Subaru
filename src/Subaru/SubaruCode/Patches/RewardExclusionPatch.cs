using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Character;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 卡池出口的最后一道：剔掉「生成专用」的牌（E·M·T / E·M·M / 临时迷尼亚 / 真·不可视之神意 等，
/// <see cref="SubaruCard.IsGenerateOnly"/> = true），它们只能被卡牌效果造出来。
/// </summary>
[HarmonyPatch(typeof(CardPoolModel), "GetUnlockedCards")]
public static class RewardExclusionPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not SubaruCardPool)
            return;

        __result = __result
            .Where(c => !(c is SubaruCard subaru && subaru.IsGenerateOnly))
            .ToList();
    }
}

/// <summary>
/// 奖励 / 商店的候选牌：<c>CardCreationOptions.GetPossibleCards(Player player)</c>。
///
/// <para><b>2026-11-19 修正一个错误结论</b>：这里以前写着「它从 CardPools 自己拼列表，
/// 不经过 GetUnlockedCards，所以只打池子那一层没用」。IL 实测（0.113，sts2.dll）不是这样 ——
/// <c>SelectMany(pool =&gt; pool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint))</c>，
/// 是**经过**池子的。所以池子那层确实会拦到奖励和商店；这里要补的是「按玩家」那一刀。</para>
///
/// <para><b>为什么必须在这里按玩家切</b>：一队两个昴、一个选大灾一个选贝蒂时，
/// 池子出口只能看到一条全局路线（或者并集），没法知道「现在是在给谁发牌」。
/// 这个方法的第一个参数就是 player，用它查这个人牌组里的契约，才切得准。</para>
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Runs.CardCreationOptions), nameof(MegaCrit.Sts2.Core.Runs.CardCreationOptions.GetPossibleCards))]
public static class RewardCandidateExclusionPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, ref IEnumerable<CardModel> __result)
    {
        if (__result == null)
            return;

        var kept = new List<CardModel>();
        var generateOnly = 0;
        var offPath = 0;
        var offPathIds = new List<string>();

        foreach (var card in __result)
        {
            if (card is SubaruCard subaru && subaru.IsGenerateOnly)
            {
                generateOnly++;
                continue;
            }

            if (!SubaruCardPool.AllowedForPlayer(player, card))
            {
                offPath++;
                if (offPathIds.Count < 6)
                    offPathIds.Add(card?.Id.ToString() ?? "<null>");
                continue;
            }

            kept.Add(card);
        }

        if (generateOnly > 0)
            GD.Print($"[Subaru] reward candidates filtered: {generateOnly} generate-only card(s)");

        if (offPath > 0)
        {
            GD.Print($"[Subaru] path filter (reward/shop, player={PlayerTag(player)}): "
                     + $"dropped {offPath} off-faction card(s) ({string.Join(", ", offPathIds)})");
        }

        __result = kept;
    }

    internal static string PlayerTag(Player? player)
    {
        try
        {
            return player?.Creature?.Name ?? "?";
        }
        catch
        {
            return "?";
        }
    }
}

/// <summary>
/// 战斗内「从候选里挑几张」的生成（发现类、原版「富足」、夜魇等）：
/// <c>CardFactory.GetDistinctForCombat / GetForCombat</c> 都带 player，而且是在
/// <c>TakeRandom</c> **之前**拿到候选表的 —— 所以在 prefix 里把候选剔干净，
/// 抽牌时就不会抽到别家的牌，也不会出现「本该 3 张只剩 1 张」。
///
/// <para><b>为什么分成两个类</b>：一个类上挂两个 [HarmonyPatch] 在 Harmony 里是合法的，
/// 但 <c>MainFile.ApplyPatchesIndividually</c> 以前用单数 <c>GetCustomAttribute</c>，
/// 遇到多个会抛 AmbiguousMatchException 把整个补丁加载带崩（2026-11-19 的实际事故）。
/// 加载器已经改成复数版，但这里仍然按「一个类一个目标」写，跟库里其它补丁保持一致。</para>
/// </summary>
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.GetDistinctForCombat))]
public static class CombatDistinctCandidatePathPrefix
{
    [HarmonyPrefix]
    public static void Prefix(Player player, ref IEnumerable<CardModel> cards)
        => CombatCandidateFilter.Narrow(player, ref cards);
}

/// <summary>同上，<c>GetForCombat</c>。</summary>
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.GetForCombat))]
public static class CombatGenericCandidatePathPrefix
{
    [HarmonyPrefix]
    public static void Prefix(Player player, ref IEnumerable<CardModel> cards)
        => CombatCandidateFilter.Narrow(player, ref cards);
}

/// <summary>两个 prefix 共用的过滤逻辑。</summary>
internal static class CombatCandidateFilter
{
    internal static void Narrow(Player player, ref IEnumerable<CardModel> cards)
    {
        if (cards == null)
            return;

        var kept = new List<CardModel>();
        var offPath = 0;
        foreach (var card in cards)
        {
            if (!SubaruCardPool.AllowedForPlayer(player, card))
            {
                offPath++;
                continue;
            }

            kept.Add(card);
        }

        if (offPath == 0)
            return;

        GD.Print($"[Subaru] path filter (combat generation, player={RewardCandidateExclusionPatch.PlayerTag(player)}): "
                 + $"dropped {offPath} off-faction card(s)");
        cards = kept;
    }
}

/// <summary>
/// 「只能由事件发放」的遗物（贝蒂的守护）不能出现在奖励/商店里。
///
/// 它继承 <see cref="SubaruRelic"/>，[Pool] 会让它自动进本角色的遗物池，
/// 所以必须在池子的出口（<c>RelicPoolModel.GetUnlockedRelics</c>）后置过滤掉。
/// </summary>
[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.GetUnlockedRelics))]
public static class GrantedRelicExclusionPatch
{
    [HarmonyPostfix]
    public static void Postfix(RelicPoolModel __instance, ref System.Collections.Generic.IEnumerable<RelicModel> __result)
    {
        if (__instance is not SubaruRelicPool || __result == null)
            return;

        var kept = __result
            .Where(r => r is not Subaru.SubaruCode.Relics.BeatriceGuard
                        and not Subaru.SubaruCode.Relics.Evolution
                        and not Subaru.SubaruCode.Relics.EmperorsDagger)
            .ToList();

        __result = kept;
    }
}
