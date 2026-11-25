using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 鬼神再临：稀有，2 费，技能（自身），[消耗]。
/// 消耗**任意张**手牌，每消耗 1 张获得 6 点[活力]；然后你在下回合无法打出手牌。
/// 升级：6 → 8（每张的活力）。
///
/// <para>「任意张」用 <c>CardSelectorPrefs(prompt, 0, 手牌数)</c>（min 0 / max 全部），
/// 所以可以一张都不选直接确认。</para>
///
/// <para>「下回合无法打出手牌」由 <see cref="KishinReturnPower"/> 负责：
/// 打出当回合不封锁（Amount = 2），下个回合开始变成 1 才封锁，再下一个回合开始移除。</para>
/// </summary>
public sealed class KishinReturn() : SubaruCard(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    /// <summary>拉姆（鬼神再临）的立绘 cut-in（2026-11-23 接入，与千里眼共用 `ram`）。</summary>
    public override string? CutInId => "ram";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<VigorPower>(6m)];

    protected override void OnUpgrade() => DynamicVars["VigorPower"].UpgradeValueBy(2m); // 6 → 8

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        var perCard = DynamicVars["VigorPower"].BaseValue;

        var selectable = pcs.Hand.Cards.Count(c => c != this);
        var exhausted = 0;

        // 自动打出（机遇门之类走 AutoPlay 的入口）没有真实玩家选择上下文：
        // CardSelectCmd.FromHand 会落到 PlayerChoiceSynchronizer.GetChoiceId 并越界抛异常
        // （2026-11-24 玩家日志实证：ArgumentOutOfRangeException）。这种情况改成
        // 确定性地吃手牌顺序里的第一张，不碰随机流，多人两端也不会分叉。
        if (cardPlay.IsAutoPlay)
        {
            exhausted = await ExhaustFirstByHandOrder(choiceContext, player);
        }
        else if (selectable > 0)
        {
            try
            {
                var selected = await CardSelectCmd.FromHand(
                    choiceContext,
                    player,
                    new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 0, selectable),
                    c => c != this,
                    this);

                foreach (var card in selected ?? [])
                {
                    await CardCmd.Exhaust(choiceContext, card, false, false);
                    exhausted++;
                }
            }
            catch (System.Exception e)
            {
                // 选牌链路偶尔会因为同步层状态（联机 / 其它 mod）不可用而抛。
                // 绝不让它打断整张牌的结算——打断后这张牌会卡在结算区、手牌也锁住。
                Godot.GD.PushWarning(
                    "[Subaru] kishin return: hand selection failed, falling back: "
                    + e.GetType().Name + ": " + e.Message);
                exhausted = await ExhaustFirstByHandOrder(choiceContext, player);
            }
        }

        if (exhausted > 0)
        {
            await PowerCmd.Apply<VigorPower>(
                choiceContext, player.Creature, perCard * exhausted, player.Creature, this, false);
        }

        // 下个回合封锁出牌（当回合不封锁，见能力里的说明）
        await PowerCmd.Apply<KishinReturnPower>(
            choiceContext, player.Creature, 2m, player.Creature, this, false);

        Godot.GD.Print($"[Subaru] kishin return: exhausted {exhausted} => vigor {perCard * exhausted}");
    }

    /// <summary>
    /// 不询问玩家的确定性吃牌：取手牌顺序里的第一张。
    /// 给「自动打出」和「选牌链路抛异常」两条兜底用——两者都不该把这张牌卡在结算区。
    /// </summary>
    private async Task<int> ExhaustFirstByHandOrder(PlayerChoiceContext choiceContext, Player player)
    {
        var hand = player.PlayerCombatState?.Hand?.Cards;
        var victim = hand?.FirstOrDefault(c => c != this);
        if (victim == null)
            return 0;

        await CardCmd.Exhaust(choiceContext, victim, false, false);
        Godot.GD.Print($"[Subaru] kishin return: no-choice fallback, exhausted {victim.Id} (no prompt)");
        return 1;
    }
}
