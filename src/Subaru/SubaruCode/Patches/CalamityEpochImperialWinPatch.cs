using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Runs;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「大灾」节点的解锁条件：**以菜月昴通关一次**（前置，由 <see cref="SubaruWitchEndEpoch"/> 承担）
/// **并且有一次是走帝国军（文森特契约）通关的**。
///
/// <para>2026-11-19 修正：以前直接挂 RitsuLib 的 <c>UnlockEpochAfterWinAs&lt;Subaru, Calamity&gt;</c>，
/// 结果是「以菜月昴通关一次」就发；而且节点链里它前置是「帝国的剑狼」（要 10 个首领），
/// EnforceChains 会在下场战斗开始时把不满足前置的节点撤掉 —— 两条叠加，表现就是解锁时机乱。</para>
///
/// <para>现在：<c>RunManager.WinRun</c> 的 prefix 里读本局路线（<see cref="SubaruRunPath"/>，
/// 从牌组里的契约牌反推，存档/死归/联机都不丢），只有 <see cref="SubaruPath.Vincent"/>
/// 才发大灾；节点链的前置同时改成 WitchEnd。</para>
///
/// <para>用 prefix 而不是 postfix：WinRun 走完可能已经把 RunState 清了。</para>
/// </summary>
[HarmonyPatch(typeof(RunManager), "WinRun")]
public static class CalamityEpochImperialWinPatch
{
    [HarmonyPrefix]
    public static void Prefix(RunManager __instance)
    {
        try
        {
            var state = __instance?.State;
            if (state == null)
                return;

            if (SubaruEpochGate.IsObtained(SubaruEpochGate.CalamityEpochId))
                return;

            var path = SubaruRunPath.Current(state);
            GD.Print($"[Subaru] run won, path={path}");

            if (path != SubaruPath.Vincent)
                return;

            // 「以菜月昴通关一次」这条前置就是本次胜利本身；先把它补上，
            // 否则 Grant 会以「前置未解锁」把大灾挡掉（同一帧里 RitsuLib 的规则还没跑）。
            if (!SubaruEpochGate.IsObtained(SubaruEpochGate.WitchEndEpochId))
            {
                SubaruEpochGranter.Grant<SubaruWitchEndEpoch>(SubaruEpochGate.WitchEndEpochId);
            }

            GD.Print("[Subaru] imperial army victory: granting calamity epoch");
            SubaruEpochGranter.Grant<SubaruCalamityEpoch>(SubaruEpochGate.CalamityEpochId);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] calamity epoch on imperial win failed: " + e.Message);
        }
    }
}
