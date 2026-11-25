using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Saves;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 百科大全 → 卡牌库 把「没见过」的牌显示成「未知」（ModelVisibility.NotSeen）。
///
/// 两层处理：
/// 1) 显示层：本 mod 的牌一律按正常卡面显示（不写存档，立刻生效）
/// 2) 记录层：第一次打开卡牌库时，把本 mod 的牌一次性写进「已发现卡牌」，
///    这样其它会读 DiscoveredCards 的界面也不会再显示未知
/// </summary>
[HarmonyPatch(typeof(NCardLibraryGrid), "GetCardVisibility")]
public static class CardLibraryVisibilityPatch
{
    private static bool _markedSeen;

    [HarmonyPostfix]
    public static void Postfix(CardModel card, ref ModelVisibility __result)
    {
        if (card is not SubaruCard)
            return;

        __result = ModelVisibility.Visible;

        if (_markedSeen)
            return;

        _markedSeen = true;
        MarkAllSeen();
    }

    private static void MarkAllSeen()
    {
        try
        {
            var progress = SaveManager.Instance?.Progress;
            if (progress == null)
            {
                GD.PushWarning("[Subaru] no progress state, skip mark-as-seen");
                return;
            }

            var count = 0;
            foreach (var card in ModelDb.AllCards)
            {
                if (card is not SubaruCard)
                    continue;

                progress.MarkCardAsSeen(card.Id);
                count++;
            }

            GD.Print($"[Subaru] compendium: marked {count} cards as seen");

            // 诊断：本角色卡池里到底认到了几张牌
            var pool = MegaCrit.Sts2.Core.Models.ModelDb.CardPool<Subaru.SubaruCode.Character.SubaruCardPool>();
            if (pool != null)
            {
                var inPool = 0;
                foreach (var _ in pool.AllCards)
                    inPool++;

                GD.Print($"[Subaru] card pool '{pool.Title}' has {inPool} cards (subaru cards total {count})");
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] mark-as-seen failed: " + e);
        }
    }
}
