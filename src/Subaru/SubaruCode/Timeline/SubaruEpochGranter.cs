using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 把自定义 epoch 标记为「已揭示」。
///
/// <para><b>2026-11-18 修正：必须写 <see cref="EpochState.Revealed"/>，不能写 Obtained。</b>
/// 原因是引擎的 <c>UnlockState</c> 在构造时只收集 <c>State == EpochState.Revealed(5)</c> 的节点
/// （见 <c>UnlockState/&lt;.ctor&gt;b__23_0</c>：<c>State == 5</c>），而卡池的阵营过滤
/// （<c>SubaruCardPool.FilterThroughEpochs → CardGates → AnyRevealed</c>）读的正是
/// <c>UnlockState</c>。之前这里走 <c>Progress.ObtainEpoch</c>（写 Obtained = 4），
/// 结果节点在存档里「已获得」，但 <c>UnlockState</c> 看不见它 ——
/// 用帝国军通关后大灾的牌一张都不会进奖励池，图鉴里也一直是「未知」。</para>
///
/// <para>现在改走官方接口 <see cref="SaveManager.ObtainEpochOverride"/>，写 Revealed，
/// 和宣发解锁（PromoUnlockAll）以及 RitsuLib 的时间线扩展用的是同一条路。
/// 拿不到 SaveManager 时兜底直接往进度里补一条 Revealed 记录。</para>
///
/// <para>同一 epoch 只执行一次（<see cref="Granted"/>），并且存档里已经有了就直接跳过
/// —— 避免战斗中反复触发解锁提示。</para>
/// </summary>
public static class SubaruEpochGranter
{
    private static readonly System.Collections.Generic.HashSet<string> Granted = new();

    private static bool _ensuredRevealed;

    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// 老存档修补：以前用 Obtained(4) 写进去的节点，这里统一升级成 Revealed(5)。
    /// 只处理「已获得但还没揭示」的节点（3/4），不会把未获得的节点提前放出来。
    /// 每局进程只跑一次。
    /// </summary>
    public static void EnsureRevealed()
    {
        if (_ensuredRevealed)
            return;

        _ensuredRevealed = true;

        try
        {
            var save = SaveManager.Instance;
            var progress = save?.Progress;
            if (save == null || progress == null)
                return;

            var changed = false;

            foreach (var id in SubaruEpochPortraits.EpochIds)
            {
                var state = StateOf(progress, id);
                if (state == null || state == EpochState.Revealed)
                    continue;

                // 只升级「已获得（含无槽位的获得）」，未获得的不碰
                if (state != EpochState.Obtained && state != EpochState.ObtainedNoSlot)
                    continue;

                save.ObtainEpochOverride(id, EpochState.Revealed);
                GD.Print($"[Subaru] epoch upgraded to Revealed: {id} (was {state})");
                changed = true;
            }

            if (changed)
                save.SaveProgressFile();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ensure epoch revealed failed: " + e.Message);
        }
    }

    private static EpochState? StateOf(ProgressState progress, string epochId)
    {
        try
        {
            foreach (var epoch in progress.Epochs)
            {
                if (epoch != null && epoch.Id == epochId)
                    return epoch.State;
            }
        }
        catch
        {
            // 读不到就当没有
        }

        return null;
    }

    public static void Grant<TEpoch>(string epochId)
        where TEpoch : EpochModel
    {
        if (string.IsNullOrEmpty(epochId) || Granted.Contains(epochId))
            return;

        // 存档里已经有了就别再发一次（原版流程会打日志、可能触发解锁提示）
        if (SubaruEpochGate.IsObtained(epochId))
        {
            Granted.Add(epochId);
            return;
        }

        // 节点链：前置没解锁就先不发放（不记入 Granted，之后条件再满足时会重试）
        if (!SubaruEpochGate.PrerequisitesMet(epochId))
        {
            GD.Print($"[Subaru] epoch locked behind prerequisite: {epochId}");
            return;
        }

        Granted.Add(epochId);

        try
        {
            var save = SaveManager.Instance;
            if (save != null)
            {
                save.ObtainEpochOverride(epochId, EpochState.Revealed);
                save.SaveProgressFile();
                GD.Print($"[Subaru] epoch revealed via ObtainEpochOverride: {epochId}");
                return;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] ObtainEpochOverride failed ({epochId}): " + e.Message);
        }

        // 兜底：直接往进度里写一条 Revealed 记录（同样能被 UnlockState 收集到）
        try
        {
            var progress = SaveManager.Instance?.Progress;
            if (progress == null)
                return;

            var field = typeof(ProgressState).GetField("_epochs", Flags);
            if (field?.GetValue(progress) is not System.Collections.IList epochs)
                return;

            foreach (var existing in epochs)
            {
                if (existing is SerializableEpoch se && se.Id == epochId)
                {
                    se.SetObtained(EpochState.Revealed);
                    GD.Print($"[Subaru] epoch forced to Revealed (existing record): {epochId}");
                    return;
                }
            }

            epochs.Add(new SerializableEpoch(epochId, EpochState.Revealed));
            GD.Print($"[Subaru] epoch appended as Revealed (fallback): {epochId}");
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] epoch fallback failed ({epochId}): " + e);
        }
    }
}
