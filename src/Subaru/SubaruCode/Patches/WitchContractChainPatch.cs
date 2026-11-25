using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Events;
using Godot;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 事件链：茶会选「接受邀请」→ 下一次抽事件必出「魔女的契约」，且本局只出一次。
///
/// 挂在 Hook.ModifyNextEvent 上（ActModel.PullNextEvent 调它，位于抽事件队列之后、
/// AddVisitedEvent 之前），所以换掉的事件会被正常登记进 VisitedEventIds，
/// 房间背景也从一开始就按契约准备，不会出现上一版那种背景残留。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyNextEvent))]
public static class WitchContractChainPatch
{
    [HarmonyPostfix]
    public static void Postfix(RunState runState, EventModel currentEvent, ref EventModel __result)
    {
        try
        {
            if (__result is WitchContractEvent)
                return;

            if (!SubaruEventFlags.AcceptedTeaParty(runState))
                return;

            // 同一条铁律：队里没有昴的局不塞 mod 事件（2026-11-22）
            if (!SubaruEventFlags.PartyHasSubaru(runState))
                return;

            var contract = ModelDb.Event<WitchContractEvent>();
            if (contract == null)
                return;

            if (SubaruEventFlags.ContractVisited(runState, contract.Id))
                return;

            GD.Print("[Subaru] event chain: forcing " + contract.Id + " after tea party accept");
            __result = contract;
            return;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] event chain failed: " + e);
        }
    }
}

/// <summary>
/// 事件房里 mod 事件的额外出现率：
/// <list type="bullet">
///   <item>魔女的茶会 <b>15%</b>（2026-11-19 从 25% 下调）</item>
///   <item>爱丽丝与荆棘之王 <b>10%</b></item>
/// </list>
///
/// <para><b>为什么只掷一次、按区间切</b>：如果两个事件各写一个补丁各掷一次，
/// 后跑的那个实际概率会被前一个吃掉（前一个换了就直接 return），
/// 而且谁先跑取决于 Harmony 的补丁顺序。这里一次 <c>NextFloat</c>，
/// <c>[0, 0.15)</c> 给茶会、<c>[0.15, 0.25)</c> 给爱丽丝，两者互斥且概率精确。</para>
///
/// <para>只影响「?」房抽到事件之后的替换，不动房型分布（事件/战斗的比例不变）；
/// 被换上的事件仍然要在 <c>VisitedEventIds</c> 里登记，抽中即算见过。</para>
///
/// <para>茶会在「拒绝过 / 已见过」时不占这个区间；爱丽丝同样是「本局没走过」才生效。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyNextEvent))]
public static class ModEventOddsPatch
{
    /// <summary>茶会的出现率。</summary>
    private const float TeaPartyChance = 0.15f;

    /// <summary>爱丽丝与荆棘之王的出现率（紧接在茶会区间之后）。</summary>
    private const float AliceChance = 0.10f;

    [HarmonyPostfix]
    public static void Postfix(RunState runState, EventModel currentEvent, ref EventModel __result)
    {
        try
        {
            // 已经是 mod 事件（茶会 / 契约 / 爱丽丝）就不再掷，避免互相覆盖
            if (__result is WitchTeaPartyEvent
                || __result is WitchContractEvent
                || __result is AliceAndThornKingEvent)
                return;

            // 两道闸都不过就**连骰子都不掷**（2026-11-22）：非昴的局本来没有 mod 事件，
            // 白掷一次 runState.Rng.UpFront 等于改动别人的随机流。
            var canTea = CanRollTeaParty(runState);
            var canAlice = CanRollAlice(runState);
            if (!canTea && !canAlice)
                return;

            var roll = runState.Rng.UpFront.NextFloat();

            if (canTea && roll < TeaPartyChance)
            {
                var teaParty = ModelDb.Event<WitchTeaPartyEvent>();
                if (teaParty != null)
                {
                    GD.Print("[Subaru] event odds: tea party 15%");
                    __result = teaParty;
                    return;
                }
            }

            if (canAlice
                && roll >= TeaPartyChance
                && roll < TeaPartyChance + AliceChance)
            {
                var alice = ModelDb.Event<AliceAndThornKingEvent>();
                if (alice != null)
                {
                    GD.Print("[Subaru] event odds: alice and thorn king 10%");
                    __result = alice;
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] event odds patch failed: " + e);
        }
    }

    /// <summary>
    /// 茶会能不能被我们塞进去：必须过 <see cref="WitchTeaPartyEvent.IsAllowed"/>（= 队里有昴、没拒绝过）
    /// 且本局没走过。<b>不能只查 VisitedEventIds</b> —— 这里直接改 <c>__result</c>，
    /// 已经越过了引擎 <c>RoomSet.EnsureNextEventIsValid</c> 的 <c>IsAllowed</c> 过滤（2026-11-22 事故）。
    /// </summary>
    private static bool CanRollTeaParty(RunState runState)
    {
        var teaParty = ModelDb.Event<WitchTeaPartyEvent>();
        return teaParty != null
               && teaParty.IsAllowed(runState)
               && !runState.VisitedEventIds.Contains(teaParty.Id);
    }

    /// <summary>爱丽丝同理：只认 <see cref="AliceAndThornKingEvent.IsAllowed"/>（队里有昴）。</summary>
    private static bool CanRollAlice(RunState runState)
    {
        var alice = ModelDb.Event<AliceAndThornKingEvent>();
        return alice != null
               && alice.IsAllowed(runState)
               && !runState.VisitedEventIds.Contains(alice.Id);
    }
}

/// <summary>
/// 「?」房本身是掷骰决定的（事件 / 战斗 / 商店…），只改事件不够：
/// 掷到战斗时 ModifyNextEvent 根本不会被调用，契约就永远等不到。
///
/// 这里在 UnknownMapPointOdds.Roll 调用之前，把候选房型裁成只剩 Event。
/// 依据：Roll 里先看候选集合是否含 Event，再用「非事件赔率」逐项掷；
/// 候选集合只剩 Event 时，非事件的每一项都会被过滤掉，最终返回 Event。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyUnknownMapPointRoomTypes))]
public static class WitchContractRoomPatch
{
    [HarmonyPostfix]
    public static void Postfix(IRunState runState, ref IReadOnlySet<RoomType> __result)
    {
        try
        {
            if (runState is not RunState rs)
                return;

            if (!SubaruEventFlags.AcceptedTeaParty(rs))
                return;

            var contract = ModelDb.Event<WitchContractEvent>();
            if (contract == null || SubaruEventFlags.ContractVisited(rs, contract.Id))
                return;

            // 原集合里本来就没有事件（比如被黑名单排除）就不要硬塞
            if (__result == null || !__result.Contains(RoomType.Event))
                return;

            __result = new HashSet<RoomType> { RoomType.Event };
            GD.Print("[Subaru] event chain: next '?' room forced to Event");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] event chain room patch failed: " + e);
        }
    }
}
