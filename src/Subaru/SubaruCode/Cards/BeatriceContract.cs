using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 契约·贝蒂：先古，能力，1 费，[保留][永恒]，初始牌组 ×1。
/// 召唤贝蒂；移除手牌中 1 张[魔女瘴气]；所有[打击]变化为[迷尼亚]（打击+ → 迷尼亚+）。
/// 「贝蒂」是一个状态而不是召唤物：不占场、不承伤。
/// 以后会和[鲜血帝·文森特]一起改成「只能从开局涅奥会话获取」，暂时仍留在初始牌组。
///
/// 2026-11-18 加强：升级不再减费，改成**生成出来的迷尼亚也是迷尼亚+**
/// （原来升级只是 1 费→0 费，收益太小）。
/// </summary>
public sealed class BeatriceContract() : SubaruCard(1, CardType.Power, CardRarity.Ancient, TargetType.Self)
{
    // [永恒]：卡表要求这张初始牌不能被移出牌组
    // 2026-11-18：只移除[固有]（[保留]留着）—— 改成「第一回合开始后直接进手牌」（EntersHandAtCombatStart）
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Contract, CardKeyword.Retain, CardKeyword.Eternal];

    public override bool EntersHandAtCombatStart => true;

    /// <summary>只从开局的涅奥契约页发放，不进卡池。</summary>
    public override bool IsGenerateOnly => true;

    /// <summary>
    /// 卡面要写「迷尼亚+」——**不能**再用 <c>StringVar("Plus", "")</c> 那种「升级换文本」的写法。
    ///
    /// 第三方 mod（玩家的 CombatSolver）会给每张牌算选择键，枚举到 <c>DynamicVars</c> 时
    /// 走 <c>SemanticStateFieldPolicy.ClassifyString</c>，遇到没登记过的**字符串**动态变量就抛
    /// `InvalidOperationException: 未分类的字符串状态字段：…BeatriceContract.Plus`，
    /// 异常一路冒到 <c>SetupPlayerTurn</c> → 「turn loop died，战斗卡住」。
    /// 触发点只是需要一个选牌界面（赌徒筹码第一回合开弃牌框就会撞上）。
    /// 所以现在改成「文案里直接写清楚升级后是什么」，不再占用字符串动态变量。
    /// </summary>
    // table-upgrade: 生成的迷尼亚为升级版
    // 升级只改行为（变出来的迷尼亚是升级版），没有数值变量要调
    protected override void OnUpgrade()
    {
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var pcs = Owner?.PlayerCombatState;
        var state = CombatState;
        if (pcs == null || state == null)
            return;

        // 出场演出（2026-11-21 用户定）：契约一局只打一次 → 招牌演出，不占每回合角色名额。
        // 返回到命中点，立绘自己播完收场，不阻塞下面的能力结算。
        await Vfx.BeatriceCutIn.PlayOnContractAsync(Owner);

        // 召唤贝蒂（状态）：Amount 记升级标记（1 = 普通 / 2 = 升级），
        // 回合结束自动打出的迷尼亚按它决定是不是迷尼亚+。
        await PowerCmd.Apply<BeatricePower>(
            choiceContext, Owner.Creature, IsUpgraded ? 2m : 1m, Owner.Creature, this, false);

        // 移除手牌中 1 张魔女瘴气 —— 这张牌每场战斗只能移除一次
        var curse = pcs.Hand.Cards.FirstOrDefault(c => c is CurseWitchMiasma);
        if (curse != null && Rewind.CurseRemoval.TryUse(Rewind.CurseRemoval.Contract))
        {
            // 契约是「明确指名移除瘴气」的效果，要开 MiasmaGuard 的允许窗口，
            // 否则会被「瘴气不可被移出战斗」那道防线挡掉。
            using (Patches.MiasmaGuard.Allow())
                await CardPileCmd.RemoveFromCombat(curse, false);
        }

        // 契约的第二条：所有打击变化为迷尼亚（契约升级 → 变出来的也是迷尼亚+）
        await TransformStrikes(choiceContext, state, Owner, IsUpgraded);
    }

    /// <summary>
    /// 把战斗里的所有[打击]换成[迷尼亚]。
    /// 升级过的打击换成升级的迷尼亚；<paramref name="forceUpgraded"/>（契约本身升级过）同样算升级。
    /// </summary>
    internal static async Task TransformStrikes(
        PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.ICombatState state,
        MegaCrit.Sts2.Core.Entities.Players.Player player, bool forceUpgraded = false)
    {
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            var strikes = pcs.AllCards.Where(c => c is StrikeSubaru).ToList();
            var count = 0;

            foreach (var strike in strikes)
            {
                var pile = pcs.AllPiles.FirstOrDefault(p => p.Cards.Contains(strike));
                if (pile == null)
                    continue;

                var minya = state.CreateCard<Minya>(player);
                if (minya == null)
                    continue;

                if (strike.IsUpgraded || forceUpgraded)
                    Extensions.CardUpgradeSync.UpgradeNow(minya);

                // 原地变形：打击直接变成迷尼亚，不进消耗堆、不额外造一张牌。
                // （0.111 的 CardCmd.Transform 会在原位置替换，牌堆顺序与死归快照都不受影响）
                await CardCmd.Transform(strike, minya, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.None);
                count++;
            }

            Godot.GD.Print($"[Subaru] contract: {count}/{strikes.Count} strikes -> minya");
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] contract transform failed: " + e);
        }
    }

    /// <summary>
    /// 死归回档后再跑一遍：存档点里已经有贝蒂（说明契约在存档点之前就打过了），
    /// 那么还原出来的打击要再变成迷尼亚，否则玩家会看到「契约白打了」。
    /// </summary>
    internal static async Task ReapplyTransformIfNeeded(
        PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.ICombatState state,
        MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (!player.Creature.HasPower<BeatricePower>())
            return;

        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return;

        // 存档点里本来就有迷尼亚（迷失是在契约之后打的）→ 已经变过了，别再来一次
        if (pcs.AllCards.Any(c => c is Minya))
            return;

        // 当初打的契约是不是升级版：升级版要变出迷尼亚+。
        // 能力牌重放时拿不到原牌的 IsUpgraded，只能查「打牌记录」。
        await TransformStrikes(choiceContext, state, player, PlayedPowerCards.WasUpgraded<BeatriceContract>());
    }
}
