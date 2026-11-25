using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 幼女使者：每回合开始时，从 [E·M·T] 与 [E·M·M] 中选 1 张加入手牌，然后层数 -1。
/// GrantsUpgraded = true 时生成的是升级版（来自升级过的「幼女使者」卡）。
/// </summary>
public sealed class LolimancerPower : CustomPowerModel
{
    public bool GrantsUpgraded { get; set; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "lolimancer_power.png".PowerImagePath();

    public override string CustomBigIconPath => "lolimancer_power.png".BigPowerImagePath();

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player.Creature != Owner)
                return;

            var state = player.Creature.CombatState;
            var pcs = player.PlayerCombatState;
            if (state == null || pcs == null)
                return;

            var emt = state.CreateCard<Emt>(player);
            var emm = state.CreateCard<Emm>(player);
            if (emt == null || emm == null)
                return;

            if (GrantsUpgraded)
            {
                Extensions.CardUpgradeSync.UpgradeNow(emt);
                Extensions.CardUpgradeSync.UpgradeNow(emm);
            }

            var options = new List<CardModel> { emt, emm };
            var picked = await CardSelectCmd.FromChooseACardScreen(choiceContext, options, player, false);

            if (picked != null)
            {
                await BattleSnapshot.AddCardSafely(pcs.Hand, picked, pcs);
                GD.Print("[Subaru] lolimancer granted: " + picked.Id);
            }

            Amount -= 1;
            if (Amount <= 0)
                await PowerCmd.Remove(this);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] lolimancer turn start failed: " + e);
        }
    }
}
