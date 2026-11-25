using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 龙历石：罕见，1 费，技能。
/// 选择 1 张手牌，将其复制加入手牌。[消耗]。升级：费用 1 → 0。
/// [腐蚀]牌不可复制：它们一场战斗只能打出一次，战斗内再造一张等于凭空多一次。
/// 牌组里本来就有多张同名腐蚀牌是正常的（那是构筑），只有「战斗内复制」被挡。
/// （设计稿里的「复制品本回合费用为 0」暂时做不到——引擎没有暴露战斗内临时改费的接口。）
/// </summary>
public sealed class DragonTablet() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    /// <summary>[腐蚀]牌、契约·贝蒂、鲜血帝·文森特都不能当复制源（与「不可复制」那两层防线保持一致）。</summary>
    private static bool CanCopy(CardModel card) =>
        card != null && !Subaru.SubaruCode.Patches.CardCreationPolicy.IsProtected(card);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        var state = CombatState;
        if (player == null || pcs == null || state == null)
            return;

        // 手里没有可复制的牌就别开选择框
        if (!pcs.Hand.Cards.Any(CanCopy))
        {
            Godot.GD.Print("[Subaru] dragon tablet: nothing copyable in hand");
            return;
        }

        var prompt = new LocString("static_hover_tips", "SUBARU-COPY_CARD.text");
        var prefs = new CardSelectorPrefs(prompt, 1);

        var picked = await CardSelectCmd.FromHand(choiceContext, player, prefs, CanCopy, this);
        var target = picked?.FirstOrDefault();
        if (target == null || !CanCopy(target))
            return;

        var canonical = ModelDb.GetById<CardModel>(target.Id);
        if (canonical == null)
        {
            Godot.GD.PushError("[Subaru] dragon tablet: no canonical for " + target.Id);
            return;
        }

        var copy = state.CreateCard(canonical, player);
        if (copy == null)
            return;

        // 复制品要保留强化等级：CreateCard 造出来的是未升级的，
        // 不补这一下就会出现「复制一张升级牌，拿到的是原版」。
        if (target.IsUpgraded && !copy.IsUpgraded)
            Extensions.CardUpgradeSync.UpgradeNow(copy);

        // 复制品本回合费用为 0（和「地狱之刃」同一套接口）
        copy.EnergyCost.SetThisTurnOrUntilPlayed(0, true);

        // 用「生成牌」的规范入口，而不是 CardPileCmd.Add：
        // 战斗内凭空多的牌都走这里（双持、夜魇、各种发现药水），
        // 也就一并受「腐蚀牌不能被复制」那道防线管着。
        await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, player, CardPilePosition.Top);
        Godot.GD.Print($"[Subaru] dragon tablet: copied {target.Id} (upgraded={target.IsUpgraded}, cost 0 this turn)");
    }
}

/// <summary>
/// 虚饰魔女·潘多拉：稀有，0 费，能力（一次性结算），权能 15、腐蚀。
/// 将手牌中的 1 张[魔女瘴气]变换为随机一张稀有牌。
/// 随机走游戏的 CombatCardGeneration 流，SL 后结果一致。
/// 升级：随机一张 → 三选一。
/// </summary>
public sealed class PandoraWitch() : SubaruCard(0, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary><c>data/cutin.json</c> 里的虚饰魔女·潘多拉立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "pandora";

    public override HashSet<CardKeyword> CanonicalKeywords =>
    [
        .. base.CanonicalKeywords,
        SubaruKeywords.Authority,
        SubaruKeywords.Corrosion,
    ];

    protected override int Authority => 15;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Choices", 2m)];

    protected override void OnUpgrade() => DynamicVars["Choices"].UpgradeValueBy(1m); // 2 → 3

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        var state = CombatState;
        if (player == null || pcs == null || state == null)
            return;

        var curse = pcs.Hand.Cards.FirstOrDefault(c => c is CurseWitchMiasma);
        if (curse == null)
        {
            Godot.GD.Print("[Subaru] pandora: no miasma in hand");
            return;
        }

        // 潘多拉是「明确指名把 1 张瘴气换掉」，开允许窗口，否则会被瘴气防线挡掉
        using (Patches.MiasmaGuard.Allow())
            await CardPileCmd.RemoveFromCombat(curse, false);

        var rng = player.RunState?.Rng?.CombatCardGeneration;
        var options = RollRares(rng, (int)DynamicVars["Choices"].BaseValue);
        if (options.Count == 0)
            return;

        var created = new List<CardModel>();

        // 潘多拉是「发现一张新牌」，不是复制已有的牌：
        // 开白名单，允许生成腐蚀牌（否则腐蚀稀有牌会从池子里消失）
        using (Subaru.SubaruCode.Patches.CardCreationPolicy.Allow())
        {
            foreach (var canonical in options)
            {
                var card = state.CreateCard(canonical, player);
                if (card != null)
                    created.Add(card);
            }
        }

        if (created.Count == 0)
            return;

        // 升级后是「三选一」：开出候选，只拿 1 张
        var chosen = created[0];
        if (created.Count > 1)
            chosen = await CardSelectCmd.FromChooseACardScreen(choiceContext, created, player, false) ?? created[0];

        foreach (var card in created)
        {
            if (card == chosen)
                continue;

            try
            {
                await CardPileCmd.RemoveFromCombat(card, false);
            }
            catch (System.Exception e)
            {
                Godot.GD.PushError("[Subaru] pandora: discard option failed: " + e);
            }
        }

        if (pcs.Hand.Cards.Count >= 10)
            await CardPileCmd.Add(chosen, pcs.DrawPile, CardPilePosition.Top, null, false);
        else
            await CardPileCmd.Add(chosen, pcs.Hand, CardPilePosition.Top, null, false);

        // 这张牌是拿一张[魔女瘴气]（一场战斗只能打一次的腐蚀牌）换来的，
        // 不能被死归当临时牌清掉，否则这次机会白亏。
        RewindPersistentCards.Mark(chosen);

        Godot.GD.Print($"[Subaru] pandora: rewrote miasma into {chosen.Id}");
    }

    /// <summary>从本 mod 的稀有牌里随机取 count 张（用游戏随机流，SL 后结果一致）。</summary>
    internal static List<CardModel> RollRares(MegaCrit.Sts2.Core.Random.Rng? rng, int count)
    {
        var pool = new List<CardModel>
        {
            ModelDb.Card<StarEclipse>(),
            ModelDb.Card<ArchbishopPride>(),
            ModelDb.Card<SwordSaintFriend>(),
            ModelDb.Card<SwordDemonFriend>(),
            ModelDb.Card<GreedContract>(),
            ModelDb.Card<Lost>(),
            ModelDb.Card<UlShamak>(),
            ModelDb.Card<AlShamak>(),
            ModelDb.Card<EchidnaWitch>(),
            ModelDb.Card<SekhmetWitch>(),
            ModelDb.Card<Regulus>(),
            ModelDb.Card<SatellaWitch>(),
        };

        // 只留「正常获取的稀有牌」：先古稀有度（傲慢司教已升格）和只能由别的牌生成的
        // （强欲的契约）都不该从随机里冒出来。
        pool.RemoveAll(c => c.Rarity != CardRarity.Rare || (c is SubaruCard sc && sc.IsGenerateOnly));

        var result = new List<CardModel>();
        var used = new HashSet<int>();

        for (var i = 0; i < count && used.Count < pool.Count; i++)
        {
            int idx;
            do
            {
                idx = rng != null ? rng.NextInt(0, pool.Count) : i % pool.Count;
            }
            while (!used.Add(idx));

            result.Add(pool[idx]);
        }

        return result;
    }
}
