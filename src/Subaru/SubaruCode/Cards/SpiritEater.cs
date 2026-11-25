using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 食精灵者：罕见，1 费，技能。下回合获得 {Energy} 点能量；本回合内所有敌人失去 {Strength} 点力量。
/// 升级：能量 1→2，力量 3→4。
/// </summary>
public sealed class SpiritEater() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
{
    /// <summary><c>data/cutin.json</c> 里的食精灵者·亚拉基亚立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "arakiya";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar(1),
        new PowerVar<StrengthPower>(3m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Energy.UpgradeValueBy(1m);
        DynamicVars.Strength.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 下回合的能量：用原版的 EnergyNextTurnPower（自带图标，下回合能量重置后自动补），
        // 不用 TurnStartQueue —— 那个是纯内存队列，玩家在场上完全看不见。
        var energy = DynamicVars.Energy.BaseValue;
        if (energy > 0)
            await PowerCmd.Apply<EnergyNextTurnPower>(
                choiceContext, Owner.Creature, energy, Owner.Creature, this, false);

        // 本回合的力量削减：先扣力量，再挂「敌人回合结束时补回去」的状态
        var state = CombatState;
        if (state == null)
            return;

        // 打出食精灵者 → 本回合手牌里的[石块·姆斯佩尔]费用归 0。
        // 走原生 CardEnergyCost.SetThisTurn：引擎自己的 EndOfTurnCleanup 会在回合结束时清掉，
        // 不用额外挂能力、也不用自己管生命周期。
        var hand = Owner.PlayerCombatState?.Hand;
        if (hand != null)
        {
            foreach (var card in hand.Cards.Where(c => c is Muspel))
            {
                card.EnergyCost.SetThisTurn(0, true);
                Godot.GD.Print("[Subaru] spirit eater: muspel cost -> 0 this turn");
            }
        }

        var down = DynamicVars.Strength.BaseValue;
        foreach (var enemy in state.HittableEnemies)
        {
            await PowerCmd.Apply<StrengthPower>(
                choiceContext, enemy, -down, Owner.Creature, this, false);
            await PowerCmd.Apply<TempEnemyStrengthDownPower>(
                choiceContext, enemy, down, Owner.Creature, this, false);
        }
    }
}
