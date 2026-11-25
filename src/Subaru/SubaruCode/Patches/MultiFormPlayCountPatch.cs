using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 复数形态：让「下一张[尸兵]」多打出 N 次。
///
/// <para>为什么要 Harmony 而不是在能力里覆写 <c>ModifyCardPlayCount</c>：
/// 对话镜（<see cref="MirrorPlayCountPatch"/>）那次实测过，这条 hook 的监听者列表里
/// 没有把模型算进去，覆写一次都没触发。所以统一走 postfix 改返回值。</para>
///
/// <para>判据：这张牌带[尸兵]词条、出牌者是持有该状态的玩家。取走加成后立即移除状态。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardPlayCount))]
public static class MultiFormPlayCountPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, CardModel card, ref int __result)
    {
        try
        {
            if (combatState == null || card == null)
                return;

            if (!card.CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier))
                return;

            var powers = card.Owner?.Creature?.Powers;
            if (powers == null)
                return;

            var state = powers.OfType<MultiFormPower>().FirstOrDefault();
            if (state == null)
                return;

            var extra = state.Consume();
            if (extra > 0)
            {
                __result += extra;
                Godot.GD.Print($"[Subaru] multi form: {card.Id} plays {__result} time(s)");
            }
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] multi form play-count patch failed: " + e);
        }
    }
}
