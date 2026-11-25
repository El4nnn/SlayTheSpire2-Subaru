using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 龙剑：罕见遗物。每回合开始时获得 2 点[活力]。
/// 活力是原版状态（<see cref="VigorPower"/>），不需要自造 Power。
/// </summary>
public class DragonSword : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：钩子会为每个玩家的回合各触发一次，只给自己发活力
        if (!ReferenceEquals(player, Owner))
            return;

        if (player?.Creature == null)
            return;

        await PowerCmd.Apply<VigorPower>(
            choiceContext, player.Creature, 2m, player.Creature, null, false);
    }
}
