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
/// 魔女的契约：茶会选「接受邀请」之后必出的后续事件。
///
/// 它自己不会随机出现（IsAllowed 恒为 false），只由
/// <see cref="Patches.WitchContractChainPatch"/> 在抽事件那一步塞进来；
/// 且本局只能出一次（已访问过就不再塞）。
///
/// 奖励是【强欲的契约】——那张牌本来就是 GenerateOnly，
/// 所以这个事件就是它唯一的来源。
///
/// 结构：第一页 签 / 问代价 / 收回手；「问代价」进第二页，第二页还能再签。
/// 这样「先问清楚再决定」不会白问，符合原文里她主动报备代价的态度。
/// </summary>
public sealed class WitchContractEvent() : CustomEventModel
{
    public override EventLayoutType LayoutType => EventLayoutType.Default;

    // 只走强制，不进随机池
    public override bool IsAllowed(IRunState runState) => false;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        Option(SignContract,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.SIGN.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.SIGN.description")),

        Option(AskPrice,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.ASK.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.ASK.description")),

        Option(WithdrawHand,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.REFUSE.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.REFUSE.description")),
    ];

    private Task AskPrice()
    {
        SetEventState(PageDescription("ASK"),
        [
            Option(SignAfterAsking,
                L10NLookup($"{Id.Entry}.pages.ASK.options.SIGN.title"),
                L10NLookup($"{Id.Entry}.pages.ASK.options.SIGN.description")),

            Option(WithdrawAfterAsking,
                L10NLookup($"{Id.Entry}.pages.ASK.options.BACK.title"),
                L10NLookup($"{Id.Entry}.pages.ASK.options.BACK.description")),
        ]);
        return Task.CompletedTask;
    }

    private async Task SignContract()
    {
        await GrantContract();
        SetEventFinished(PageDescription("SIGNED"));
    }

    private async Task SignAfterAsking()
    {
        await GrantContract();
        SetEventFinished(PageDescription("SIGNED"));
    }

    private Task WithdrawHand()
    {
        SetEventFinished(PageDescription("REFUSED"));
        return Task.CompletedTask;
    }

    private Task WithdrawAfterAsking()
    {
        SetEventFinished(PageDescription("REFUSED_AFTER_ASKING"));
        return Task.CompletedTask;
    }

    /// <summary>发【强欲的契约】：直接进牌组，并弹一次获得牌的展示（和茶会事件同一套）。</summary>
    private async Task GrantContract()
    {
        try
        {
            var card = Owner.RunState.CreateCard<GreedContract>(Owner);
            GD.Print($"[Subaru] witch contract: card created {card.Id}");

            var added = await CardPileCmd.Add(card, PileType.Deck, CardPilePosition.Top, null, true);
            GD.Print($"[Subaru] witch contract: add success={added.success} deck={Owner.Deck.Cards.Count}");

            CardCmd.PreviewCardPileAdd(added, 0.6f, CardPreviewStyle.EventLayout);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] witch contract: grant failed: " + e);
        }
    }

    // 立绘：同一个秘境，艾姬多娜伸手
    public override string CustomInitialPortraitPath => "events/witch_contract.png".ImagePath();
}
