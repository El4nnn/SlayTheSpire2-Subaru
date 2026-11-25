using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Events;

/// <summary>
/// 爱丽丝与荆棘之王（事件）：夜里山丘上站着荆棘之王，胸口的无形荆棘缠上你的心脏。
///
/// 结构（2026-11-19 第二版，按用户要求把「选项的话」拆成两步）：
///   首页三选一，按钮上直接写清代价/收益；
///   选完进中间页——正文是该选项的台词，按钮只剩「获得……」，真正结算奖励；
///   结算完再进结果页。
///
///   ① 数星星，失去 3 点最大生命 → 「比起眺望黑暗……」→ 获得吾星 → 结果页
///   ② 握住他伸出的手，获得荆棘王冠与诅咒【受伤】 → 「他向你伸出手……」→ 获得荆棘王冠与【受伤】 → 结果页
///   ③ 转身离开 → 结果页（没有奖励，不拆）
///
/// 注意：每条路径都必须走到 SetEventFinished，否则界面停在那一页，表现为卡死。
/// </summary>
public sealed class AliceAndThornKingEvent() : CustomEventModel
{
    /// <summary>选项①的最大生命代价。</summary>
    private const int MaxHpLoss = 3;

    public override EventLayoutType LayoutType => EventLayoutType.Default;

    /// <summary>队里没有昴就不出现（2026-11-22 用户定：mod 事件不能污染别人的局）。</summary>
    public override bool IsAllowed(IRunState runState) => SubaruEventFlags.PartyHasSubaru(runState);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        Option(CountStars,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.COUNT_STARS.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.COUNT_STARS.description")),

        Option(TakeHisHand,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.TAKE_HAND.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.TAKE_HAND.description")),

        Option(WalkAway,
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.WALK_AWAY.title"),
            L10NLookup($"{Id.Entry}.pages.INITIAL.options.WALK_AWAY.description")),
    ];

    /// <summary>① 第一页：进台词页，真正的结算在 <see cref="TakeMyStar"/>。</summary>
    private Task CountStars()
    {
        GD.Print("[Subaru] alice event: COUNT_STARS");

        SetEventState(PageDescription("STARS"),
        [
            Option(TakeMyStar,
                L10NLookup($"{Id.Entry}.pages.STARS.options.TAKE.title"),
                L10NLookup($"{Id.Entry}.pages.STARS.options.TAKE.description")),
        ]);

        return Task.CompletedTask;
    }

    /// <summary>① 第二页：发【吾星】，扣 3 点最大生命。</summary>
    private async Task TakeMyStar()
    {
        GD.Print("[Subaru] alice event: TAKE_MY_STAR");

        try
        {
            await GrantRelic<MyStar>();

            await CreatureCmd.LoseMaxHp(
                new BlockingPlayerChoiceContext(), Owner.Creature, MaxHpLoss, null);

            GD.Print($"[Subaru] alice event: max hp -{MaxHpLoss} => {Owner.Creature.MaxHp}");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] alice event: count stars failed: " + e);
        }

        SetEventFinished(PageDescription("STARS_DONE"));
    }

    /// <summary>② 第一页：进台词页，真正的结算在 <see cref="TakeHisCrown"/>。</summary>
    private Task TakeHisHand()
    {
        GD.Print("[Subaru] alice event: TAKE_HAND");

        SetEventState(PageDescription("HAND"),
        [
            Option(TakeHisCrown,
                L10NLookup($"{Id.Entry}.pages.HAND.options.TAKE.title"),
                L10NLookup($"{Id.Entry}.pages.HAND.options.TAKE.description")),
        ]);

        return Task.CompletedTask;
    }

    /// <summary>② 第二页：发【荆棘王冠】与原版诅咒【受伤】。</summary>
    private async Task TakeHisCrown()
    {
        GD.Print("[Subaru] alice event: TAKE_HIS_CROWN");

        try
        {
            await GrantRelic<ThornCrown>();

            // 原版诅咒「受伤」：只进牌组，不进奖励池（这里也不该走奖励界面，能跳过就白拿）
            var card = Owner.RunState.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Injury>(Owner);
            var added = await CardPileCmd.Add(card, PileType.Deck, CardPilePosition.Top, null, true);
            GD.Print($"[Subaru] alice event: injury added={added.success} deck={Owner.Deck.Cards.Count}");

            CardCmd.PreviewCardPileAdd(added, 0.6f, CardPreviewStyle.EventLayout);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] alice event: take hand failed: " + e);
        }

        SetEventFinished(PageDescription("HAND_DONE"));
    }

    /// <summary>③ 转身离开：什么也不发生。</summary>
    private Task WalkAway()
    {
        GD.Print("[Subaru] alice event: WALK_AWAY");
        SetEventFinished(PageDescription("WALK_AWAY"));
        return Task.CompletedTask;
    }

    /// <summary>发一件事件专属遗物（RelicRarity.Event，不进随机池）。</summary>
    private async Task GrantRelic<T>() where T : RelicModel
    {
        var relic = ModelDb.Relic<T>().ToMutable();
        await RelicCmd.Obtain(relic, Owner, -1);

        // 遗物栏节点若「先设 Model 再进树」，NRelic.Reload 会被 IsNodeReady 挡掉 →
        // 有框没图。等节点进树后补一次（贝蒂的守护就是这么空白的）。
        RelicIconRefresh.Schedule(relic);

        GD.Print($"[Subaru] alice event: granted relic {relic.Id}");
    }

    // 事件立绘：夜色山丘上的荆棘之王与白裙女孩
    public override string CustomInitialPortraitPath => "events/alice_and_thorn_king.png".ImagePath();
}
