using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 忧郁魔人·赫克特：罕见，2 费，技能（群体）。
/// 本回合造成的伤害提高 30%，然后对所有敌人造成 6 点伤害。
/// 注意顺序：先挂加成再出伤，所以这 6 点本身也吃加成。
/// 升级：30%→50%，6→8。
/// </summary>
public sealed class Hector() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
{
    /// <summary><c>data/cutin.json</c> 里的忧郁魔人立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "hector";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6m, ValueProp.Move),
        new IntVar("Percent", 30m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2m);
        DynamicVars["Percent"].UpgradeValueBy(20m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        // 1) 先加成本回合的出伤倍率。
        // DamageUpPower 的 Amount 就是「累计百分点」：打几张就加几个 percent，
        // 倍率由能力自己算（1 + Amount/100），不再用实例字段 ——
        // 实例字段在死归回档后会丢，加成会莫名其妙归零。
        var percent = DynamicVars["Percent"].BaseValue;
        await PowerCmd.Apply<DamageUpPower>(
            choiceContext, Owner.Creature, percent, Owner.Creature, this, false);

        var power = Owner.Creature.Powers.FirstOrDefault(p => p is DamageUpPower) as DamageUpPower;
        if (power != null)
            Godot.GD.Print($"[Subaru] hector: damage +{power.Amount}% (x{1m + power.Amount / 100m})");

        // 2) 再打全体伤害（吃上面那个倍率）
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(state)
            .Execute(choiceContext);
    }
}
