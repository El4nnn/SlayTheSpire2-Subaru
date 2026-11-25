using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 赛场上那张「打出去的牌」的显示工具（纯视觉）。
///
/// 为什么需要：本 mod 的对单攻击牌出牌后要等弹幕飞到第一发命中才开始结算
/// （Celestia 更是要等整串弹幕），这段时间那张 NCard 一动不动悬在屏幕中间，
/// 看着像卡住了。现在让它在这段等待期间**变成 70% 透明度**。
///
/// 找节点要试三个地方（2026-11-20 实测）：
///   ① <c>NCardPlayQueue.Instance.GetCardNode</c> —— 单人出牌时这里是**空的**
///      （出牌队列只在排队/多人时才有条目），所以这步大概率拿不到；
///   ② <c>NPlayerHand.Instance.GetCard</c> —— 单人出牌时那张牌还挂在手牌的
///      holder 上，这才是屏幕中间悬着的那张；
///   ③ <c>NCard.FindOnTable</c> —— 兜底。
///
/// 安全边界：只碰 <c>Control.Modulate</c>，不碰队列账目、不 reparent。
/// </summary>
public static class CardVisuals
{
    /// <summary>等待结算时的不透明度（0.15 = 只留 15% 不透明，快看不见了）。</summary>
    public const float WaitingAlpha = 0.15f;

    private const int RetryTimes = 8;      // 出牌瞬间牌可能还没挂好，隔几帧再试
    private const int RetryDelayMs = 16;

    /// <summary>把正在打出的这张牌调暗到 <see cref="WaitingAlpha"/>。拿不到节点就静默返回。</summary>
    public static void DimWhileWaiting(CardModel? card)
    {
        if (card == null || !Settings.SubaruVisualSettings.HitFxEnabled)
            return;

        _ = DimAsync(card);
    }

    /// <summary>恢复成不透明（结算结束后必须调，否则节点回池后会带着 0.7 透明度）。</summary>
    public static void Restore(CardModel? card)
    {
        if (card == null)
            return;

        var node = FindNode(card);
        if (node == null)
            return;

        var c = node.Modulate;
        node.Modulate = new Color(c.R, c.G, c.B, 1f);
    }

    private static async Task DimAsync(CardModel card)
    {
        try
        {
            for (var i = 0; i < RetryTimes; i++)
            {
                var node = FindNode(card);
                if (node != null)
                {
                    var c = node.Modulate;
                    node.Modulate = new Color(c.R, c.G, c.B, WaitingAlpha);
                    GD.Print($"[Subaru] card dimmed to {WaitingAlpha:P0} while waiting (parent={node.GetParent()?.Name})");
                    return;
                }

                await Task.Delay(RetryDelayMs);
            }

            GD.Print("[Subaru] card dim: node not found (queue/hand/table all empty)");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] card dim failed: " + e.Message);
        }
    }

    private static Control? FindNode(CardModel card)
    {
        // ① 出牌队列
        try
        {
            var queue = NCardPlayQueue.Instance;
            if (queue != null && GodotObject.IsInstanceValid(queue))
            {
                var n = queue.GetCardNode(card);
                if (n != null && GodotObject.IsInstanceValid(n))
                    return n;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] card lookup(queue) failed: " + e.Message);
        }

        // ② 手牌区（单人出牌时悬着的那张就在这）
        try
        {
            var hand = NPlayerHand.Instance;
            if (hand != null && GodotObject.IsInstanceValid(hand))
            {
                var n = hand.GetCard(card);
                if (n != null && GodotObject.IsInstanceValid(n))
                    return n;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] card lookup(hand) failed: " + e.Message);
        }

        // ③ 场上兜底
        try
        {
            var found = NCard.FindOnTable(card, null);
            if (found != null && GodotObject.IsInstanceValid(found))
                return found;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] card lookup(table) failed: " + e.Message);
        }

        return null;
    }
}
