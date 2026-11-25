using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 魔瘴石：普通遗物。回合结束时吸收 3 层[魔女遗香]（从自己身上扣掉，最多扣到 0）。
///
/// 魔女遗香是「每层使你受到的伤害 +1%」的负担（强欲的契约每回合塞 10 层），
/// 所以这是个纯减负遗物：把负担吃掉一点，不产出任何收益。
/// </summary>
public class MiasmaStone : SubaruRelic
{
    private const int AbsorbPerTurn = 3;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player)
            return Task.CompletedTask;

        var owner = Owner?.Creature;
        if (owner == null || participants == null || !participants.Contains(owner))
            return Task.CompletedTask;

        var fragrance = owner.Powers.FirstOrDefault(p => p is WitchFragrancePower);
        if (fragrance == null)
            return Task.CompletedTask;

        var absorbed = System.Math.Min(AbsorbPerTurn, fragrance.Amount);
        if (absorbed <= 0)
            return Task.CompletedTask;

        var left = fragrance.Amount - absorbed;
        fragrance.Amount = left;
        if (left <= 0)
            fragrance.RemoveInternal();

        Godot.GD.Print($"[Subaru] miasma stone: absorbed {absorbed} fragrance (left {left})");
        return Task.CompletedTask;
    }
}
