using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 本回合的临时力量（宣讲用）。
/// 和 TempDexterityPower 同理：原版的 TemporaryStrengthPower 在 0.111 里也不保证能查到，
/// 所以自己实现——施加时给力量，回合结束扣回去。
/// </summary>
public sealed class TempStrengthPower : CustomPowerModel
{
    private const string BaseGameIcon = "res://images/powers/strength_power.png";

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath =>
        Godot.ResourceLoader.Exists(BaseGameIcon) ? BaseGameIcon : "damage_up_power.png".PowerImagePath();

    public override string CustomBigIconPath =>
        Godot.ResourceLoader.Exists(BaseGameIcon) ? BaseGameIcon : "damage_up_power.png".BigPowerImagePath();

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        if (side != CombatSide.Player)
            return;

        var owner = Owner;
        if (owner == null)
            return;

        await PowerCmd.Apply<StrengthPower>(choiceContext, owner, -Amount, owner, null, false);
        await PowerCmd.Remove(this);
    }
}
