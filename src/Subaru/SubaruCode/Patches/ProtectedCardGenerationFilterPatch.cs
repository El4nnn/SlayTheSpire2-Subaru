using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 战斗内「从卡池挑几张给你选」的效果（发现类、**原版「富足」** 等）不能出现
/// <b>不可被创建</b>的牌 —— 否则玩家选了也拿不到（2026-11-19 玩家反馈）。
///
/// <para>实证链路（原版 <c>Abundance.OnPlay</c> 的 IL）：
/// <c>Owner.Character.CardPool.GetUnlockedCards(...)</c>
/// → <c>Where(c =&gt; c.Type == Power)</c>
/// → <c>CardFactory.GetDistinctForCombat(player, 3, rng)</c>
/// → 逐个 <c>Upgrade()</c> → 选择框 → <c>CardPileCmd.AddGeneratedCardToCombat</c>。</para>
///
/// <para>而 <see cref="CorrosionCreationGuardPatch"/> 会在最后那一步把
/// [腐蚀]牌和契约牌**静默挡掉**（它们「一场战斗只能用一次 / 只能由涅奥发放」，
/// 被凭空造一份就白赚一次）。两边一撞 → 选择框里有，选了却什么都没发生。
/// mod 的腐蚀能力牌有 11 张（EmiliaBlessing / EchidnaWitch / Theresia / Lost …），
/// 原版「富足」正好只挑能力牌，所以撞上的概率不低。</para>
///
/// <para>修法选在**候选池**而不是放行生成：<c>CardFactory.FilterForCombat</c> 是
/// <c>GetDistinctForCombat</c> 内部对候选做的过滤（IL 里 <c>FilterForRun</c> → <c>FilterForCombat</c>
/// → <c>TakeRandom</c>），在这里剔掉同一批牌，选择框里就不会再出现选了拿不到的选项。
/// 挡生成那层继续保留 —— 它还要防双持 / 夜魇这类「复制手上已有牌」的路径（那些不走这里）。</para>
///
/// <para>不影响奖励 / 商店：那两条走 <c>CardCreationOptions.GetPossibleCards</c>，
/// 腐蚀牌本来就是正常卡池牌，该给的照给。</para>
/// </summary>
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.FilterForCombat))]
public static class ProtectedCardGenerationFilterPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref IEnumerable<CardModel> __result)
    {
        if (__result == null)
            return;

        // 我方主动生成腐蚀牌的窗口（CardCreationPolicy.Allow()）不拦
        if (CardCreationPolicy.AllowingCorroded)
            return;

        var kept = new List<CardModel>();
        var removed = new List<string>();

        foreach (var card in __result)
        {
            if (CardCreationPolicy.IsProtected(card))
            {
                removed.Add(card?.Id.ToString() ?? "<null>");
                continue;
            }

            kept.Add(card);
        }

        if (removed.Count == 0)
            return;

        GD.Print($"[Subaru] combat card pool filtered: {removed.Count} protected card(s) "
                 + $"({string.Join(", ", removed)})");
        __result = kept;
    }
}
