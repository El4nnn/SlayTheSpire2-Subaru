using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Potions;

/// <summary>
/// 兵粮丸：普通药水。立刻获得 1 点[能量]，下回合开始再获得 1 点。
///
/// 「下回合的能量」走原版 <see cref="EnergyNextTurnPower"/>（和「食精灵者」同一个做法）：
/// 它自带图标、下回合能量重置后自动补，玩家在场上看得见；
/// 不用 TurnStartQueue —— 那是纯内存队列，界面上没有任何提示。
/// </summary>
public class Hyorogan : SubaruPotion
{
    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.Self;

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        // 立刻 +1 能量
        player.PlayerCombatState?.GainEnergy(1);

        // 下回合开始再 +1（挂状态，图标可见）
        await PowerCmd.Apply<EnergyNextTurnPower>(
            choiceContext, player.Creature, 1m, player.Creature, null, false);

        Godot.GD.Print("[Subaru] hyorogan: +1 energy now, +1 next turn");
    }
}
