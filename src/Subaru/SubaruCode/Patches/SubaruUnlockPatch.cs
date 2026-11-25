using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 角色解锁：菜月昴由「异世界召唤」节点（<c>SUBARU_EPOCH_CALL</c>）解锁，和原版角色一致。
///
/// 原版选人界面判断锁没锁，是看 <c>UnlockState.Characters</c> 里有没有这个角色，
/// 而这个列表只由「已解锁的 epoch」推出来。原版只会移除 Silent/Regent/... 这几个写死的角色，
/// mod 角色默认一直留在列表里，所以这里自己补一刀：
///   - 节点已揭示 → 确保在列表里（双保险，防止框架层把它滤掉）
///   - 节点没揭示 → 从列表里移除
///
/// 想临时跳过解锁流程（调试用）把 <see cref="ForceUnlock"/> 置 true。
/// </summary>
[HarmonyPatch(typeof(UnlockState), "get_Characters")]
public static class SubaruUnlockPatch
{
    private const bool ForceUnlock = false;

    [HarmonyPostfix]
    public static void Postfix(UnlockState __instance, ref IEnumerable<CharacterModel> __result)
    {
        try
        {
            var self = ModelDb.Character<Subaru.SubaruCode.Character.Subaru>();
            if (self == null)
                return;

            var list = __result?.ToList() ?? new List<CharacterModel>();

            var unlocked = ForceUnlock
                || (__instance != null && SubaruEpochGate.IsRevealed(__instance, SubaruEpochGate.CallEpochId));

            if (unlocked)
            {
                if (!list.Contains(self))
                    list.Add(self);
            }
            else
            {
                list.Remove(self);
            }

            __result = list;
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] unlock characters patch failed: " + e);
        }
    }
}
