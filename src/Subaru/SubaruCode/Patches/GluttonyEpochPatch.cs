using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「暴食」节点：用菜月昴累计打赢 15 场精英战。
///
/// 计数直接读原版的 <c>ProgressState.EncounterStats</c>（和原版每个角色「15 精英」节点
/// 是同一份数据，算法也一样：精英 encounter 的 FightStats 里该角色的 Wins 之和），
/// 所以老存档已经打过的精英会追溯计入，不需要自己再开一个计数器。
///
/// 触发点选在 <c>SaveManager.UpdateProgressAfterCombatWon</c>（原版战斗胜利后更新统计的入口），
/// postfix 跑完时这一场的战绩已经记进去了，读到的数字自然是「含本场」的。
/// </summary>
public static class GluttonyEpochPatch
{
    public const int EliteWinsRequired = 15;

    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static MethodInfo _getEliteEncounters;

    /// <summary>
    /// 原版「战斗胜利后更新进度」的入口：<c>UpdateProgressAfterCombatWon(Player localPlayer, CombatRoom combatRoom)</c>。
    /// postfix 跑的时候本场战绩已经写进 <c>EncounterStats</c>，读到的数字自然含本场。
    ///
    /// <para><b>2026-11-19 修：这个补丁以前从来没生效过。</b>原来把
    /// <c>[HarmonyPatch(typeof(SaveManager), "UpdateProgressAfterCombatWon")]</c> 写在**方法**上、
    /// 类上没有 <c>[HarmonyPatch]</c>；而 <c>MainFile.ApplyPatchesIndividually</c> 是按
    /// 「类上有 HarmonyPatch 特性」来筛补丁类的 —— 于是整个类被静默跳过，
    /// 「暴食」的计数器一次都没跑过（日志里从来没有 <c>gluttony: elite wins X/15</c>）。
    /// 现在改成和其它补丁一致的「类上有特性 + 嵌套类」写法。</para>
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), "UpdateProgressAfterCombatWon")]
    public static class AfterCombatWonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player localPlayer) => TryGrant(localPlayer);
    }

    public static void TryGrant(Player player)
    {
        try
        {
            if (player == null)
                return;

            if (player.Character is not Subaru.SubaruCode.Character.Subaru)
                return;

            if (SubaruEpochGate.IsObtained(SubaruEpochGate.GluttonyEpochId))
                return;

            var wins = EliteWins(player.Character.Id);

            if (wins < EliteWinsRequired)
            {
                GD.Print($"[Subaru] gluttony: elite wins {wins}/{EliteWinsRequired}");
                return;
            }

            GD.Print($"[Subaru] gluttony: elite wins {wins}/{EliteWinsRequired}, granting epoch");
            SubaruEpochGranter.Grant<SubaruGluttonyEpoch>(SubaruEpochGate.GluttonyEpochId);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] gluttony epoch check failed: " + e.Message);
        }
    }

    /// <summary>某个角色「打赢过的精英战」总数。</summary>
    public static int EliteWins(ModelId characterId)
    {
        var elites = EliteEncounters();
        var progress = SaveManager.Instance == null ? null : SaveManager.Instance.Progress;

        if (elites == null || progress == null || progress.EncounterStats == null)
            return 0;

        var total = 0;

        foreach (var pair in progress.EncounterStats)
        {
            if (!elites.Contains(pair.Key))
                continue;

            var fightStats = pair.Value == null ? null : pair.Value.FightStats;
            if (fightStats == null)
                continue;

            foreach (var fight in fightStats)
            {
                if (fight != null && fight.Character == characterId)
                    total += fight.Wins;
            }
        }

        return total;
    }

    /// <summary>原版把「哪些 encounter 算精英」算在 ProgressSaveManager 里，这里借同一份名单。</summary>
    private static HashSet<ModelId> EliteEncounters()
    {
        try
        {
            var saveManager = SaveManager.Instance;
            if (saveManager == null)
                return null;

            var field = typeof(SaveManager).GetField("_progressSaveManager", Flags);
            var progressManager = field == null ? null : field.GetValue(saveManager);

            if (progressManager == null)
                return null;

            if (_getEliteEncounters == null)
                _getEliteEncounters = progressManager.GetType().GetMethod("GetEliteEncounters", Flags);

            return _getEliteEncounters == null
                ? null
                : _getEliteEncounters.Invoke(progressManager, null) as HashSet<ModelId>;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] get elite encounters failed: " + e.Message);
            return null;
        }
    }
}
