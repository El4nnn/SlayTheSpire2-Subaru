using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 魔女瘴气「不可丢弃」：引擎没有可 veto 的钩子（只有事后的 AfterCardDiscarded），
/// 所以直接拦弃牌指令本身。保留关键词只挡住了「回合结束弃牌」，
/// 这一层挡的是所有强制弃牌效果。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Discard), new[] { typeof(PlayerChoiceContext), typeof(CardModel) })]
public static class NoDiscardSinglePatch
{
    private static bool Prefix(CardModel card, ref Task __result)
    {
        if (card is not CurseWitchMiasma)
            return true;

        GD.Print("[Subaru] discard blocked: " + card.Id);

        // Discard 返回 Task：跳过原方法必须自己给已完成的 Task，
        // 否则调用方 await null 会 NullReferenceException（同 CardAnimationPatch 那个坑）。
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Discard), new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<CardModel>) })]
public static class NoDiscardManyPatch
{
    private static bool Prefix(ref IEnumerable<CardModel> cards, ref Task __result)
    {
        if (cards == null)
            return true;

        var list = cards as IList<CardModel> ?? cards.ToList();
        if (!list.Any(c => c is CurseWitchMiasma))
            return true;

        var kept = list.Where(c => c is not CurseWitchMiasma).ToList();
        GD.Print($"[Subaru] discard blocked on {list.Count - kept.Count} miasma, kept {kept.Count}");

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
/// 赌徒药水 / 赌徒筹码 / 精算赌博 / 命运之轮走的是 <c>CardCmd.DiscardAndDraw</c>，
/// 不经过 <see cref="NoDiscardSinglePatch"/>/<see cref="NoDiscardManyPatch"/> 拦的那两个 Discard 重载
/// （2026-11-18 玩家报「瘴气能被赌徒药水弃掉」）。
///
/// <para>处理方式：把瘴气从待弃列表里剔掉再自己调一次，**抽牌张数改成实际弃掉的张数** ——
/// 否则瘴气留在手上还照原数抽，等于白赚一张手牌。</para>
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.DiscardAndDraw))]
public static class NoDiscardAndDrawPatch
{
    private static bool Prefix(
        PlayerChoiceContext choiceContext,
        ref IEnumerable<CardModel> cardsToDiscard,
        int cardsToDraw,
        ref Task __result)
    {
        // 注意：注入的参数名必须和游戏里一模一样（2026-11-19 的日志：
        // 声明成 cards 时会被跳过——游戏里的形参叫 cardsToDiscard）。
        if (cardsToDiscard == null)
            return true;

        var list = cardsToDiscard as IList<CardModel> ?? cardsToDiscard.ToList();
        if (!list.Any(c => c is CurseWitchMiasma))
            return true;

        var kept = list.Where(c => c is not CurseWitchMiasma).ToList();
        GD.Print($"[Subaru] discard+draw blocked on {list.Count - kept.Count} miasma, kept {kept.Count}");

        __result = CardCmd.DiscardAndDraw(choiceContext, kept, Math.Min(cardsToDraw, kept.Count));
        return false;
    }
}
