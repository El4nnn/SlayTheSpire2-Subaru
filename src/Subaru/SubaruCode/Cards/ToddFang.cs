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

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 陶德·方古：<b>多人模式专属</b>，1 费，普通，攻击，[消耗]。
/// 造成 5 点伤害 2 次（升级 3 次），然后<b>再生成一张</b>塞进另一名玩家的手牌。
///
/// <para>「自己这张耗掉 + 造一张给队友」（用户 2026-11-19 定）：
/// 打出的这张按 [消耗] 正常进消耗堆，**另外**用 <c>CombatState.CreateCard</c> 造一张新牌
/// 交给队友，所以牌不会在两个人之间来回传同一张 —— 每打一次换一张新的。
/// 升级版给出去也必须是升级版（否则卡面写「3 次」结果队友拿到「2 次」）。</para>
///
/// <para>递牌用 <c>CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, receiver, Top)</c>：
/// 这是引擎给「生成的牌放进指定玩家手牌」准备的标准入口（本 mod 的 E·M·T 也用它），
/// 会顺手处理接收方的牌堆、卡牌节点和联机同步。
/// 自己拼 <c>Add</c> 容易把 Owner / 节点归属弄错，对方打出来时会算到你头上。</para>
///
/// <para>接收方必须是活着的玩家；队友全灭时这张牌就只是个 1 费 5×2 的攻击（不再造牌）。
/// 给谁按<b>座位顺序的下一名活玩家</b>（用户 2026-11-19 定的「固定下家」，不随机、也不做手动选，
/// 原因见 <see cref="PickReceiver"/> 的注释）。</para>
/// <para>接收方手牌满了就把新牌放进他的抽牌堆顶，别把牌弄丢（和大兔·增殖同一套保底）。</para>
///
/// <para>为什么不进单人牌池：单人没有队友可给，这张牌就只是个 1 费 5×2 的攻击，
/// 比同费攻击牌强。卡池过滤见 <c>SubaruCardPool</c>。</para>
/// </summary>
public sealed class ToddFang() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    public override string? CutInId => "todd";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(5m, ValueProp.Move),
        new IntVar("Hits", 2m),
    ];

    /// <summary>升级只加段数：2 → 3（每段仍是 5 点）。</summary>
    protected override void OnUpgrade() => DynamicVars["Hits"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var hits = (int)DynamicVars["Hits"].BaseValue;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount(hits)
            .Execute(choiceContext);

        await PassCopyAsync();
    }

    /// <summary>给随机一名活着的队友造一张这张牌（升级状态跟着走）。</summary>
    private async Task PassCopyAsync()
    {
        var receiver = PickReceiver();
        if (receiver == null)
        {
            GD.Print("[Subaru] mp: todd fang no living teammate, no copy passed");
            return;
        }

        var state = CombatState;
        if (state == null)
            return;

        var copy = state.CreateCard<ToddFang>(receiver);
        if (copy == null)
            return;

        if (IsUpgraded && !copy.IsUpgraded)
            Extensions.CardUpgradeSync.UpgradeNow(copy);

        var pcs = receiver.PlayerCombatState;
        if (pcs == null)
            return;

        var name = receiver.Creature?.Name;

        if (pcs.Hand.Cards.Count >= CardPile.MaxCardsInHand)
        {
            await CardPileCmd.AddGeneratedCardToCombat(
                copy, PileType.Draw, receiver, CardPilePosition.Top);
            GD.Print($"[Subaru] mp: todd fang copy -> {name} draw pile (hand full)");
            return;
        }

        await CardPileCmd.AddGeneratedCardToCombat(
            copy, PileType.Hand, receiver, CardPilePosition.Top);
        GD.Print($"[Subaru] mp: todd fang copy -> {name} hand");
    }

    /// <summary>
    /// 接收方 = <b>下一名活着的玩家</b>（用户 2026-11-19 定的「固定下家」方案）。
    ///
    /// <para>顺序取 <c>CombatState.Players</c>（= 联机房间的座位/加入顺序），从自己往后数，
    /// 数到底就绕回第一个；死掉的人跳过（给死人是没意义的，引擎也会拒绝）。
    /// 两人局时结果就是「另一名玩家」；四人局按座位依次传，可预测、没有随机。
    /// 没有活着的队友（单人 / 队友全灭）返回 null。</para>
    ///
    /// <para>为什么不做「手动选」：原版只有一个目标位（<c>CardPlay.Target</c>），
    /// 「选玩家」在原版里就是「这张牌的目标是那名队友」（AnyAlly）；
    /// 我们这张牌的目标已经用来选敌人了，引擎也没有独立的选玩家弹窗，见交接文档 §10.4。</para>
    /// </summary>
    private Player? PickReceiver()
    {
        var owner = Owner;
        var players = owner?.Creature?.CombatState?.Players;
        if (owner == null || players == null)
            return null;

        // 保留 null 位置会让下标和座位错开，所以先剔掉空位再定位自己
        var seats = players.Where(p => p != null && p.Creature != null).ToList();
        var start = seats.FindIndex(p => ReferenceEquals(p, owner));
        if (start < 0 || seats.Count < 2)
            return null;

        for (var step = 1; step < seats.Count; step++)
        {
            var candidate = seats[(start + step) % seats.Count];

            // 绕一圈会回到自己，跳过；死人跳过
            if (ReferenceEquals(candidate, owner))
                continue;

            if (candidate.Creature.CurrentHp > 0)
                return candidate;
        }

        return null;
    }
}
