using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 尸兵·巴尔罗伊：普通，2 费，攻击（单体）。造成 10 伤害，然后获得等同于实际伤害数的[活力]。
/// 升级：10 → 14。
///
/// 「实际伤害数」取 <c>DamageResult.TotalDamage</c>（含被格挡的部分），
/// 和大罪司教·卡佩拉那张「获得等同实际伤害的格挡」同一套口径。
/// 活力在**这一下之后**才加上，所以本次伤害自己吃不到加成。
/// </summary>
public sealed class CorpseBallroy() : SubaruCard(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    /// <summary>立绘 cut-in（2026-11-22 接）：尸兵版与本体共用 `ballroy` 这张图。</summary>
    public override string? CutInId => "ballroy";

    /// <summary>有专属演出：停掉默认攻击动作与通用弹幕（2026-11-22 用户报「默认动画没拦截」）。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>命中音由光柱音效代替，关掉通用每段命中音（顿帧/抖动保留）。</summary>
    public override bool HasCustomHitSfx => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(10m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m); // 10 → 14

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var player = Owner;
        if (player == null)
            return;

        // 尸兵版只有一段伤害 → 固定用二档；终结必杀则把这一击换成终结动画（返回时正好是命中帧）
        if (Vfx.BallroyBeam.WillBeFinisherKill(player, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move,
                                               this, cardPlay))
        {
            Godot.GD.Print("[Subaru] corpse ballroy: finisher kill -> attack anim becomes 终结动画");
            await Vfx.BallroyBeam.PlayFinisherAttackAsync(cardPlay.Target);
        }
        else
        {
            await Vfx.BallroyBeam.PlayAsync(cardPlay.Target, 2);
        }

        Vfx.BallroyBeam.NoteDamage();

        var cmd = await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        var dealt = cmd.Results
            .SelectMany(list => list)
            .Sum(r => (decimal)r.TotalDamage);

        if (dealt <= 0m)
            return;

        await PowerCmd.Apply<VigorPower>(
            choiceContext, player.Creature, dealt, player.Creature, this, false);

        Godot.GD.Print($"[Subaru] corpse ballroy: dealt {dealt} => vigor {dealt}");
    }
}
