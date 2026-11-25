using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 仪式兽「耳鸣」（<see cref="RingingPower"/>）的死归回滚补丁。
///
/// <para><b>为什么需要</b>：耳鸣的放行判定（原版 IL 实证）是
/// 「这张牌带 Ringing 附魔，且**本回合还没出过任何一张牌**」才允许打出：
/// <c>RingingPower.ShouldPlay</c> → <c>History.CardPlaysStarted.Any(e =&gt; e.HappenedThisTurn(state) &amp;&amp; 属于该玩家)</c>。
/// 而 <c>HappenedThisTurn</c> 比的是三个计数器：
/// <c>RoundNumber</c> + <c>CurrentSide</c> + <c>PlayerCombatState.TurnNumber</c>。
/// </para>
///
/// <para><b>冲突点</b>：我们的回档会把 <c>RoundNumber</c> / <c>TurnNumber</c> 写回存档点，
/// 但**战斗历史不回滚**。于是回档之后，当前回合号倒回了存档点，历史里却还留着
/// 「存档点之后那段被打回的时间线」的出牌记录，两者的 (RoundNumber, Side, TurnNumber) 一模一样
/// → 引擎认定「本回合已经出过牌」→ 手上带耳鸣的牌全部打不出去。
/// 玩家看到的就是「一张牌都没打，却出不了牌」。
/// </para>
///
/// <para><b>做法</b>：只回滚与耳鸣相关的两样东西，不动全局历史框架——
/// 1) 把「本回合的出牌记录」裁回到存档点时的条数（超出部分都是被回滚掉的时间线）；
/// 2) 存档点上没有耳鸣 → 卸掉 RingingPower（它的 AfterRemoved 会清掉卡上的附魔），
///    再兜底清一遍残留的 Ringing 附魔与对应的历史条目。
/// 裁剪历史这件事项目里已有先例：魂缚（Chains of Binding）就是这么做的
/// （<c>BattleSnapshot.RestoreChainsState</c>，同样是反射取 <c>CombatHistory._entries</c> 后 RemoveAt）。
/// </para>
///
/// 只处理仪式兽这一条线；其它依赖 <c>HappenedThisTurn</c> 的判定不在本补丁范围内。
/// </summary>
public static class CeremonialRingingRollback
{
    /// <summary>存档点那一刻「本回合已出过几张牌」（历史口径）。</summary>
    public static int CountCardPlaysThisTurn(ICombatState? state, Player? player)
    {
        if (state == null || player?.Creature == null)
            return 0;

        var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
        if (history == null)
            return 0;

        var count = 0;
        foreach (var entry in history.Entries)
        {
            if (entry is CardPlayStartedEntry play
                && play.Actor == player.Creature
                && play.HappenedThisTurn(state))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>存档点那一刻玩家身上有没有耳鸣。</summary>
    public static bool HasRinging(Player? player)
    {
        return player?.Creature?.Powers?.Any(p => p is RingingPower) == true;
    }

    /// <summary>回档收尾调用：把耳鸣相关状态回滚到存档点口径。</summary>
    public static async Task ApplyAsync(
        PlayerChoiceContext ctx,
        ICombatState? state,
        Player? player,
        int savepointPlays,
        bool ringingAtSavepoint)
    {
        if (state == null || player?.Creature == null)
            return;

        var creature = player.Creature;
        var removed = 0;

        try
        {
            var entries = RawEntries();
            if (entries != null)
            {
                // 1) 本回合的出牌记录裁回存档点条数
                var kept = 0;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not CardPlayStartedEntry play)
                        continue;
                    if (play.Actor != creature)
                        continue;
                    if (!play.HappenedThisTurn(state))
                        continue;

                    kept++;
                    if (kept > savepointPlays)
                    {
                        entries.RemoveAt(i);
                        i--;
                        removed++;
                    }
                }

                // 2) 存档点没有耳鸣 → 连「耳鸣附魔写入历史」一起裁掉
                if (!ringingAtSavepoint)
                {
                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (entries[i] is CardAfflictedEntry afflicted
                            && afflicted.Actor == creature
                            && afflicted.Affliction is Ringing
                            && afflicted.HappenedThisTurn(state))
                        {
                            entries.RemoveAt(i);
                            i--;
                            removed++;
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] ringing history rollback failed: " + e.Message);
        }

        // 3) 存档点没有耳鸣 → 卸掉这份能力（AfterRemoved 会清掉卡上的 Ringing 附魔）
        if (!ringingAtSavepoint)
        {
            foreach (var power in creature.Powers.Where(p => p is RingingPower).ToList())
            {
                try
                {
                    await PowerCmd.Remove(power);
                    Godot.GD.Print("[Subaru] ringing rollback: RingingPower removed (not in savepoint)");
                }
                catch (System.Exception e)
                {
                    Godot.GD.PushWarning("[Subaru] ringing rollback remove failed: " + e.Message);
                }
            }

            // 4) 兜底：能力已经没了但牌上还挂着附魔（回档重建牌的边角情况）
            var cleared = ClearOrphanRinging(player);
            if (cleared > 0)
                Godot.GD.Print($"[Subaru] ringing rollback: cleared {cleared} orphan affliction(s)");
        }

        if (removed > 0)
            Godot.GD.Print(
                $"[Subaru] ringing rollback: trimmed {removed} history entr(ies), " +
                $"savepoint plays={savepointPlays}, ringing at savepoint={ringingAtSavepoint}");
    }

    /// <summary>反射拿 <c>CombatHistory._entries</c>（公开的 Entries 只是只读 IEnumerable）。</summary>
    private static IList? RawEntries()
    {
        var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
        if (history == null)
            return null;

        var field = typeof(CombatHistory).GetField(
            "_entries", BindingFlags.Instance | BindingFlags.NonPublic);

        return field?.GetValue(history) as IList;
    }

    private static int ClearOrphanRinging(Player player)
    {
        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return 0;

        var cleared = 0;
        foreach (var card in AllCards(pcs))
        {
            if (card?.Affliction is not Ringing)
                continue;

            try
            {
                CardCmd.ClearAffliction(card);
                cleared++;
            }
            catch (System.Exception e)
            {
                Godot.GD.PushWarning("[Subaru] clear ringing affliction failed: " + e.Message);
            }
        }

        return cleared;
    }

    private static IEnumerable<CardModel> AllCards(PlayerCombatState pcs)
    {
        return pcs.Hand.Cards
            .Concat(pcs.DrawPile.Cards)
            .Concat(pcs.DiscardPile.Cards)
            .Concat(pcs.ExhaustPile.Cards)
            .Concat(pcs.PlayPile.Cards);
    }
}
