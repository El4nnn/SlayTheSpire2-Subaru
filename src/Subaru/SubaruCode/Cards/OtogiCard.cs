using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 大兔·增殖：普通，0 费，攻击。
/// 造成 1 点伤害；打出的这张回到手牌，同时再复制 1 张加入手牌。
/// 每打一次手牌 +1，靠手牌上限自然刹车——这就是它「刷伤害」的代价。
/// 手牌已满时无法打出。升级：伤害 1 → 3。
/// </summary>
public sealed class Otogi() : SubaruCard(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(1m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);

    /// <summary>
    /// 手牌已满时不能打出：这张牌打出后会回到手牌、再复制一张加入手牌，
    /// 手牌满了就没有增殖空间，打出去只剩一次伤害。
    /// </summary>
    protected override bool IsPlayable
    {
        get
        {
            var pcs = Owner?.PlayerCombatState;
            if (pcs != null && pcs.Hand.Cards.Count >= CardPile.MaxCardsInHand)
                return false;

            return base.IsPlayable;
        }
    }

    /// <summary>打出后不进弃牌堆，而是回到手牌。（0.111 起改用 CardLocation）</summary>
    public override CardLocation ModifyCardPlayResultLocation(
        CardModel card,
        bool isAutoPlay,
        ResourceInfo resources,
        CardLocation location)
    {
        if (card != this)
            return location;

        return new CardLocation(location.player, PileType.Hand, CardPilePosition.Top);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var player = Owner;
        var state = CombatState;
        if (player == null || state == null)
            return;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        // 增殖：再来一张。
        // 升级过的「大兔·增殖」复制的也必须是升级版，否则卡面写着「伤害 3」却增殖出一张「伤害 1」
        // （2026-11-18 反馈：大兔+ 复制出来的是大兔）。
        var copy = state.CreateCard<Otogi>(player);
        if (copy == null)
            return;

        if (IsUpgraded && !copy.IsUpgraded)
            Extensions.CardUpgradeSync.UpgradeNow(copy);

        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return;

        if (pcs.Hand.Cards.Count >= 10)
        {
            // 手牌满了就进抽牌堆顶，别把牌弄丢
            await CardPileCmd.Add(copy, pcs.DrawPile, CardPilePosition.Top, null, false);
            Godot.GD.Print("[Subaru] otogi: hand full, copy pushed to draw");
            return;
        }

        await CardPileCmd.Add(copy, pcs.Hand, CardPilePosition.Top, null, false);
        Godot.GD.Print("[Subaru] otogi: copy added to hand");
    }
}
