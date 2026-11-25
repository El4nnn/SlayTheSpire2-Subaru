using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「本场战斗内必须跨死归保留」的临时牌登记簿。
///
/// 一般的战斗内生成牌（迷尼亚、真·不可视之神意之类）在死归还原时会被当临时牌清掉，
/// 这是对的——它们本来就该随回档消失。
/// 但有些牌是一次性资源换来的、且来源本身带[腐蚀]（一场只能用一次），
/// 被回档吃掉就等于白白亏掉这次机会。这类牌在生成时登记进来，
/// BattleSnapshot.RestorePile 会放过它们。
///
/// 注意：这里按**实例引用**记，不能用原型——同名的正常牌不该被牵连。
/// </summary>
public static class RewindPersistentCards
{
    private static readonly HashSet<CardModel> Kept = new();

    public static void Mark(CardModel? card)
    {
        if (card == null)
            return;

        if (Kept.Add(card))
            GD.Print("[Subaru] keep across rewind: " + card.Id);
    }

    public static bool IsMarked(CardModel? card)
    {
        return card != null && Kept.Contains(card);
    }

    /// <summary>战斗结束后清账（由 DeathReturnSpike.ResetForCombat 调用）。</summary>
    public static void Reset()
    {
        Kept.Clear();
    }
}
