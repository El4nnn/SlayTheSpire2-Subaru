using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Rewind;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 对话镜：每场战斗第一张[技能]牌重放 1 次。
///
/// 之前把逻辑写在遗物的 <c>ModifyCardPlayCount</c> 覆写上，实测一次都没触发——
/// 说明这条 hook 的监听者列表里没有把它算进去（日志里连 one 行都没有）。
/// 现在不再依赖监听者机制，直接 Harmony 挂 <see cref="Hook.ModifyCardPlayCount"/> 的 postfix，
/// 由遗物对象自己记账（<see cref="TalkingMirror.TryConsumeReplay"/>）。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyCardPlayCount))]
public static class MirrorPlayCountPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, CardModel card, ref int __result)
    {
        try
        {
            if (combatState == null || card == null)
                return;

            if (card.Type != CardType.Skill)
                return;

            var player = combatState.Players?.FirstOrDefault();
            if (player == null)
                return;

            var mirror = player.Relics?.OfType<TalkingMirror>().FirstOrDefault();
            if (mirror == null || !ReferenceEquals(card.Owner, player))
                return;

            if (mirror.TryConsumeReplay(card))
                __result += 1;
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] mirror play-count patch failed: " + e);
        }
    }
}
