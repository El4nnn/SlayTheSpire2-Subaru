using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 贝蒂在场。
///
/// 按设定，贝蒂是一种「状态」，不是召唤物：不占场、不承伤、没有血量，
/// 只负责两件事：
///   1) 带[贝蒂]词条的牌在贝蒂不在场时不能打出
///   2) 带[阴魔法]词条的牌：你每回合打出的第一张费用 -1
///
/// 「第一张」的判定用回合号：只有在本回合还没打出过阴魔法时才给折扣，
/// 不靠 AfterPlayerTurnStart 重置——存档/回档/中途获得贝蒂都不会错位。
/// </summary>
public sealed class BeatricePower : CustomPowerModel
{
    /// <summary>上一次打出[阴魔法]的回合号（-1 = 本场还没打过）。</summary>
    /// <summary>
    /// 上一次打出[阴魔法]的回合号。
    ///
    /// <para>初值必须是一个「永远不会等于真实回合号」的值：多人里
    /// <c>PlayerCombatState.TurnNumber</c> 拿不到时会退化成 -1，而初值若是 -1，
    /// 就会变成「第一回合永远判成已经打过」→ 首回合折扣失效（2026-11-19 玩家报的问题之一）。</para>
    /// </summary>
    private int _lastYinTurn = int.MinValue;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override string CustomPackedIconPath => "beatrice_power.png".PowerImagePath();

    public override string CustomBigIconPath => "beatrice_power.png".BigPowerImagePath();

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    /// <summary>这张牌是不是「我的」。多人里 hooks 是全场广播的，不判归属会去改队友的牌。</summary>
    private bool IsMine(CardModel card)
    {
        var me = Owner?.Player;
        if (me == null || card == null)
            return false;

        var cardOwner = card.Owner;
        // Owner 为空（图鉴 canonical / 未落地的临时牌）时无法判断，按「我的」放行，
        // 否则会在某些生成路径上把折扣整个吞掉；能判断时必须是同一名玩家。
        return cardOwner == null || ReferenceEquals(cardOwner, me);
    }

    /// <summary>当前回合号；拿不到返回 -1。</summary>
    private int CurrentTurn() => Owner?.Player?.PlayerCombatState?.TurnNumber ?? -1;

    /// <summary>本回合是否还能吃到[阴魔法]折扣。</summary>
    private bool CanDiscount(CardModel card, decimal cost)
    {
        if (cost <= 0m)
            return false;

        if (card is not SubaruCard subaru)
            return false;

        if (!IsMine(card))
            return false;

        // 标了「贝蒂免费用」的牌（目前没有）优先归零
        if (subaru.FreeWithBeatrice)
            return false;

        if (!subaru.CanonicalKeywords.Contains(SubaruKeywords.YinMagic))
            return false;

        var turn = CurrentTurn();

        // 回合号读不到就别因为「上回合打过」把折扣吞掉：宁可放行
        if (turn < 0)
            return true;

        return turn != _lastYinTurn;
    }

    /// <summary>贝蒂在场时，用[阴魔法]标记的牌费用 -1（每回合第一张）。</summary>
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal cost, out decimal newCost)
    {
        newCost = cost;

        // 多人：hooks 会把全场每一张牌都问一遍，先确认这张牌是我自己的
        if (!IsMine(card))
            return false;

        if (card is SubaruCard { FreeWithBeatrice: true })
        {
            newCost = 0m;
            return true;
        }

        if (!CanDiscount(card, cost))
            return false;

        newCost = System.Math.Max(0m, cost - 1m);
        LogDiscountOnce(card, cost, newCost);
        return true;
    }

    /// <summary>
    /// 多人排查用：同一 (回合, 牌) 只打一行，确认「折扣到底给没给、给了哪张」。
    /// 费用钩子会被每帧查询，不加去重会刷屏。
    /// </summary>
    private static readonly HashSet<string> _mpLogged = [];

    private void LogDiscountOnce(CardModel card, decimal cost, decimal newCost)
    {
        if (!MultiplayerSupport.IsMultiplayer)
            return;

        var key = $"{CurrentTurn()}:{card?.Id}";
        if (!_mpLogged.Add(key))
            return;

        Godot.GD.Print($"[Subaru] mp beatrice: discount {card?.Id} {cost} -> {newCost} "
                       + $"(turn {CurrentTurn()}, last yin turn {_lastYinTurn})");
    }

    /// <summary>记账：本回合打过阴魔法之后，剩下的阴魔法都按原价。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 只记「我自己」打出的阴魔法：多人里队友打牌也会广播到这条钩子，
        // 记到别人头上会把自己的首回合折扣提前用掉（2026-11-19 玩家报的问题）。
        if (cardPlay.Card is SubaruCard card
            && card.CanonicalKeywords.Contains(SubaruKeywords.YinMagic)
            && IsMine(card))
        {
            _lastYinTurn = CurrentTurn();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 玩家回合结束：自动打出一张[临时迷尼亚]（契约升级过 → 临时迷尼亚+）。
    ///
    /// <para>Amount 装的是契约的升级标记（1 = 普通 / 2 = 升级），和斯芬克斯/文森特同一套写法：
    /// 快照只恢复 Amount，实例字段会被丢，所以标记必须放在这里。</para>
    ///
    /// <para>这次自动打出不受[怠惰]那种「每回合最多打出 N 张」的限制 —— 它是贝蒂自己的效果，
    /// 不占玩家的出牌名额（<see cref="Patches.AutoPlayLimitBypass"/>）。</para>
    /// </summary>
    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player)
                return;

            var owner = Owner;
            var player = owner?.Player;
            if (owner == null || player == null)
                return;

            if (participants != null && !participants.Contains(owner))
                return;

            var state = owner.CombatState;
            if (state?.HittableEnemies == null || !state.HittableEnemies.Any())
                return;

            var card = state.CreateCard<MinyaTemp>(player);
            if (card == null)
                return;

            if (Amount >= 2m && !card.IsUpgraded)
                Extensions.CardUpgradeSync.UpgradeNow(card);

            // 先加进手牌（直接加进 Hand 不受 10 张上限拦截），再像诅咒那样从手里打出去：
            // 不占玩家的出牌次数、不受怠惰限制，打出后按卡面自带的[消耗]进消耗堆。
            await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, null, false);

            var target = state.HittableEnemies.FirstOrDefault();

            using (Patches.AutoPlayLimitBypass.Allow())
            {
                await CardCmd.AutoPlay(
                    choiceContext, card, target, AutoPlayType.None,
                    skipXCapture: false, skipCardPileVisuals: false);
            }

            GD.Print($"[Subaru] beatrice: end of turn added+played minya (upgraded={card.IsUpgraded})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] beatrice auto-play failed: " + e);
        }
    }
}
