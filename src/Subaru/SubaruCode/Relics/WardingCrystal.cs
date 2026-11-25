using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 退魔水晶：普通遗物。战斗开始时给予所有敌人 1 层[虚弱]。
///
/// 写法照原版「红面具」（<c>MegaCrit.Sts2.Core.Models.Relics.RedMask</c>）：
/// 钩子用 <see cref="BeforeSideTurnStart"/>，只在「玩家自己参与 + 第 1 回合」时触发一次，
/// 然后对 <c>combatState.HittableEnemies</c> 一次性施加。
/// </summary>
public class WardingCrystal : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Common;

    private const decimal WeakStacks = 1m;

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        var owner = Owner;
        if (owner?.Creature == null || participants == null || !participants.Contains(owner.Creature))
            return;

        // 只在本场第 1 个玩家回合触发
        if ((owner.PlayerCombatState?.TurnNumber ?? 2) > 1)
            return;

        Flash();

        await PowerCmd.Apply<WeakPower>(
            choiceContext,
            combatState.HittableEnemies,
            WeakStacks,
            owner.Creature,
            null,
            false);

        Godot.GD.Print($"[Subaru] warding crystal: weak 1 to {combatState.HittableEnemies.Count} enemies");
    }
}
