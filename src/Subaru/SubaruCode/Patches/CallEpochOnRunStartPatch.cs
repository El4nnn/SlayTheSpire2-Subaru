using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「异世界召唤？！」节点：用愚者开一局即算，打完、打输、中途放弃都算。
///
/// 但**发放时机**要跟原版角色解锁节点一致：在原版里，节点是在一局结束时结算的，
/// 解锁动画也是那一刻播。之前我们挂在「进入第一幕」上，结果刚开局动画就播了，
/// 所以改成挂在一局结束的两个出口：
///   - <c>RunManager.OnEnded</c>：正常结束（胜利 / 死亡）
///   - <c>RunManager.Abandon</c>：手动放弃
/// 两个出口都用 prefix 先抓住当时的 RunState（postfix 时它可能已经被清掉了）。
/// </summary>
public static class CallEpochOnRunEnd
{
    private static RunState _pending;

    public static void TryGrant(RunState state, string source)
    {
        try
        {
            if (state == null)
                return;

            if (SubaruEpochGate.IsObtained(SubaruEpochGate.CallEpochId))
                return;

            var players = state.Players;
            if (players == null || players.Count == 0)
                return;

            foreach (var player in players)
            {
                if (player == null)
                    continue;

#if GAME_BETA
                // 0.107 没有 Fool 角色，这条解锁路径只在 beta 分支成立
                var character = player.Character;
                if (character == null || character is not Fool)
                    continue;

                GD.Print($"[Subaru] run with Fool finished ({source}), granting call epoch");
                SubaruEpochGranter.Grant<SubaruCallEpoch>(SubaruEpochGate.CallEpochId);
                return;
#endif
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] call epoch on run end failed: " + e.Message);
        }
    }

    private static void Capture(RunManager manager)
    {
        try
        {
            _pending = manager == null ? null : manager.State;
        }
        catch
        {
            _pending = null;
        }
    }

    private static void Flush(string source)
    {
        var state = _pending;
        _pending = null;
        TryGrant(state, source);
    }

    /// <summary>正常结束（胜利 / 死亡）。</summary>
    [HarmonyPatch(typeof(RunManager), "OnEnded")]
    public static class OnEndedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(RunManager __instance) => Capture(__instance);

        [HarmonyPostfix]
        public static void Postfix() => Flush("run ended");
    }

    /// <summary>手动放弃这一局。</summary>
    [HarmonyPatch(typeof(RunManager), "Abandon")]
    public static class OnAbandonPatch
    {
        [HarmonyPrefix]
        public static void Prefix(RunManager __instance) => Capture(__instance);

        [HarmonyPostfix]
        public static void Postfix() => Flush("run abandoned");
    }
}
