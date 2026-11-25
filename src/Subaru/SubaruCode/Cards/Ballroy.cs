using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 魔弹射手·巴尔罗伊：稀有，2 费，攻击（单体）。[九神将]。
/// 对目标造成 10 伤害；下回合开始对其造成 15，再下回合开始造成 20。
/// 升级：10/15/20 → 13/19/27。
///
/// 延迟的两段伤害挂在玩家身上的隐藏能力 <see cref="Powers.BallroyShotPower"/>，
/// 每个玩家回合开始结算一段；目标死了就直接清掉。
/// </summary>
public sealed class Ballroy() : SubaruCard(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>立绘 cut-in（2026-11-22 接）：魔弹射手·巴尔罗伊，与尸兵版共用同一张。</summary>
    public override string? CutInId => "ballroy";

    /// <summary>有专属演出：停掉默认攻击动作与通用弹幕（2026-11-22 用户报「默认动画没拦截」）。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>命中音由光柱音效代替，关掉通用每段命中音（顿帧/抖动保留）。</summary>
    public override bool HasCustomHitSfx => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(10m, ValueProp.Move),
        new DamageVar("Echo1", 15m, ValueProp.Move),
        new DamageVar("Echo2", 20m, ValueProp.Move),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);            // 10 → 13
        DynamicVars["Echo1"].UpgradeValueBy(4m);          // 15 → 19
        DynamicVars["Echo2"].UpgradeValueBy(7m);          // 20 → 27
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;

        // 天降光柱：第一段（10）用一档。若是「终结必杀」，这一击的攻击动画直接换成终结动画
        // （蓄力→白屏→黑柱砸下来，返回时正好是命中帧），不再先播光柱、死了再补一段
        if (Vfx.BallroyBeam.WillBeFinisherKill(Owner, target, DynamicVars.Damage.BaseValue, ValueProp.Move,
                                               this, cardPlay))
        {
            Godot.GD.Print("[Subaru] ballroy: finisher kill -> attack anim becomes 终结动画");
            await Vfx.BallroyBeam.PlayFinisherAttackAsync(target);
        }
        else
        {
            await Vfx.BallroyBeam.PlayAsync(target, 1);
        }

        Vfx.BallroyBeam.NoteDamage();

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);

        await PowerCmd.Apply<Powers.BallroyShotPower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);

        var power = Owner.Creature.Powers.OfType<Powers.BallroyShotPower>().FirstOrDefault();
        if (power != null)
            power.AddShot(target, DynamicVars["Echo1"].BaseValue, DynamicVars["Echo2"].BaseValue);

        Godot.GD.Print($"[Subaru] ballroy: {target.Name} hit 10, echo"
                       + $" {DynamicVars["Echo1"].BaseValue}/{DynamicVars["Echo2"].BaseValue} queued");
    }
}
