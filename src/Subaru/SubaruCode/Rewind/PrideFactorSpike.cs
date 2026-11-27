using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Godot;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Relics;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 傲慢因子（初始遗物）：锁血 + 回合开始回档。
/// 每次死归后本场战斗 +2 力量 +1 敏捷（由 DeathReturnSpike.ApplyRewindBlessing 结算）。
/// 升级版把初始傲慢从 3 提到 4。
/// </summary>
public class PrideFactorSpike : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    /// <summary>战斗开始时的傲慢层数（升级版 +1）。</summary>
    public virtual int StartingPride => 3;

    /// <summary>致命伤害只留 1 点生命，让敌人回合正常走完。</summary>
    public override decimal ModifyHpLostAfterOsty(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card)
    {
        return DeathReturnSpike.CapLethalDamage(target, amount, props, dealer, card);
    }

    /// <summary>
    /// 回合开始流程**走完时**再补一次回档。
    ///
    /// <para>为什么需要：致命伤害如果落在「回合开始流程内部」（`AfterPlayerTurnStartLate` 之后、
    /// `AfterSideTurnStart` 一带），<c>OnPlayerTurnStart</c> 里那句 <c>if (_pending)</c> 已经判过了，
    /// 这次死亡会被漏到下一回合才开始执行 —— 玩家看到的是「应该死归却没回档」，
    /// 然后被迫带着 1 血打完整个回合，下一回合才回档，观感就成了「一直死归」。</para>
    ///
    /// <para>安全边界：此时回合机已经走完玩家回合开始、还没把操作权交给玩家，
    /// 不在敌方行动链里；回档自己重放 <c>AfterSideTurnStart</c> 时会因为
    /// <c>_pending</c> 已被清掉而直接返回，不会递归。</para>
    /// </summary>
    public override async Task AfterSideTurnStart(
        MegaCrit.Sts2.Core.Combat.CombatSide side,
        IReadOnlyList<Creature> participants,
        MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player)
            return;

        if (!MultiplayerSupport.RewindEnabled)
        {
            // 多人没有死归：傲慢因子改成本场开战给一次力/敏（用户定的「单机方案 1 的数值 ×2」）。
            // 每场只给一次，判重由 DeathReturnSpike 内部按 CombatState 实例做。
            await DeathReturnSpike.ApplyMultiplayerOpeningBonusAsync(
                Owner, new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext());
            return;
        }

        if (!DeathReturnSpike.HasPendingRewind)
            return;

        await DeathReturnSpike.FlushPendingRewindAsync(combatState, Owner);
    }

    public override Task BeforeCombatStart()
    {
        // 战斗开始就把死归账本清干净。
        //
        // 之前只在 AfterCombatEnd 清账，但玩家「真死」结束本局时那个钩子不会触发
        // （战斗没有正常结束），于是 DeathCount / 力量敏捷累计 / 欠的遗香与瘴气
        // 全部漏到下一局：新开局第一场战斗如果先按「立即死归」，
        // 就会按上一局的死亡次数一次性补发 2×N 点力量与敏捷。
        DeathReturnSpike.ResetForCombat();

        // 让「傲慢」的保底层数跟着这件遗物走
        DeathReturnSpike.MinPride = StartingPride;

        // 时间线维护：「崩坏的开始」等按条件发放，顺带清掉缺前置的节点
        Subaru.SubaruCode.Patches.EpochMaintenancePatch.TryMaintainEpochs(Owner);

        // 先古契约：战斗开始时压到抽牌堆最底，保证起手 5 张绝不抽到它；
        // 抽完牌再由 AfterPlayerTurnStartLate 单独塞进手牌（不占抓牌位）。
        AncientContractHand.MoveToDrawPileBottom(Owner);

        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：这条钩子对每个玩家的回合开始都会触发。死归状态和「先古契约进手牌」
        // 都只属于我自己，队友的回合必须直接跳过（否则会把我的契约牌塞进他手里）。
        if (!ReferenceEquals(player, Owner))
            return;

        var state = player.Creature?.CombatState;

        if (state != null)
            await DeathReturnSpike.OnPlayerTurnStart(state, choiceContext, player);

        // 先古契约：第一回合（抽牌结束后）直接进手牌，不占起手抽牌位。
        // 放在 OnPlayerTurnStart 之后：死归回档会重建牌堆，必须等还原完再塞。
        await Cards.AncientContractHand.MoveIntoHandAsync(choiceContext, player);
    }

    /// <summary>
    /// 魔女瘴气：每张在手牌里的瘴气，回合结束让玩家失去 1 点生命。
    ///
    /// 原来挂在卡牌自己的 <c>HasTurnEndInHandEffect</c> 上，但引擎对这个开关的处理是
    /// 「先搬进出牌堆 → 结算效果 → 再搬去结算堆」，所以牌每回合都会被抽走，
    /// [保留] 关键词拦不住。这里改成由遗物在回合结束时自己结算：
    /// 牌留在手上，扣血照旧（顺序上先于弃手牌，观感一致）。
    /// </summary>
    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        MegaCrit.Sts2.Core.Combat.CombatSide side,
        IEnumerable<Creature> creatures)
    {
        try
        {
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player)
                return;

            var owner = Owner;
            var pcs = owner?.PlayerCombatState;
            if (owner?.Creature == null || pcs == null)
                return;

            var count = 0;
            foreach (var card in pcs.Hand.Cards)
            {
                if (card is CurseWitchMiasma)
                    count++;
            }

            if (count <= 0)
                return;

            var hasBuffer = owner.Creature.Powers.Any(p => p is BufferPower);
            var hasSlippery = owner.Creature.Powers.Any(p => p is SlipperyPower);

            for (var i = 0; i < count; i++)
            {
                // 瘴气扣血**不吃缓冲 / 滑溜**（2026-11-20 用户定）：开一个窗口，
                // 让 Patches.MiasmaBleed 那边的补丁把这两个能力的拦截短路掉。
                using (Patches.MiasmaBleed.Enter())
                {
                    await CreatureCmd.Damage(
                        choiceContext,
                        new[] { owner.Creature },
                        1m,
                        ValueProp.Unblockable,
                        owner.Creature);
                }
            }

            Godot.GD.Print($"[Subaru] miasma end-of-turn damage: {count} card(s)" +
                           $", buffer={(hasBuffer ? "ignored" : "-")}, slippery={(hasSlippery ? "ignored" : "-")}");
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] miasma end-of-turn damage failed: " + e.Message);
        }
    }

    /// <summary>
    /// 「本场战斗造成过伤害的次数」记账（过路魔·塞蕾丝缇雅的 N）。
    /// 挂在初始遗物上：它每局都在，而且遗物的 AfterDamageGiven 每个伤害结算都会收到一次。
    ///
    /// <para><b>2026-11-19 修正</b>：参数顺序写反过。引擎的签名是
    /// <c>AfterDamageGiven(choiceContext, dealer, result, props, target, cardSource)</c>
    /// （见 <c>Hook.AfterDamageGiven</c> 的 sig），旧代码把第 2 个参数当成 target、
    /// 第 5 个当成 dealer，于是判的是「玩家被打」而不是「玩家打人」——
    /// 计数器只会在挨打时涨。现在按真实顺序声明。</para>
    /// </summary>
    public override Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        MegaCrit.Sts2.Core.Entities.Creatures.DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? card)
    {
        if (dealer != null && Owner?.Creature != null && ReferenceEquals(dealer, Owner.Creature) && result != null
            #if GAME_BETA
                && result.IntendedHpLoss > 0)
#else
                && result.UnblockedDamage > 0)
#endif
        {
            CombatStats.OnDamageGiven();
        }

        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        DeathReturnSpike.ResetForCombat();

        // 「帝国的剑狼」自建计数器：首领战胜利 +1（统计没记上的话，我们自己记）
        try
        {
            var bossWon = room != null && room.RoomType == RoomType.Boss;
            Subaru.SubaruCode.Timeline.SwordWolfProgress.OnCombatEnded(bossWon);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] sword wolf combat-end hook failed: " + e.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>升级版：初始傲慢 +1。</summary>
    public override RelicModel GetUpgradeReplacement() => ModelDb.Relic<PrideFactorPlus>();
}
