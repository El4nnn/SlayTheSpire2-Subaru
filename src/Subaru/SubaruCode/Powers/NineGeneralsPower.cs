using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// [九神将]的连锁折扣：本回合打出过任意一张[九神将]后，本回合剩下的[九神将]费用 -1。
///
/// 折扣只减 1，不叠加（第二张打完后第三张仍然是 -1，不会变成 -2）。
/// 能力由 <see cref="SubaruCard"/> 在第一次打出[九神将]时挂上，之后每回合开始清标记；
/// 不显示图标（它在场上没有可见数值）。
/// </summary>
public sealed class NineGeneralsPower : CustomPowerModel
{
    private bool _playedThisTurn;

    /// <summary>这张牌是不是「我的」。多人里 hooks 全场广播，不判归属会改队友的牌。</summary>
    private bool IsMine(CardModel? card)
    {
        var me = Owner?.Player;
        if (me == null)
            return false;

        var cardOwner = card?.Owner;
        // Owner 为空（canonical / 未落地的临时牌）时按「我的」放行
        return cardOwner == null || ReferenceEquals(cardOwner, me);
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    protected override bool IsVisibleInternal => false;

    public override string CustomPackedIconPath => "madness_power.png".PowerImagePath();

    public override string CustomBigIconPath => "madness_power.png".BigPowerImagePath();

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：每个玩家的回合开始都会进来一次，标记只能由「我的回合」清
        if (!ReferenceEquals(player, Owner?.Player))
            return Task.CompletedTask;

        _playedThisTurn = false;
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card is SubaruCard card
            && card.CanonicalKeywords.Contains(SubaruKeywords.NineGenerals)
            && IsMine(card))
        {
            _playedThisTurn = true;
        }

        return Task.CompletedTask;
    }

    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal cost, out decimal newCost)
    {
        newCost = cost;

        if (!_playedThisTurn || cost <= 0m)
            return false;

        // 多人：只给「我自己的」九神将打折，否则队友的牌也会被我这张能力改费
        if (!IsMine(card))
            return false;

        if (card is not SubaruCard subaru
            || !subaru.CanonicalKeywords.Contains(SubaruKeywords.NineGenerals))
        {
            return false;
        }

        newCost = cost - 1m;
        return true;
    }
}
