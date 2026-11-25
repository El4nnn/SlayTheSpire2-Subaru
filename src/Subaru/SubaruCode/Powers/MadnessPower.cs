using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 癫狂：3 回合，每回合能量 -1、每回合最多 3 张牌、死归失效。
/// 额外受伤 50% 不再由本能力提供：改为获得癫狂时每层附带 1 层「易伤」，
/// 由引擎原生易伤结算，意图上的受伤数字才会同步。
/// </summary>
public sealed class MadnessPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => false;

    public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
    {
        // 多人：这条钩子对全场每张牌都会问一遍，只该限制「癫狂的承受者」自己的出牌，
        // 否则队友的连击/多段牌会被我的减益一起砍到 3 张。
        var me = Owner?.Player;
        if (me != null && card?.Owner != null && !ReferenceEquals(card.Owner, me))
            return playCount;

        return System.Math.Min(playCount, 3);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature != Owner)
            return;

        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs != null && pcs.Energy > 0)
                pcs.Energy = System.Math.Max(0, pcs.Energy - 1);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] madness energy failed: " + e);
        }

        await Task.CompletedTask;
    }

    public override string CustomPackedIconPath => "madness_power.png".PowerImagePath();

    public override string CustomBigIconPath => "madness_power.png".BigPowerImagePath();

    // 关键：只允许一个实例。默认的 Instanced 会为每次 Apply 新建一份，
    // 而伤害修正按「所有实例的倍率求和」，多份就会变成 12×1.5 这种爆炸。
    public override PowerInstanceType InstanceType => PowerInstanceType.None;
}
