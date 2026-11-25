using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 强欲魔女·艾姬多娜：「睿智之书」＝世界的记忆。
/// 每打出一张技能牌，抽 1 张牌（求知欲没有尽头）。
/// 不是【腐蚀】，所以死归后会随快照一起被清掉——想继续读就得再打一次。
/// </summary>
public sealed class EchidnaPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    // 2026-11-19：改成可叠层 —— 每层每打出一张技能牌就多抽 1 张
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "echidna_power.png".PowerImagePath();
    public override string CustomBigIconPath => "echidna_power.png".BigPowerImagePath();

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            var player = owner?.Player;
            if (player == null)
                return;

            var card = cardPlay.Card;
            if (card?.Type != CardType.Skill)
                return;

            // 多人里 AfterCardPlayed 是全场的：队友打出技能牌也会广播到我这层能力。
            // 睿智之书只认**持有者自己**打出的牌（2026-11-19 玩家报「会给队友抽牌」）。
            if (!ReferenceEquals(card.Owner, player))
                return;

            var draws = Amount <= 0m ? 1 : (int)Amount;
            for (var i = 0; i < draws; i++)
                await CardPileCmd.Draw(choiceContext, player);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] echidna draw failed: " + e);
        }
    }
}

/// <summary>
/// 暴食魔女·达芙妮：永远填不满的食欲。
/// 每回合开始时失去 1 点生命，抽 1 张牌。
/// </summary>
public sealed class DaphnePower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "daphne_power.png".PowerImagePath();
    public override string CustomBigIconPath => "daphne_power.png".BigPowerImagePath();

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var owner = Owner;
            if (owner == null || player.Creature != owner)
                return;

            await CreatureCmd.Damage(
                choiceContext, new[] { owner }, 1m, ValueProp.Unblockable, owner);

            var draws = Amount <= 0 ? 1 : Amount;
            for (var i = 0; i < draws; i++)
                await CardPileCmd.Draw(choiceContext, player);

            GD.Print($"[Subaru] daphne: -1 hp, draw {draws}");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] daphne failed: " + e);
        }
    }
}

/// <summary>
/// 嫉妒魔女·莎缇拉：标记用。真正的结算在 DeathReturnSpike.ApplyRewindBlessing 里，
/// 按「15 × 本场死归次数」在快照还原之后对全体敌人造成伤害。
/// 带【腐蚀】，所以跨死归保留。
/// </summary>
public sealed class SatellaPower : CustomPowerModel, ICorrodedPower
{
    /// <summary>
    /// 每次死归的伤害基数（打出时由卡牌写入，默认 15；升级过的卡写 17）。
    /// 【腐蚀】让它不随死归重建，所以这个字段也能活过回档。
    /// </summary>
    public decimal DamagePerRewind { get; set; } = 15m;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "satella_power.png".PowerImagePath();
    public override string CustomBigIconPath => "satella_power.png".BigPowerImagePath();
}
