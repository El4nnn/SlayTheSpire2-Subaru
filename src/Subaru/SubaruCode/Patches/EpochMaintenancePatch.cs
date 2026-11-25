using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Character;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 每场战斗开头维护一次时间线：
///
/// 1. 把「没有前置却被解锁」的子节点撤掉（<see cref="SubaruEpochGate.EnforceChains"/>）；
/// 2. 「崩坏的开始」：在魔女的契约里签过字就发放（前置节点由 Granter 检查）。
///
/// 注意：「帝国的剑狼」**不在这里发**了。原来的条件是「每日挑战」，
/// 但塔2的每日挑战是指定角色的，菜月昴摸不到，所以改成
/// RitsuLib 的「累计击败 N 个首领」规则，见
/// <see cref="SubaruTimeline.Register"/> 里的 UnlockEpochAfterBossVictories。
/// </summary>
public static class EpochMaintenancePatch
{
    private static bool _bossRuleLogged;

    /// <summary>
    /// 一次性诊断：确认昴现在满足 RitsuLib 的「模组角色使用时间线策略」条件。
    ///
    /// 2026-11-19 的根因是昴没实现 <c>IModCharacterEpochTimelineRequirement</c>，
    /// 于是 RitsuLib 的首领/精英解锁补丁对昴「放行原版」，原版对模组角色直接
    /// <c>throw new ArgumentOutOfRangeException("character")</c>，剑狼的 10 次首领判定从未执行。
    /// 加上接口后这里应打出 <c>policy=True</c>；之后 RitsuLib 自己会打
    /// "[Unlocks] Obtained epoch 'SUBARU_SWORD_WOLF_EPOCH' after N boss win(s)"，
    /// 或者（规则没注册上时）"… has no registered boss-win epoch rule …"。
    /// </summary>
    private static void LogTimelinePolicyOnce(Player? player)
    {
        if (_bossRuleLogged || player?.Character == null)
            return;

        _bossRuleLogged = true;

        try
        {
            var usesPolicy = player.Character is
                STS2RitsuLib.Scaffolding.Characters.IModCharacterEpochTimelineRequirement;
            GD.Print($"[Subaru] timeline policy: character={player.Character.Id} policy={usesPolicy}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] timeline policy probe failed: " + e.Message);
        }
    }

    /// <summary>战斗开始时调用一次；名字保留 TryGrant 语义上叫「维护」。</summary>
    public static void TryMaintainEpochs(Player? player)
    {
        try
        {
            LogTimelinePolicyOnce(player);

            // 先维护节点链：把「没有前置却被解锁」的子节点撤掉
            var removed = SubaruEpochGate.EnforceChains();
            foreach (var id in removed)
                GD.Print($"[Subaru] epoch removed (missing prerequisite): {id}");

            // 老存档修补：以前写成 Obtained 的节点升级成 Revealed（卡池/图鉴才看得到）
            SubaruEpochGranter.EnsureRevealed();

            if (player == null)
                return;

            if (player.Character is not Subaru.SubaruCode.Character.Subaru)
                return;

            var runState = player.RunState;
            if (runState == null)
                return;

            // 「崩坏的开始」：在魔女的契约里签过字（前置节点由 Granter 检查）
            if (Subaru.SubaruCode.Events.SubaruEventFlags.SignedWitchContract(runState))
            {
                SubaruEpochGranter.Grant<SubaruCollapseEpoch>(SubaruEpochGate.CollapseEpochId);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] epoch maintenance failed: " + e.Message);
        }
    }
}
