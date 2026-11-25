using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Unlocks;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 按 epoch id 查「这个历史节点有没有被揭示」。
/// 原版只有泛型版的 <c>UnlockState.IsEpochRevealed&lt;T&gt;()</c>，
/// 而卡池过滤需要按 id 动态判断（一张牌可能被多个节点解锁），所以这里直接读内部的 id 集合。
/// </summary>
public static class SubaruEpochGate
{
    /// <summary>我们自己的节点 id。</summary>
    public const string CallEpochId = "SUBARU_EPOCH_CALL";
    public const string RewindEpochId = "SUBARU_REWIND_EPOCH";
    public const string WitchEndEpochId = "SUBARU_WITCH_END_EPOCH";
    public const string LoveEpochId = "SUBARU_LOVE_EPOCH";
    public const string SwordWolfEpochId = "SUBARU_SWORD_WOLF_EPOCH";
    public const string CollapseEpochId = "SUBARU_COLLAPSE_EPOCH";
    public const string GluttonyEpochId = "SUBARU_GLUTTONY_EPOCH";

    /// <summary>大灾（邀约7）：最后一个节点，挂在「帝国的剑狼」后面。</summary>
    public const string CalamityEpochId = "SUBARU_CALAMITY_EPOCH";

    private static readonly FieldInfo? UnlockedIdsField =
        typeof(UnlockState).GetField(
            "_unlockedEpochIds",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public static bool IsRevealed(UnlockState? state, string epochId)
    {
        if (state == null || string.IsNullOrEmpty(epochId))
            return false;

        try
        {
            return UnlockedIdsField?.GetValue(state) is HashSet<string> ids && ids.Contains(epochId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 节点链：值 = 必须先解锁的前置节点。
    /// 异世界召唤（无前置）→ 死亡回归 / 魔女时代的终结 / 暴食；
    /// 死亡回归 → Love；异世界召唤 → 帝国的剑狼 / 崩坏的开始。
    ///
    /// <para>
    /// 剑狼的前置 2026-11-19 从「魔女时代的终结」改成「异世界召唤」：
    /// 它的解锁条件是「以昴累计击败 10 个首领」，而 10 个首领不需要通关就能凑够
    /// （比如连续几局死在第 3 幕 BOSS）。原来那份「必须先通关」的前置和规则不同步，
    /// 会出现「局后发放 → 下一场战斗开始时被 <see cref="EnforceChains"/> 当缺前置删掉」
    /// 的闪烁；改成根节点后条件只剩一条。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Prerequisites =
        new Dictionary<string, string>
        {
            [RewindEpochId] = CallEpochId,
            [WitchEndEpochId] = CallEpochId,
            [GluttonyEpochId] = CallEpochId,
            [LoveEpochId] = RewindEpochId,
            [SwordWolfEpochId] = CallEpochId,
            [CollapseEpochId] = WitchEndEpochId,
            // 大灾：前置是「以菜月昴通关一次」（WitchEnd），另加一个运行时条件 ——
            // 必须有一次是走帝国军（文森特契约）通关的，见 CalamityEpochImperialWinPatch。
            [CalamityEpochId] = WitchEndEpochId,
        };

    /// <summary>某个节点是否已经在存档里（已获得或已揭示）。</summary>
    public static bool IsObtained(string epochId)
    {
        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            if (progress?.Epochs == null)
                return false;

            foreach (var epoch in progress.Epochs)
            {
                if (epoch != null
                    && epoch.Id == epochId
                    && epoch.State >= MegaCrit.Sts2.Core.Saves.EpochState.Obtained)
                {
                    return true;
                }
            }
        }
        catch
        {
            // 读不到就当没解锁
        }

        return false;
    }

    /// <summary>前置节点是否都满足（没有前置就算满足）。</summary>
    public static bool PrerequisitesMet(string epochId)
    {
        return !Prerequisites.TryGetValue(epochId, out var required) || IsObtained(required);
    }

    /// <summary>
    /// 强制维护节点链：如果某个子节点在没有前置的情况下被解锁了
    /// （例如原版/框架的 AfterWinAs 流程直接发放），就把这条记录撤掉。
    /// 返回撤掉的 id 列表，便于打日志。
    /// </summary>
    public static List<string> EnforceChains()
    {
        var removed = new List<string>();

        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            var field = progress == null
                ? null
                : typeof(MegaCrit.Sts2.Core.Saves.ProgressState).GetField(
                    "_epochs",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

            if (field?.GetValue(progress) is not System.Collections.IList epochs)
                return removed;

            foreach (var pair in Prerequisites)
            {
                var child = pair.Key;
                var parent = pair.Value;

                if (!IsObtained(child) || IsObtained(parent))
                    continue;

                for (var i = epochs.Count - 1; i >= 0; i--)
                {
                    if (epochs[i] is MegaCrit.Sts2.Core.Saves.SerializableEpoch se && se.Id == child)
                    {
                        epochs.RemoveAt(i);
                        removed.Add(child);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning("[Subaru] enforce epoch chains failed: " + e.Message);
        }

        return removed;
    }

    /// <summary>任意一个节点已揭示就算解锁。</summary>
    public static bool AnyRevealed(UnlockState? state, IReadOnlyList<string> epochIds)
    {
        for (var i = 0; i < epochIds.Count; i++)
        {
            if (IsRevealed(state, epochIds[i]))
                return true;
        }

        return false;
    }
}
