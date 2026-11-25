using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 梦剑·正梦：稀有遗物。
/// 每场战斗第一个玩家回合开始时，从手牌里选任意张[消耗]；
/// 每消耗 1 张，对所有敌人造成 10 点伤害；精英战 ×2、首领战 ×3。
/// 「可以不消耗」= 选择界面最小张数为 0，直接确认即可跳过。
///
/// <para><b>为什么不用「本场已用过」的字段</b>：原来靠 <c>_usedThisCombat</c> 记状态
/// （还实现了 <c>IRewindCounterRelic</c> 让回档还原它），但那个字段在快照**之前**就被置成
/// <c>true</c>，于是死归回第一回合后被还原成 <c>true</c>、<c>AfterPlayerTurnStart</c>
/// 再触发时被自己挡掉 —— 玩家看到的正是「回到第一回合，正梦不弹选牌框了」。
/// 现在改成和原版「开局一次」类遗物一样**无状态**：只看
/// <c>PlayerCombatState.TurnNumber == 1</c>。死归会把 TurnNumber 一起还原成 1，
/// 所以补跑时能正常再触发一次。</para>
/// </summary>
public class DreamSword : SubaruRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    private const int DamagePerCard = 10;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：只在「我的」第一个回合触发（否则队友的首回合也会让我弃牌换伤害）
        if (!ReferenceEquals(player, Owner))
            return;

        var pcs = player?.PlayerCombatState;
        var state = player?.Creature?.CombatState;
        if (pcs == null || state == null || player.Creature == null)
            return;

        // 「第一个玩家回合」判断：TurnNumber 是每回合自增的，第 2 回合起就不再触发。
        // 不设任何字段，所以死归还原成第 1 回合时自然会再来一次。
        if (pcs.TurnNumber > 1)
            return;

        var hand = pcs.Hand.Cards.ToList();
        if (hand.Count == 0)
            return;

        var multiplier = RoomMultiplier(player);
        var perCard = DamagePerCard * multiplier;

        try
        {
            var prompt = new LocString("static_hover_tips", "SUBARU-DREAM_PROMPT.text");
            var prefs = new CardSelectorPrefs(prompt, 0, hand.Count);

            var picked = await CardSelectCmd.FromHand(choiceContext, player, prefs, null, this);
            var cards = picked?.ToList() ?? new List<CardModel>();

            foreach (var card in cards)
            {
                await CardCmd.Exhaust(choiceContext, card, false, false);

                foreach (var enemy in state.HittableEnemies.ToList())
                {
                    await CreatureCmd.Damage(
                        choiceContext, enemy, perCard,
                        ValueProp.Move | ValueProp.Unpowered, player.Creature);
                }
            }

            Godot.GD.Print(
                $"[Subaru] dream sword: exhausted {cards.Count}, {perCard} dmg each, x{multiplier}");
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] dream sword failed: " + e);
        }
    }

    /// <summary>精英战 ×2，首领战 ×3，普通战斗 ×1。</summary>
    private static int RoomMultiplier(Player player)
    {
        var roomType = player.RunState?.CurrentRoom?.RoomType;
        return roomType switch
        {
            RoomType.Elite => 2,
            RoomType.Boss => 3,
            _ => 1,
        };
    }
}
