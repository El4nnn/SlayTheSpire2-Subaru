using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>怠惰因子：每回合开始时把 1 张[真·不可视之神意]加入手牌。</summary>
public sealed class SlothFactorPower : CustomPowerModel
{
    /// <summary>GrantsUpgraded = true 时生成的是升级版（来自升级过的「怠惰因子」）。</summary>
    public bool GrantsUpgraded { get; set; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "sloth_factor_power.png".PowerImagePath();

    public override string CustomBigIconPath => "sloth_factor_power.png".BigPowerImagePath();

    /// <summary>层数＝打出过几张怠惰因子；每层每回合生成一张。</summary>
    private int Stacks => Amount <= 0 ? 1 : Amount;

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

            var stacks = Stacks;
            for (var i = 0; i < stacks; i++)
            {
                var card = state.CreateCard<TrueInvisibleProvidence>(player);
                if (card == null)
                    continue;

                if (GrantsUpgraded)
                    Extensions.CardUpgradeSync.UpgradeNow(card);

                await BattleSnapshot.AddCardSafely(pcs.Hand, card, pcs);
            }

            GD.Print($"[Subaru] sloth factor: granted {stacks} true invisible providence");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] sloth factor failed: " + e);
        }
    }
}

/// <summary>
/// 强欲的契约：每回合开始 +1 能量并抽 1 张牌；
/// 每回合结束可让最多 2 张手牌获得[保留]，并获得 10 层[魔女遗香]。
/// </summary>
public sealed class GreedContractPower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "greed_contract_power.png".PowerImagePath();

    public override string CustomBigIconPath => "greed_contract_power.png".BigPowerImagePath();

    /// <summary>
    /// 这份能力的层数＝「打出了几张强欲的契约」。效果全部按层数放大：
    /// 之前是写死的 +1 能量 / 抽 1 / +10 遗香，于是用龙历石复制一张、
    /// 场上显示 2 层，实际收益还是 1 层——玩家会以为「叠了没用」。
    /// </summary>
    private int Stacks => Amount <= 0 ? 1 : Amount;

    /// <summary>
    /// 能量走「最大能量」这条路（和龙珠、添水、灵质同一个钩子）：
    /// 回合开始重置能量时直接是 4/4，而不是 3/3 再额外 +1。
    /// </summary>
    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        var owner = Owner;
        if (owner == null || player == null || player.Creature != owner)
            return amount;

        return amount + Stacks;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player.Creature != Owner)
                return;

            var stacks = Stacks;

            // 能量已经由 ModifyMaxEnergy 提高上限，这里不再 GainEnergy，只负责抽牌
            for (var i = 0; i < stacks; i++)
                await CardPileCmd.Draw(choiceContext, player);

            GD.Print($"[Subaru] greed contract: max energy +{stacks}, draw {stacks}");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] greed contract turn start failed: " + e);
        }
    }

    // 必须在**弃牌之前**跑：原版回合结束的顺序是
    // BeforeSideTurnEnd（这里）→ 弃掉没[保留]的手牌 → AfterSideTurnEnd。
    // 之前挂在 AfterSideTurnEnd，结果是先把牌扔了再让你选保留，选中的牌早就进弃牌堆了。
    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> creatures)
    {
        try
        {
            var owner = Owner;
            if (side != CombatSide.Player || owner == null || !creatures.Contains(owner))
                return;

            var player = owner.Player;
            var pcs = player?.PlayerCombatState;
            if (player == null || pcs == null)
                return;

            // 「立即死归」会强制结束回合，然后整回合回档 —— 这一刻弹「选最多 2 张保留」
            // 和给遗香都会被抹掉，纯属白问（2026-11-23 用户报）。
            // 判据用 DeathReturnSpike 的强制结束回合标志，只覆盖那一次，正常结束回合不受影响。
            if (DeathReturnSpike.IsForcedEndTurn)
            {
                GD.Print("[Subaru] greed contract: forced end turn (rewind) -> skip retain selection");
                return;
            }

            var prompt = new LocString("static_hover_tips", "SUBARU-GREED_PROMPT.text");
            var keepMax = 2 * Stacks;
            var prefs = new CardSelectorPrefs(prompt, 0, keepMax);

            // 只排掉[魔女瘴气]：它本来就不参与弃牌，留着没有意义；
            // 其它诅咒和状态仍然可以选（要不要留是玩家的选择）
            var picked = await CardSelectCmd.FromHand(
                choiceContext, player, prefs, c => c is not CurseWitchMiasma, this);

            var count = 0;
            foreach (var card in picked ?? Enumerable.Empty<CardModel>())
            {
                // 用原版的「本回合保留」标记（ShouldRetainThisTurn 就是靠它），
                // 不能用 ApplyKeyword(Retain)——那会把[保留]永久写到牌上，之后每回合都留住。
                CardCmd.ApplySingleTurnRetain(card);
                count++;
            }

            var fragrance = 10m * Stacks;
            await PowerCmd.Apply<WitchFragrancePower>(choiceContext, owner, fragrance, owner, null, false);
            GD.Print($"[Subaru] greed contract: {count} cards retained, fragrance +{fragrance}");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] greed contract turn end failed: " + e);
        }
    }
}
