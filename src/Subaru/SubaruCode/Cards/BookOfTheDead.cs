using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Character;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 死者之书：先古，2 费，技能，[消耗]。
/// 将一张随机[普通]、一张[罕见]、一张[稀有]牌加入手牌，它们都获得[消耗]，且本回合内免费打出。
/// 升级：费用不变（仍 2），加入的牌变成升级版。
///
/// <para>2026-11-18 增强：原来是「1 张随机稀有牌、本回合免费」，改成三种稀有度各一张 + 各自带[消耗]。</para>
///
/// <para>2026-11-18 平衡：费用 1 → 2，升级不再减费（改为加入的牌为升级版）——
/// 三种稀有度各一张配 1 费太便宜。升级版走 <c>UpgradeInternal()</c>，
/// 和「大灾·斯芬克斯」发升级版尸兵、贝蒂契约造迷尼亚+ 是同一个做法。</para>
///
/// <para>随机走 <c>RunState.Rng.CombatCardGeneration</c>（游戏自己的随机流），所以 SL 重来结果一致。
/// 池子按稀有度动态扫 <c>ModelDb.AllCards</c>，并且过滤掉：
/// 衍生专用牌（<c>IsGenerateOnly</c>）、状态/诅咒/任务牌、当前路线不允许的牌、
/// 以及 epoch 还没解锁的牌（复用 <see cref="SubaruCardPool"/> 的解锁表）。</para>
/// </summary>
public sealed class BookOfTheDead() : SubaruCard(2, CardType.Skill, CardRarity.Ancient, TargetType.Self)
{
    // table-upgrade: 加入的牌为升级版

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    /// <summary>加牌之外的生命代价。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars => [new HpLossVar(4m)];

    // 升级不减费：效果在加牌时读 IsUpgraded（加入的牌为升级版），这里刻意留空。
    protected override void OnUpgrade() { }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        var state = CombatState;
        if (player == null || pcs == null || state == null)
            return;

        var created = new List<CardModel>();

        // 「发现一张新牌」而不是复制：开白名单，允许随机到腐蚀牌
        using (Subaru.SubaruCode.Patches.CardCreationPolicy.Allow())
        {
            foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
            {
                var canonical = RollOne(rarity, player);
                if (canonical == null)
                {
                    GD.PushWarning($"[Subaru] book of the dead: no {rarity} card available");
                    continue;
                }

                var card = state.CreateCard(canonical, player);
                if (card == null)
                    continue;

                // 升级：加入的牌是升级版（先升级，再补词条/改费，顺序别反）
                if (IsUpgraded)
                    Extensions.CardUpgradeSync.UpgradeNow(card);

                // 这一份获得[消耗]（本体不带）
                card.AddKeyword(CardKeyword.Exhaust);

                // 本回合内免费打出（reduceOnly：不会把涨价固化）
                try
                {
                    card.EnergyCost?.SetThisTurnOrUntilPlayed(0, true);
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] book of the dead: free cost failed: " + e.Message);
                }

                created.Add(card);
            }
        }

        foreach (var card in created)
        {
            if (pcs.Hand.Cards.Count >= 10)
                await CardPileCmd.Add(card, pcs.DrawPile, CardPilePosition.Top, null, false);
            else
                await CardPileCmd.Add(card, pcs.Hand, CardPilePosition.Top, null, false);
        }

        // 代价写在最后：先把牌加进手牌，再失去生命（万一这一步把人打死，
        // 前半段收益已经结算完，不会白亏一半）
        await CreatureCmd.Damage(choiceContext, player.Creature,
            DynamicVars.HpLoss.BaseValue,
            ValueProp.Unblockable, player.Creature);

        GD.Print($"[Subaru] book of the dead: created {created.Count} -> "
                 + string.Join(", ", created.Select(c => c.Id)));
    }

    /// <summary>按稀有度从「本 mod 的可用牌」里随机取一张（不重复、不取衍生牌）。</summary>
    private static CardModel? RollOne(CardRarity rarity, Player player)
    {
        try
        {
            var runState = player.RunState;
            var rng = runState?.Rng?.CombatCardGeneration;

            var pool = new List<CardModel>();

            foreach (var card in ModelDb.AllCards)
            {
                if (card is not SubaruCard subaru)
                    continue;

                if (subaru.IsGenerateOnly)
                    continue;

                if (card.Rarity != rarity)
                    continue;

                // 状态 / 诅咒 / 任务牌不进随机池
                if (card.Type is CardType.Status or CardType.Curse or CardType.Quest)
                    continue;

                if (runState != null && !subaru.IsAllowedOnPath(runState))
                    continue;

                // epoch 还没解锁的牌不提前发
                if (!SubaruCardPool.IsUnlockedForGeneration(card))
                    continue;

                pool.Add(card);
            }

            if (pool.Count == 0)
                return null;

            return rng != null ? rng.NextItem(pool) : pool[0];
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] book of the dead roll failed: " + e.Message);
            return null;
        }
    }
}
