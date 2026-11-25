using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 魔女瘴气的统一防护：**不可被丢弃、不可被消耗、不可被移出战斗**，
/// 并且保留不再靠[保留]关键词（见 <see cref="MiasmaRetainPatch"/>）。
///
/// <para>为什么要换成这一层：卡面上的词条已经合并成一个[魔女的诅咒]，
/// 引擎的[保留]/[不可打出]关键词会各自顶一行字，留在 CanonicalKeywords 里就合并不掉。
/// 所以「保留」改成直接回答 <c>ShouldRetainThisTurn</c>，
/// 「不可打出」改在 <c>IsPlayable</c> 里返回 false（<c>CanPlay</c> 两条都会查）。</para>
///
/// <para>例外：明确指名「消耗 / 移除 1 张[魔女瘴气]」的自有卡牌
/// （契约·贝蒂、第一次骑士、潘多拉魔女）要能正常拿走它，所以给一个
/// <see cref="Allow"/> 窗口，调用方 <c>using</c> 包起来即可。</para>
/// </summary>
public static class MiasmaGuard
{
    private static int _allowDepth;

    public static bool Allowed => _allowDepth > 0;

    /// <summary>开一个「允许动瘴气」的窗口，返回 IDisposable 以便 using 收尾。</summary>
    public static System.IDisposable Allow()
    {
        _allowDepth++;
        return new Scope();
    }

    private sealed class Scope : System.IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;

            _done = true;
            if (_allowDepth > 0)
                _allowDepth--;
        }
    }
}

/// <summary>消耗（放进消耗堆）：瘴气不参与，除非在 <see cref="MiasmaGuard.Allow"/> 窗口里。</summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Exhaust))]
public static class MiasmaExhaustGuard
{
    private static bool Prefix(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        if (card is not CurseWitchMiasma || MiasmaGuard.Allowed)
            return true;

        GD.Print("[Subaru] miasma exhaust blocked: " + card.Id);

        // Exhaust 返回 Task<CardPileAddResult?>：跳过原方法必须自己给已完成的任务，
        // 否则调用方 await null 会 NullReferenceException。
        __result = Task.FromResult<CardPileAddResult?>(null);
        return false;
    }
}

/// <summary>移出战斗（契约·贝蒂 / 第一次骑士 / 潘多拉魔女走这条）：同样默认拒绝。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.RemoveFromCombat), new[] { typeof(CardModel), typeof(bool) })]
public static class MiasmaRemoveGuard
{
    private static bool Prefix(CardModel card, ref Task __result)
    {
        if (card is not CurseWitchMiasma || MiasmaGuard.Allowed)
            return true;

        GD.Print("[Subaru] miasma remove blocked: " + card.Id);
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.RemoveFromCombat), new[] { typeof(IEnumerable<CardModel>), typeof(bool) })]
public static class MiasmaRemoveManyGuard
{
    private static bool Prefix(ref IEnumerable<CardModel> cards, ref Task __result)
    {
        if (cards == null || MiasmaGuard.Allowed)
            return true;

        var list = cards as IList<CardModel> ?? cards.ToList();
        if (!list.Any(c => c is CurseWitchMiasma))
            return true;

        var kept = list.Where(c => c is not CurseWitchMiasma).ToList();
        GD.Print($"[Subaru] miasma remove blocked on {list.Count - kept.Count}, kept {kept.Count}");

        if (kept.Count == 0)
        {
            __result = Task.CompletedTask;
            return false;
        }

        cards = kept;
        return true;
    }
}

/// <summary>
/// 保留：不再挂[保留]关键词，直接回答引擎的「这回合结束时留不留手」。
/// <c>CombatManager.FlushPlayerHand</c> 就是按 <see cref="CardModel.ShouldRetainThisTurn"/>
/// 分流手牌的，所以这一条等于无条件保留。
/// </summary>
[HarmonyPatch(typeof(CardModel), "get_ShouldRetainThisTurn")]
public static class MiasmaRetainPatch
{
    private static void Postfix(CardModel __instance, ref bool __result)
    {
        if (__result)
            return;

        if (__instance is CurseWitchMiasma)
            __result = true;
    }
}
