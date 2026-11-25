using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 黑水：稀有遗物。每回合开始时，给予所有敌人 3 层[中毒]。
///
/// 是「持续叠加」型收益：毒每回合结算后减 1，但每回合又会补 3 层，
/// 所以回合数越多单体伤害越高（3、6、9、12…），这也是它落在稀有档的原因。
/// </summary>
public class BlackWater : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    private const decimal PoisonPerTurn = 3m;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：钩子对每个玩家的回合开始都会触发，只认自己的回合，
        // 否则队友回合也会按我的遗物上毒（额外白嫖层数）。
        if (!ReferenceEquals(player, Owner))
            return;

        if (player?.Creature == null)
            return;

        var state = player.Creature.CombatState;
        if (state == null)
            return;

        var enemies = state.HittableEnemies;
        if (enemies == null || enemies.Count == 0)
            return;

        await PowerCmd.Apply<PoisonPower>(
            choiceContext,
            enemies,
            PoisonPerTurn,
            player.Creature,
            null,
            false);

        Godot.GD.Print($"[Subaru] black water: poison 3 to {enemies.Count} enemies");
    }
}
