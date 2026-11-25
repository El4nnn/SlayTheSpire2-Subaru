using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 基尔提之鞭：普通，群体 5 伤害 + 本回合 2 敏捷。升级：伤害 5→7，敏捷 2→3。
/// （2026-11-17 平衡：临时敏捷 1/2 → 2/3）
/// </summary>
public sealed class Guiltywhip() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(5m, ValueProp.Move),
        new PowerVar<DexterityPower>(2m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2m);
        DynamicVars.Dexterity.UpgradeValueBy(1m);   // 敏捷 2 → 3
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(CombatState)
            .WithHitFx(VfxCmd.slashPath)
            .Execute(choiceContext);

        // 本回合的敏捷：先加敏捷，再挂一份「回合结束扣回去」的临时状态
        await PowerCmd.Apply<DexterityPower>(
            choiceContext, Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this, false);
        await PowerCmd.Apply<TempDexterityPower>(
            choiceContext, Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>硬撑：普通，12 格挡，下回合开始时获得 1 层脆弱。升级：12→18。</summary>
public sealed class Endure() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 延迟到下个玩家回合开始再给
        Rewind.TurnStartQueue.AddFrail(1);
    }
}

/// <summary>
/// 最优骑士·尤利乌斯：罕见，1 费，攻击（单体）。无视[格挡] 8 点伤害，**吃力量**。升级：8→12。
///
/// <para><b>为什么 props 是 <c>Move | Unblockable</c></b>：力量走
/// <c>StrengthPower.ModifyDamageAdditive</c> → <c>IsPoweredAttack</c>，
/// 判据为 <c>HasFlag(Move=8) &amp;&amp; !HasFlag(Unpowered=4)</c>。
/// 2026-11-18 之前这里只写了 <c>Unblockable=2</c>，等于「固定伤害」——力量完全不生效，
/// 和卡面写的攻击牌定位不符，所以改成两个标志都带（和八臂·库尔刚一致）。</para>
///
/// <para>执行时仍要显式给 <c>cmd.DamageProps</c>：只标在 <c>DamageVar</c> 上不会自动带过去。</para>
/// </summary>
public sealed class BestFriend() : SubaruCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>尤利乌斯（最优骑士）的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "julius";

    /// <summary>无视格挡 + 吃力量（详见类注释）。</summary>
    private const ValueProp Props = ValueProp.Move | ValueProp.Unblockable;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(8m, Props)];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var cmd = DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target);

        cmd.DamageProps = Props;

        await cmd.Execute(choiceContext);
    }
}
