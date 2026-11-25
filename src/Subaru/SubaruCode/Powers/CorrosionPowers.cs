using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

// ─────────────────────────────────────────────────────────────
// 腐蚀牌的「持续型」能力：全部带 ICorrodedPower，跨死归保留。
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 宫廷小丑：每回合开始时，从四张[基础魔法]里选一张加入手牌。
/// 升级过的卡写的 <see cref="Upgraded"/> = true，给的是「基础魔法+」。
/// </summary>
public sealed class RoswaalPower : CustomPowerModel, ICorrodedPower
{
    public bool Upgraded { get; set; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "magus_power.png".PowerImagePath();
    public override string CustomBigIconPath => "magus_power.png".BigPowerImagePath();

    /// <summary>层数＝打出过几张宫廷小丑；每层每回合各给一次「四选一」。</summary>
    private int Stacks => Amount <= 0 ? 1 : Amount;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var owner = Owner;
            var state = owner?.CombatState;
            if (owner == null || state == null || player?.Creature != owner)
                return;

            var stacks = Stacks;
            for (var i = 0; i < stacks; i++)
                await GrantOneAsync(choiceContext, player, state);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] roswaal turn start failed: " + e);
        }
    }

    private async Task GrantOneAsync(
        PlayerChoiceContext choiceContext, Player player, MegaCrit.Sts2.Core.Combat.ICombatState state)
    {
        var created = new List<CardModel>();
        foreach (var canonical in new CardModel[]
                 {
                     ModelDb.Card<FireMagic>(),
                     ModelDb.Card<WindMagic>(),
                     ModelDb.Card<EarthMagic>(),
                     ModelDb.Card<WaterMagic>(),
                 })
        {
            if (canonical == null)
                continue;

            var card = state.CreateCard(canonical, player);
            if (card == null)
                continue;

            if (Upgraded && !card.IsUpgraded)
                Extensions.CardUpgradeSync.UpgradeNow(card);

            created.Add(card);
        }

        if (created.Count == 0)
            return;

        // 原版的「二选一」界面（FromChooseACardScreen）内部有硬断言：最多 2 张，
        // 传第 3 张就抛 ArgumentException（0.2.46 的日志：
        // Only works with less than 3 cards）。四张必须走网格选择。
        CardModel? chosen;
        if (created.Count == 1)
        {
            chosen = created[0];
        }
        else
        {
            var prompt = new LocString("static_hover_tips", "SUBARU-BASIC_MAGIC_PROMPT.text");
            var prefs = new CardSelectorPrefs(prompt, 1, 1);
            var picked = await CardSelectCmd.FromSimpleGrid(choiceContext, created, player, prefs);
            chosen = picked?.FirstOrDefault() ?? created[0];
        }

        foreach (var card in created)
        {
            if (ReferenceEquals(card, chosen))
                continue;

            try
            {
                await CardPileCmd.RemoveFromCombat(card, false);
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] roswaal cleanup failed: " + e.Message);
            }
        }

        // CreateCard 造出来的牌不在任何牌堆里，必须自己塞进手牌；
        // 手牌满了就推到抽牌堆顶，绝不能丢（那会让这张牌凭空消失）。
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs != null)
            {
                if (pcs.Hand.Cards.Count >= CardPile.MaxCardsInHand)
                {
                    await CardPileCmd.Add(chosen, pcs.DrawPile, CardPilePosition.Top, null, false);
                    GD.Print($"[Subaru] roswaal: hand full, {chosen.Id} pushed to draw");
                }
                else
                {
                    await CardPileCmd.Add(chosen, pcs.Hand, CardPilePosition.Top, null, false);
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] roswaal add to hand failed: " + e);
        }

        GD.Print($"[Subaru] roswaal: granted {chosen.Id} (upgraded={Upgraded})");
    }
}

/// <summary>
/// 选我吧：每回合开始时抽 Amount 张、随机弃 Amount 张。
/// Amount 由卡牌写入（1；升级过的 2）。
/// 图标＝贝蒂的蝴蝶结（改金色），和贝蒂那套视觉挂钩。
/// </summary>
public sealed class ChooseMePower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "choose_me_power.png".PowerImagePath();
    public override string CustomBigIconPath => "choose_me_power.png".BigPowerImagePath();

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var owner = Owner;
            if (owner == null || player?.Creature != owner)
                return;

            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            var count = Amount;
            if (count <= 0)
                return;

            for (var i = 0; i < count; i++)
                await CardPileCmd.Draw(choiceContext, player);

            var rng = player.RunState?.Rng?.CombatCardGeneration;

            for (var i = 0; i < count; i++)
            {
                var hand = pcs.Hand.Cards.Where(c => c != null).ToList();
                if (hand.Count == 0)
                    break;

                var pick = rng != null ? rng.NextItem(hand) : hand[hand.Count - 1];
                await CardCmd.Discard(choiceContext, pick);
            }

            GD.Print($"[Subaru] choose me: draw/discard {count}");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] choose me turn start failed: " + e);
        }
    }
}

/// <summary>
/// 艾米莉亚的祝福：每回合开始时，按手牌里[魔女瘴气]的张数获得格挡。
/// Amount = 每张瘴气给的格挡（5；升级过的 7）。
/// 和[癫狂]天然互斥：癫狂会把瘴气清空，所以它最强的一刻就在癫狂之前。
/// </summary>
public sealed class EmiliaBlessingPower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "emilia_blessing_power.png".PowerImagePath();
    public override string CustomBigIconPath => "emilia_blessing_power.png".BigPowerImagePath();

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var owner = Owner;
            if (owner == null || player?.Creature != owner)
                return;

            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            var miasma = pcs.Hand.Cards.Count(c => c is CurseWitchMiasma);
            if (miasma <= 0 || Amount <= 0)
            {
                GD.Print($"[Subaru] emilia blessing: no miasma in hand ({miasma})");
                return;
            }

            var block = miasma * Amount;

            await CreatureCmd.GainBlock(owner, block, ValueProp.Move, null, false);
            GD.Print($"[Subaru] emilia blessing: {miasma} miasma x {Amount} = {block} block");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] emilia blessing turn start failed: " + e);
        }
    }
}
