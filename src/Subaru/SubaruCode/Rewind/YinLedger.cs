using Godot;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「本场战斗打出过几张[阴魔法]牌」的记账（封魔石按它削力量）。
///
/// 这个计数**随死归回溯**：快照点上的值由 DeathReturnSpike 一起存下来
/// （`_yinInSnapshot`），回档时 <see cref="SetCount"/> 退回去，
/// 和「造成过伤害次数」是同一套做法。战斗结束由 Reset 清零。
///
/// 注意记账时机：挂在 <c>SubaruCard.OnPlay</c> 里 <c>OnPlayCard</c> **之后**，
/// 所以封魔石自己那张不计入本次结算（「已经打出过」的口径）。
/// </summary>
public static class YinLedger
{
    private static int _count;

    public static int Count => _count;

    public static void MarkPlayed(CardModel? card)
    {
        // 求解器在线程池线程里模拟出牌时也会调到这里，模拟不能算进真计数。
        if (!MainThreadGuard.IsMainThread)
            return;

        if (card is not SubaruCard subaru)
            return;

        if (!subaru.CanonicalKeywords.Contains(SubaruKeywords.YinMagic))
            return;

        _count++;
        GD.Print($"[Subaru] yin magic played: {_count} (last={card.Id})");
    }

    public static void SetCount(int value)
    {
        var next = value < 0 ? 0 : value;
        if (next == _count)
            return;

        GD.Print($"[Subaru] yin counter restore: {_count} -> {next}");
        _count = next;
    }

    public static void Reset()
    {
        if (_count != 0)
            GD.Print($"[Subaru] yin counter reset (was {_count})");
        _count = 0;
    }
}
