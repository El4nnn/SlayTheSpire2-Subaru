using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「本回合打出过的能力牌」记账。
///
/// 能力牌（CardType.Power）打出后会从战斗牌里消失（不进任何牌堆），
/// 所以死归按快照恢复时它不会被还原 —— 玩家会看到「契约·贝蒂打完之后就没了」。
///
/// 处理方式：
///   1. 每打出一张能力牌，记下它的**原型（Canonical）**和是否已升级
///   2. 抓死归快照（设置存档点）时清空记录 —— 存档点之前打出的牌本来就不该回来
///   3. 死归恢复牌堆之后，把记录里「现在不在任何牌堆上」的牌重新造一张放回手牌
///
/// 关键：记录必须存 CanonicalInstance，不能存打出时那个可变实例。
/// 可变实例交给 state.CreateCard 会抛 MutableModelException（贝蒂之前就是这么失败的）。
/// </summary>
public static class PlayedPowerCards
{
    private sealed class Entry
    {
        public CardModel Canonical;
        public bool Upgraded;

        // 打出那一刻的「本场费用覆盖」（宝石面具给的就是这个：这张牌本场 0 费）。
        // 重建副本时只复制升级状态是不够的，费用会变回原型价。
        public int? CombatEnergyCost;
        public int? CombatStarCost;

        /// <summary>
        /// 这张牌在存档点里存不存在。
        ///
        /// <para>能力药水 / 幼女使者 / 契约变形这类**本场生成**的能力牌：
        /// 打出时也会被记一笔，但它连存档点都没有，死归后补回手牌就是白赚一张
        /// （2026-11-21 实测：力量药水选的牌被删掉后又被补回来）。</para>
        /// </summary>
        public bool InSnapshot = true;
    }

    private static readonly List<Entry> Played = new();
    private static ICombatState _state;

    private static void Ensure(ICombatState state)
    {
        if (state == null)
            return;

        if (!ReferenceEquals(_state, state))
        {
            _state = state;
            Played.Clear();
        }
    }

    /// <summary>
    /// 这张能力牌打出时是不是升级版。
    ///
    /// 用途：能力牌打出后就从战斗牌里消失了，死归时按记录重放；像「契约·贝蒂」这种
    /// 「升级后造出来的牌也变升级版」的卡，重放时得知道当初打的是不是升级版
    /// （否则回档后变出来的迷尼亚会掉回普通版）。
    /// </summary>
    public static bool WasUpgraded<T>() where T : CardModel
    {
        foreach (var entry in Played)
        {
            if (entry.Canonical is T)
                return entry.Upgraded;
        }

        return false;
    }

    /// <summary>打出一张牌时调用（只记能力牌）。</summary>
    public static void Record(ICombatState state, CardModel card)
    {
        try
        {
            if (card == null || card.Type != CardType.Power)
                return;

            var canonical = card.CanonicalInstance;
            if (canonical == null)
            {
                GD.PushWarning("[Subaru] power card has no canonical: " + card.Id);
                return;
            }

            Ensure(state);

            var entry = new Entry
            {
                Canonical = canonical,
                Upgraded = card.IsUpgraded,
                // 判据是实例引用：快照里存的就是这张牌对象（牌后来被打出也不会变）。
                // 药水给的牌是快照之后才造出来的 → 这里查不到 → 死归时不补。
                InSnapshot = DeathReturnSpike.SavepointContainsCard(card),
            };

            try
            {
                entry.CombatEnergyCost = card.EnergyCost?.GetResolved();
            }
            catch
            {
            }

            try
            {
                entry.CombatStarCost = card.GetStarCostThisCombat();
            }
            catch
            {
            }

            Played.Add(entry);
            GD.Print($"[Subaru] recorded power card: {card.Id} (upgraded={card.IsUpgraded}, "
                     + $"inSnapshot={entry.InSnapshot})");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] record power card failed: " + e);
        }
    }

    /// <summary>设置存档点（抓快照）时调用：这之前的都不该回来。</summary>
    public static void ClearFromSavepoint()
    {
        if (Played.Count > 0)
            GD.Print($"[Subaru] power card record cleared at savepoint ({Played.Count})");

        Played.Clear();
    }

    /// <summary>死归恢复完牌堆之后调用：把丢失的能力牌补回手牌。</summary>
    public static async Task RestoreAsync(
        PlayerChoiceContext ctx, ICombatState state, Player player)
    {
        try
        {
            if (Played.Count == 0 || player == null)
                return;

            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            foreach (var entry in Played.ToList())
            {
                if (entry?.Canonical == null)
                    continue;

                // 存档点里本来就没有的牌（药水 / 幼女使者 / 契约变形造出来的）：
                // 打进战斗牌里又被删除是正确结果，**不能**再造一张塞回手牌。
                if (!entry.InSnapshot)
                {
                    GD.Print($"[Subaru] power card not in savepoint, skip restore: {entry.Canonical.Id}");
                    continue;
                }

                // [腐蚀]牌不补回手牌：它由 CorrodedCards.Purge 负责丢进消耗堆
                // （本场不能再打，战斗结束照常回牌组）。
                if (CorrodedCards.IsCorroded(entry.Canonical))
                {
                    GD.Print($"[Subaru] power card is corroded, skip restore: {entry.Canonical.Id}");
                    continue;
                }

                // 还在某个牌堆上就不用补（牌堆里是可变实例，按原型比对）
                if (pcs.AllPiles.Any(pile =>
                        pile.Cards.Any(c => c != null && c.CanonicalInstance == entry.Canonical)))
                    continue;

                var copy = state.CreateCard(entry.Canonical, player);
                if (copy == null)
                    continue;

                if (entry.Upgraded && !copy.IsUpgraded)
                    Extensions.CardUpgradeSync.UpgradeNow(copy);

                // 费用覆盖要补回（宝石面具那类「本场免费」的牌）
                BattleSnapshot.ApplyCombatCostOverride(copy, new BattleSnapshot.CardExtraState
                {
                    CombatEnergyCost = entry.CombatEnergyCost,
                    CombatStarCost = entry.CombatStarCost,
                });

                await BattleSnapshot.AddCardSafely(pcs.Hand, copy, pcs);
                GD.Print($"[Subaru] restored power card to hand: {copy.Id}");
            }

            Played.Clear();
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] restore power cards failed: " + e);
        }
    }
}
