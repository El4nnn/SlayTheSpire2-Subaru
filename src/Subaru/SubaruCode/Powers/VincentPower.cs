using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;
using Godot;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 文森特在场（鲜血帝·文森特的契约）。
///
/// 和[贝蒂]一样是「状态」而不是召唤物：不占场、不承伤、没有血量，
/// 只负责一条规则：每 2 个玩家回合开始（第 2、4、6…回合），
/// 往手牌塞 1 张随机[九神将]。
///
/// Amount 不用来做层数，只用来记「塞进来的牌要不要升级」（1 = 普通 / 2 = 升级）。
/// 为什么塞进 Amount：回档恢复能力时只恢复 Amount
/// （BattleSnapshot 走 PowerCmd.Apply(canonical, creature, saved.Amount, ...)），
/// 实例字段会丢，所以升级标记只能放 Amount。
/// 玩家看不到这个 2：NPower.RefreshAmount 只给 PowerStackType.Counter 显示数字，
/// Single 的数值文本恒为空。
/// </summary>
public sealed class VincentPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "vincent_power.png".PowerImagePath();

    public override string CustomBigIconPath => "vincent_power.png".BigPowerImagePath();

    /// <summary>契约是否升级过（决定之后塞进来的[九神将]是不是升级版）。</summary>
    private bool GrantsUpgraded => Amount >= 2;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player.Creature != Owner)
                return;

            var turn = player.PlayerCombatState?.TurnNumber ?? 0;
            if (turn <= 0 || turn % 2 != 0)
                return;

            await GrantAsync(choiceContext, player, GrantsUpgraded);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] vincent contract failed: " + e);
        }
    }

    /// <summary>随机塞 1 张[九神将]进手牌。<paramref name="upgraded"/> 为真时塞升级版。</summary>
    internal static async Task GrantAsync(PlayerChoiceContext choiceContext, Player player, bool upgraded)
    {
        try
        {
            var state = player.Creature.CombatState;
            var pcs = player.PlayerCombatState;
            if (state == null || pcs == null)
                return;

            var candidates = Candidates();
            var rng = player.RunState?.Rng?.CombatCardGeneration;
            var canonical = rng != null ? rng.NextItem(candidates) : candidates[0];

            var card = state.CreateCard(canonical, player);
            if (card == null)
                return;

            if (upgraded)
                Extensions.CardUpgradeSync.UpgradeNow(card);

            await BattleSnapshot.AddCardSafely(pcs.Hand, card, pcs);

            GD.Print($"[Subaru] vincent: granted {card.Id} (upgraded={card.IsUpgraded})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] vincent grant failed: " + e);
        }
    }

    /// <summary>十张[九神将]。</summary>
    private static List<CardModel> Candidates() =>
    [
        ModelDb.Card<SpiritEater>(),
        ModelDb.Card<BlueLightning>(),
        ModelDb.Card<SoulMarriage>(),
        ModelDb.Card<Orbart>(),
        ModelDb.Card<Gurobi>(),
        ModelDb.Card<Madeleine>(),
        ModelDb.Card<Moguro>(),
        ModelDb.Card<Gotz>(),
        ModelDb.Card<Ballroy>(),
        ModelDb.Card<Kishia>(),
        ModelDb.Card<Kafma>(),
        ModelDb.Card<Kulgan>(),
    ];
}
