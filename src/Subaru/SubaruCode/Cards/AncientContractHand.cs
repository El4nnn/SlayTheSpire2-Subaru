using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 先古契约（贝蒂 / 文森特 / 大灾）的「战斗开始时进入手牌」。
///
/// <para><b>为什么不再用[固有]</b>：[固有]的语义是「起手时这张牌在手里」，实现上它会被排在
/// 抽牌堆顶、跟着起手一起抽 —— 等于**占掉 5 个抓牌位里的 1 个**。
/// 现在改成：战斗开始时把契约压到抽牌堆**最底部**（起手 5 张绝不可能抽到它），
/// 抽牌结束后在第一回合的回合开始（Late 钩子）再把契约从牌堆底直接移进手牌。
/// 手牌变成 6 张，契约不占抓牌位。</para>
///
/// <para>只在回合号 = 1 时执行；死归回档会把回合号还原成 1、契约也回到抽牌堆底，
/// 所以回档后会自动再塞一次。</para>
/// </summary>
public static class AncientContractHand
{
    /// <summary>
    /// 战斗开始时（<c>BeforeCombatStart</c>，此时牌堆已经铺好）把先古契约压到抽牌堆最底。
    /// 直接改模型顺序即可：这时候抽牌堆还没露脸，不需要演出。
    /// </summary>
    public static void MoveToDrawPileBottom(Player player)
    {
        try
        {
            var pcs = player?.PlayerCombatState;
            if (pcs == null)
                return;

            var contracts = pcs.DrawPile.Cards
                .Where(card => card is SubaruCard subaru && subaru.EntersHandAtCombatStart)
                .ToList();

            foreach (var card in contracts)
                pcs.DrawPile.MoveToBottomInternal(card);

            if (contracts.Count > 0)
                GD.Print($"[Subaru] ancient contract moved to draw bottom: {contracts.Count}, draw={pcs.DrawPile.Cards.Count}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ancient contract bottom move failed: " + e.Message);
        }
    }

    public static async Task MoveIntoHandAsync(PlayerChoiceContext choiceContext, Player player, bool refreshHandUi = true)
    {
        try
        {
            var pcs = player?.PlayerCombatState;
            if (pcs == null)
                return;

            if (pcs.TurnNumber != 1)
                return;

            var contracts = pcs.DrawPile.Cards
                .Where(card => card is SubaruCard subaru && subaru.EntersHandAtCombatStart)
                .ToList();

            if (contracts.Count == 0)
                return;

            var moved = 0;
            foreach (var card in contracts)
            {
                await BattleSnapshot.AddCardSafely(pcs.Hand, card, pcs);
                if (ReferenceEquals(card.Pile, pcs.Hand))
                    moved++;
            }

            GD.Print($"[Subaru] ancient contract moved to hand: {moved}/{contracts.Count}, "
                + $"hand={pcs.Hand.Cards.Count}, draw={pcs.DrawPile.Cards.Count}, "
                + $"piles={string.Join("/", contracts.Select(c => c.Pile?.Type.ToString() ?? "null"))}");

            // 只改模型不够：NPlayerHand 只在 PlayerActionsDisabledChanged / OnCombatStateChanged
            // 时重建，塞牌这两个事件都不发 —— 牌确实在手里，但画面上还是 5 张（2026-11-18
            // 实测：日志有 moved to hand: 1，玩家看不到牌）。补一次和回档共用同一套的手牌刷新。
            if (moved > 0 && refreshHandUi)
                DeathReturnSpike.RefreshHandUi(player.Creature?.CombatState);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ancient contract hand move failed: " + e.Message);
        }
    }
}
