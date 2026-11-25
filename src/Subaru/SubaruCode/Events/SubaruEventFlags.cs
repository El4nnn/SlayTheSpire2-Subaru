using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Subaru.SubaruCode.Events;

/// <summary>
/// 事件之间的状态查询。状态不另存，全部从「本局地图历史」里读——
/// 游戏在每次选完选项时会往 MapPointHistory 记一条 EventOptionHistoryEntry，
/// 里面存的是选项标题的本地化 key，所以可以精确判断当时选了哪个。
///
/// 为什么要这么做：事件模型是可复制的，跨房间状态必须跟着存档走；
/// 而自带的地图历史本来就会写进 current_run.save，不用自己再造一套持久化。
/// </summary>
public static class SubaruEventFlags
{
    /// <summary>
    /// 队里是否至少有一名昴。
    ///
    /// <para>2026-11-22 用户定：**mod 事件不能出现在没有昴的局里**。事件池是 run 级的，
    /// 多人里队友玩别的角色时，以前照样会抽到「魔女的茶会 / 爱丽丝与荆棘王」，
    /// 奖励（强欲的契约、之后的契约链）对谁都没用，还白占一次事件位。</para>
    /// </summary>
    public static bool PartyHasSubaru(IRunState runState)
    {
        try
        {
            var players = runState?.Players;
            return players != null && players.Any(p => p?.Character is Character.Subaru);
        }
        catch
        {
            // 拿不到玩家列表就不放行：宁可事件不出现，也不要污染别人的局
            return false;
        }
    }

    private const string TeaPartyEntry = "SUBARU-WITCH_TEA_PARTY_EVENT";

    /// <summary>本局是否在茶会里选过「接受邀请」。</summary>
    public static bool AcceptedTeaParty(IRunState runState) => Chose(runState, ".ACCEPT.");

    /// <summary>本局是否在茶会里选过「谁会相信魔女」（拒绝线）。</summary>
    public static bool RefusedTeaParty(IRunState runState) => Chose(runState, ".LEAVE.");

    /// <summary>本局是否在「魔女的契约」事件里选过「把手掌贴上去」（签订契约）。</summary>
    public static bool SignedWitchContract(IRunState runState) =>
        ChoseIn(runState, ContractEntry, ".SIGN.");

    private const string ContractEntry = "SUBARU-WITCH_CONTRACT_EVENT";

    private static bool Chose(IRunState runState, string optionMarker) =>
        ChoseIn(runState, TeaPartyEntry, optionMarker);

    private static bool ChoseIn(IRunState runState, string entryKey, string optionMarker)
    {
        foreach (var point in runState.MapPointHistory)
        {
            foreach (var entry in point)
            {
                foreach (var player in entry.PlayerStats)
                {
                    foreach (var choice in player.EventChoices)
                    {
                        var key = choice.Title == null ? null : choice.Title.LocEntryKey;
                        if (key == null)
                            continue;
                        if (key.Contains(entryKey, StringComparison.OrdinalIgnoreCase)
                            && key.Contains(optionMarker, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>本局是否已经走过「魔女的契约」。</summary>
    public static bool ContractVisited(RunState runState, ModelId contractId) =>
        runState.VisitedEventIds.Contains(contractId);

    /// <summary>
    /// 把某个事件从「本局已访问」里删掉，让它还能被随机抽到。
    /// 引擎抽事件时会跳过已访问的（RoomSet.EnsureNextEventIsValid），
    /// 一直跳到所有事件都见过一遍为止——所以要「再想一想」之后还能遇见茶会，必须手动删。
    /// </summary>
    public static void AllowEventAgain(IRunState runState, ModelId eventId)
    {
        if (runState is not RunState state)
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = state.GetType().GetField("_visitedEventIds", flags);
        if (field?.GetValue(state) is ISet<ModelId> set && set.Remove(eventId))
        {
            Godot.GD.Print($"[Subaru] event chain: '{eventId}' removed from visited");
        }
        else
        {
            Godot.GD.PushWarning("[Subaru] event chain: could not remove from _visitedEventIds");
        }
    }
}
