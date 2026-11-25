using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 迷失：存档点标记 + 每回合多抽 1 张（两个效果已合并成一个状态）。
///
/// 层数 = 存档点所在的回合数；同一场战斗内打出多张「迷失」时只保留最后一张（覆盖层数）。
/// 每回合开始抽 1 张牌，带【腐蚀】= 不随死归重置：牌组会回到存档点那一刻，
/// 但「每回合多抽一张」留在身上。
/// 它必须跨死归保留，否则回档后就看不出存档点在哪一回合了——所以走记事本（RewindLedger）那条路。
/// </summary>
public sealed class LostSavepointPower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => false;

    public override string CustomPackedIconPath => "lost_savepoint_power.png".PowerImagePath();

    public override string CustomBigIconPath => "lost_savepoint_power.png".BigPowerImagePath();

    // 存档点只会有一个，重复打出是覆盖而不是叠加
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player.Creature != Owner)
                return;

            await CardPileCmd.Draw(choiceContext, player);
            GD.Print("[Subaru] lost savepoint: draw 1");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] lost savepoint draw failed: " + e);
        }
    }
}
