using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 龙珠：稀有遗物。从第 3 回合开始，每回合额外获得 1 点[能量]。
///
/// 走的是原版 +1 能量遗物同一条路（<c>ModifyMaxEnergy</c>，如「添水」「灵质」），
/// 所以能量球显示会直接变成 4/4，而不是「3/3 再 +1」。
/// <c>PlayerCombatState.MaxEnergy</c> 是每次读取都重算的计算属性，
/// 回合号一到 3 就会自然生效，不需要额外挂状态。
/// </summary>
public class DragonOrb : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    /// <summary>从第几回合开始生效。</summary>
    private const int FromTurn = 3;

    private const decimal Bonus = 1m;

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        if (player != Owner)
            return amount;

        var turn = player.PlayerCombatState?.TurnNumber ?? 0;
        return turn >= FromTurn ? amount + Bonus : amount;
    }
}
