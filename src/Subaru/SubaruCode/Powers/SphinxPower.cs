using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;
using Godot;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 大灾·斯芬克斯在场（契约）。
///
/// <para>规则：玩家每累计造成 15 点伤害，就往手牌塞一张随机[尸兵]（每回合最多 2 张）；
/// 这一张本回合费用 -1，并获得[消耗]。契约升级后，塞进来的尸兵是**升级版**。</para>
///
/// <para><b><c>Amount</c> 不是层数，是「升级标记」</b>（1 = 普通 / 2 = 升级），
/// 照抄 <c>VincentPower</c> 的做法：<c>BattleSnapshot</c> 回档时只恢复 <c>Amount</c>，
/// 实例字段会丢，所以升级标记只能放 <c>Amount</c>。<c>StackType.Single</c> 不显示这个数字。</para>
///
/// <para>阈值固定 18，**不再跟升级走**（2026-11-18 改）：升级的收益改成「发出来的尸兵是升级版」。</para>
///
/// <para><b>累计值 / 每回合计数</b>都是实例字段，正常状态下死归后会保留在实例上 ——
/// 所以这里对照 <see cref="DeathReturnSpike.RewindVersion"/>：版本一变就把
/// 「累计伤害 / 本回合已发张数」清回存档点状态（2026-11-18 定：计数随死归回滚）。</para>
///
/// <para>2026-11-18 削弱：阈值 14 → 15，并且**每回合最多发 2 张**。
/// 到上限后不再继续囤积（否则超额伤害会攒着，下回合开局直接连发几张）。</para>
/// </summary>
public sealed class SphinxPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "sphinx_power.png".PowerImagePath();

    public override string CustomBigIconPath => "sphinx_power.png".BigPowerImagePath();

    /// <summary>已累计、还没兑换的伤害。</summary>
    private decimal _accumulated;

    /// <summary>正在发牌时不再重入（发牌本身会触发一堆钩子）。</summary>
    private bool _granting;

    /// <summary>本回合已经发过几张（上限 <see cref="MaxGrantsPerTurn"/>）。</summary>
    private int _grantsThisTurn;

    /// <summary><see cref="_grantsThisTurn"/> 属于哪个回合号，回合一变就清零。</summary>
    private int _grantTurn = -1;

    /// <summary>上次看到的回档版本号：死归后把上面这些计数退回存档点状态。</summary>
    private int _rewindVersion = -1;

    /// <summary>兑换阈值：固定 15（升级只影响发出来的尸兵是不是升级版）。</summary>
    private const decimal Threshold = 15m;

    /// <summary>每回合最多发牌次数（2026-11-18 削弱加的上限）。</summary>
    private const int MaxGrantsPerTurn = 2;

    /// <summary>契约是否升级过（决定发出来的[尸兵]是不是升级版）。</summary>
    private bool GrantsUpgraded => Amount >= 2m;

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel cardSource)
    {
        try
        {
            if (_granting || Owner == null || dealer == null)
                return;

            // 只算「玩家自己造成的伤害」
            if (!ReferenceEquals(dealer, Owner))
                return;

#if GAME_BETA
            var dealt = result?.IntendedHpLoss ?? 0;
#else
            var dealt = result?.UnblockedDamage ?? 0;
#endif
            if (dealt <= 0)
                return;

            // 死归回滚：能力实例可能跨过回档还活着，计数必须退回存档点的状态，
            // 否则「本回合已经发满 2 张」会一直挡着玩家领牌。
            if (_rewindVersion != DeathReturnSpike.RewindVersion)
            {
                _rewindVersion = DeathReturnSpike.RewindVersion;
                _grantsThisTurn = 0;
                _grantTurn = -1;
                _accumulated = 0;
            }

            _accumulated += dealt;

            // 每回合上限：回合号变了就重置计数
            var turn = Owner.Player?.PlayerCombatState?.TurnNumber ?? -1;
            if (turn != _grantTurn)
            {
                _grantTurn = turn;
                _grantsThisTurn = 0;
            }

            while (_accumulated >= Threshold)
            {
                if (_grantsThisTurn >= MaxGrantsPerTurn)
                {
                    // 到上限后不再囤积：继续攒下去，下回合开局会一口气连发几张
                    _accumulated = 0;
                    break;
                }

                _accumulated -= Threshold;
                _grantsThisTurn++;
                await GrantRandomCorpseSoldierAsync(choiceContext);
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] sphinx power failed: " + e);
        }
    }

    /// <summary>塞一张随机[尸兵]进手牌：加[消耗]、本回合费用 -1。</summary>
    private async Task GrantRandomCorpseSoldierAsync(PlayerChoiceContext choiceContext)
    {
        var owner = Owner;
        var player = owner?.Player;
        if (player == null)
            return;

        var state = owner.CombatState;
        var pcs = player.PlayerCombatState;
        if (state == null || pcs == null)
            return;

        var pool = CorpseSoldierPool();
        if (pool.Count == 0)
        {
            GD.PushWarning("[Subaru] sphinx: no corpse soldier cards registered");
            return;
        }

        _granting = true;
        try
        {
            var rng = player.RunState?.Rng?.CombatCardGeneration;
            var canonical = rng != null ? rng.NextItem(pool) : pool[0];

            var card = state.CreateCard(canonical, player);
            if (card == null)
                return;

            // 升级过的契约：发出来的尸兵直接是升级版
            if (GrantsUpgraded)
                Extensions.CardUpgradeSync.UpgradeNow(card);

            // 这一份获得[消耗]（尸兵本体不带，见 CardKeyword 注释）
            card.AddKeyword(CardKeyword.Exhaust);

            // 本回合费用 -1（reduceOnly：不会把涨价固化）
            try
            {
                card.EnergyCost?.AddThisTurn(-1, true);
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] sphinx: cost reduction failed: " + e.Message);
            }

            await BattleSnapshot.AddCardSafely(pcs.Hand, card, pcs);

            GD.Print($"[Subaru] sphinx granted {card.Id} (upgraded={card.IsUpgraded}, exhaust, cost -1 this turn)");
        }
        finally
        {
            _granting = false;
        }
    }

    private static List<CardModel> _pool = [];

    /// <summary>所有带[尸兵]词条的牌（规范实例）。第一次调用时扫一遍，之后缓存。</summary>
    private static List<CardModel> CorpseSoldierPool()
    {
        if (_pool.Count > 0)
            return _pool;

        try
        {
            foreach (var card in ModelDb.AllCards)
            {
                if (card == null)
                    continue;

                if (!card.CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier))
                    continue;

                // 衍生专用牌不参与随机池（尸兵本体目前都是正常牌，这条是防御）
                if (card is SubaruCard subaru && subaru.IsGenerateOnly)
                    continue;

                _pool.Add(card);
            }

            GD.Print($"[Subaru] sphinx: corpse soldier pool = {_pool.Count}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sphinx: build corpse soldier pool failed: " + e.Message);
        }

        return _pool;
    }
}
