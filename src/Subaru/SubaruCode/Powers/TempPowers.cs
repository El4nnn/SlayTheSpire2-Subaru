using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 神龙·波尔肯尼卡：本回合内受到的伤害 ×0.7（龙鳞）。**敌人回合结束**时消失。
///
/// 时机说明：这是防御向能力，必须在敌人行动期间仍然生效。
/// 之前写成 <c>side == Player</c>（玩家回合结束就摘），敌人的攻击完全吃不到减伤 ——
/// 和柯尔·雷欧尼斯（浮空）是同一个 bug，2026-11-17 一起修。
/// 这里没有实例字段开关，所以回档天然安全（只靠 Amount）。
/// </summary>
public sealed class VolcanicaPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "volcanica_power.png".PowerImagePath();
    public override string CustomBigIconPath => "volcanica_power.png".BigPowerImagePath();

    public override decimal ModifyDamageMultiplicative(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay cardPlay)
    {
        if (target != Owner || Amount <= 0)
            return 1m;

        return 0.7m;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> creatures)
    {
        var owner = Owner;
        if (side != CombatSide.Enemy || owner == null)
            return;

        await PowerCmd.Remove(this);
    }
}

/// <summary>魔导的加护：每打出一张技能牌，对随机敌人造成 Amount 点伤害（整场战斗常驻，[腐蚀]跨死归保留）。</summary>
public sealed class MagusPower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "magus_power.png".PowerImagePath();
    public override string CustomBigIconPath => "magus_power.png".BigPowerImagePath();

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            var card = cardPlay.Card;
            if (owner == null || card?.Type != CardType.Skill)
                return;

            // 多人里 AfterCardPlayed 是全场的：队友打出技能牌也会广播到我这层。
            // 魔导的加护只认**持有者自己**打出的牌。
            if (!ReferenceEquals(card.Owner, owner.Player))
                return;

            var state = owner.CombatState;
            if (state == null)
                return;

            var enemies = state.HittableEnemies.ToList();
            if (enemies.Count == 0 || Amount <= 0)
                return;

            // 必须用游戏自己的随机流（Seed+Counter 会写进存档），
            // 用 new Random() 的话 SL 会重掷，同一张牌结果就变了。
            var rng = owner.Player?.RunState?.Rng?.CombatTargets;
            var pick = rng != null ? rng.NextItem(enemies) : enemies[0];

            await CreatureCmd.Damage(
                choiceContext, pick, Amount, ValueProp.Move, owner);
            GD.Print($"[Subaru] magus: {Amount} to random enemy");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] magus failed: " + e);
        }
    }
}

/// <summary>
/// 多重吟唱：本回合内打出的下一张「非腐蚀技能牌」触发次数 ×Amount（只用一次）。
/// 腐蚀牌本来一场只能打一次，翻倍没有意义且会打乱腐蚀记账，所以显式排除；
/// 不匹配的牌不消耗这次机会。
/// </summary>
public sealed class MeteorPower : CustomPowerModel
{
    private bool _used;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "meteor_power.png".PowerImagePath();
    public override string CustomBigIconPath => "meteor_power.png".BigPowerImagePath();

    /// <summary>只有技能牌、且不是腐蚀牌，才吃这次翻倍。</summary>
    private static bool IsEligible(CardModel card)
    {
        if (card.Type != CardType.Skill)
            return false;

        return !(card is SubaruCard subaru && subaru.CanonicalKeywords.Contains(SubaruKeywords.Corrosion));
    }

    /// <summary>
    /// 这张牌是不是「我的」：多人里 <c>ModifyCardPlayCount</c> 对全场每张牌都会问一遍，
    /// 不判归属会让队友打一张技能牌就把我的「流星」一次性用掉。
    /// </summary>
    private bool IsMine(CardModel card)
    {
        var me = Owner?.Player;
        if (me == null)
            return true;

        return card?.Owner == null || ReferenceEquals(card.Owner, me);
    }

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
    {
        if (_used || Owner == null || Amount <= 1)
            return playCount;

        if (!IsEligible(card) || !IsMine(card))
            return playCount;

        _used = true;
        GD.Print($"[Subaru] meteor: play count {playCount} -> {playCount * (int)Amount}");
        return playCount * (int)Amount;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> creatures)
    {
        var owner = Owner;
        if (side != CombatSide.Player || owner == null || !creatures.Contains(owner))
            return;

        await PowerCmd.Remove(this);
    }
}
