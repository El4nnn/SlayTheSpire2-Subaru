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
/// 本回合的临时敏捷。
///
/// 原版有个 TemporaryDexterityPower，但 0.111 里它没进 ModelDb（PowerCmd.Apply 会抛
/// KeyNotFoundException: POWER.TEMPORARY_DEXTERITY_POWER），所以自己实现一份：
/// 施加时给等量敏捷，回合结束把这份敏捷扣回去。
/// </summary>
public sealed class TempDexterityPower : CustomPowerModel
{
    private const string BaseGameIcon = "res://images/powers/dexterity_power.png";

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

        await PowerCmd.Apply<DexterityPower>(choiceContext, owner, -Amount, owner, null, false);
        await PowerCmd.Remove(this);
    }
}
