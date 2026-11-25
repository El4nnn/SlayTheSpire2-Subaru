using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 鬼神再临：下回合无法打出手牌。
///
/// <para><b>Amount 的用法</b>（这样能跨死归，实例字段会被快照丢掉）：
/// 打出时 Amount = 2 → 当回合不封锁（此时是 2）；
/// 下个回合开始 Amount 2 → 1，**只有 Amount == 1 时封锁**，所以只封下个回合；
/// 再下一个回合开始 1 → 0，移除。</para>
///
/// <para>封锁走 <c>AbstractModel.ShouldPlay</c>：返回 false 就等同于「这张牌现在不能打」
/// （游戏里会显示成不可用的灰卡），比去改 <c>CardModel.IsPlayable</c> 干净。</para>
/// </summary>
public sealed class KishinReturnPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "kishin_return_power.png".PowerImagePath();

    public override string CustomBigIconPath => "kishin_return_power.png".BigPowerImagePath();

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        // 只看「该封锁的那一个回合」：Amount == 1 时玩家所有牌都打不出
        if (Amount != 1)
            return true;

        var owner = Owner;
        if (owner?.Player == null || card == null)
            return true;

        // 只封自己的牌（怪物塞进来的牌不受影响）
        return !ReferenceEquals(card.Owner, owner.Player);
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        var owner = Owner;
        if (owner?.Player == null || player != owner.Player)
            return Task.CompletedTask;

        var next = Amount - 1;

        if (next <= 0)
        {
            GD.Print("[Subaru] kishin return: lock expired");
            _ = PowerCmd.Remove(this);
            return Task.CompletedTask;
        }

        Amount = next;
        GD.Print($"[Subaru] kishin return: amount -> {Amount}");
        return Task.CompletedTask;
    }
}
