using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Events;

/// <summary>
/// 魔女的茶会（事件）：你精神恍惚，来到一片秘境。眼前出现一位魔女，邀请你参加她们的茶会。
///
/// 选项一 接受邀请：获得卡牌「魔女的茶会」（这张牌只从本事件获得，不进奖励池）。
/// 选项二 谁会相信魔女：获得 50 金币，离开。
///
/// 给牌不走奖励界面（奖励界面能「跳过」，跳过就白拿）。
/// 走的是原版 SpecialCardReward.OnSelect 内部那两步：
///   CardPileCmd.Add 入组 + CardCmd.PreviewCardPileAdd 展示动画。
/// 原版 CardPileCmd.AddCurseToDeck 在事件/遗物里也是直接调 PreviewCardPileAdd，
/// 所以非奖励场景下这个展示是能用的。
///
/// 注意：两个选项都必须走到 SetEventFinished，否则事件界面会一直停在那一页（表现为「卡死」）。
/// </summary>
public sealed class WitchTeaPartyEvent() : CustomEventModel
{
    /// <summary>
    /// 用原版事件的布局（卷轴面板 + 左侧插画 + 上方标题）。
    /// 不写这一行，BaseLib 的 CustomEventModel 会走 Custom 布局，
    /// 界面变成「一屏铺满、文字右对齐」，和原版事件不是一个样子。
    /// </summary>
    public override EventLayoutType LayoutType => EventLayoutType.Default;

    /// <summary>
    /// 选过「谁会相信魔女」之后，本局不再出现。
    /// 引擎的 RoomSet.EnsureNextEventIsValid 本来就会跳过 IsAllowed == false 的事件。
    /// </summary>
    public override bool IsAllowed(IRunState runState)
        => SubaruEventFlags.PartyHasSubaru(runState) && !SubaruEventFlags.RefusedTeaParty(runState);

    // 没有 Acts = 共享事件，任何章节都能刷
    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        Option(AcceptInvitation,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.ACCEPT.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.ACCEPT.description")),

        Option(WhoWouldBelieve,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.LEAVE.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.LEAVE.description")),

        Option(ThinkAgain,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.THINK_AGAIN.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.THINK_AGAIN.description")),
    ];

    /// <summary>第三个选项：什么都不拿，直接走。走之前把「已访问」抹掉，之后还能再刷到。</summary>
    private Task ThinkAgain()
    {
        SubaruEventFlags.AllowEventAgain(Owner.RunState, Id);
        SetEventFinished(PageDescription("THINK"));
        return Task.CompletedTask;
    }

    private async Task AcceptInvitation()
    {
        GD.Print("[Subaru] tea party: ACCEPT clicked");
        try
        {
            var card = Owner.RunState.CreateCard<WitchTeaParty>(Owner);
            GD.Print($"[Subaru] tea party: card created {card.Id}");

            // skipVisuals = true：事件界面里没有牌堆节点，等牌堆动画会永远等不完。
            // 展示动画改用下面的 PreviewCardPileAdd，和原版 SpecialCardReward.OnSelect
            // （以及 CardPileCmd.AddCurseToDeck）用的是同一套。
            var added = await CardPileCmd.Add(card, PileType.Deck, CardPilePosition.Top, null, true);
            GD.Print($"[Subaru] tea party: add success={added.success} deck={Owner.Deck.Cards.Count}");

            CardCmd.PreviewCardPileAdd(added, 0.6f, CardPreviewStyle.EventLayout);
            GD.Print("[Subaru] tea party: preview shown");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] tea party: add card failed: " + e);
        }

        SetEventFinished(PageDescription("ACCEPT"));
        GD.Print("[Subaru] tea party: finished(ACCEPT)");
    }

    private async Task WhoWouldBelieve()
    {
        GD.Print("[Subaru] tea party: LEAVE clicked");
        try
        {
            await PlayerCmd.GainGold(50, Owner);
            GD.Print("[Subaru] tea party: gold +50");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] tea party: gold failed: " + e);
        }

        SetEventFinished(PageDescription("LEAVE"));
        GD.Print("[Subaru] tea party: finished(LEAVE)");
    }

    // 事件立绘：魔女在山丘上摆着茶桌，昴正走上去
    public override string CustomInitialPortraitPath => "events/witch_tea_party.png".ImagePath();
}

