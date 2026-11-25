using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 不死王的洗礼：罕见，1 费，技能（单体）。[消耗]。
/// 你的[消耗牌堆]中每有一张[尸兵]，就对目标施加 N 层[中毒]。基础 5，升级 7。
/// 2026-11-19：加上[消耗]。
///
/// <para><b>为什么不带[尸兵]词条</b>：这张牌本身不是尸兵（它是斯芬克斯的「洗礼」，
/// 是制造尸兵的手段而不是尸体）。带上词条会让它自己进入斯芬克斯的随机池、
/// 被「尸兵费用 -1」命中，还会被自己数的牌堆统计进去。所以按
/// <see cref="CalamityForm"/> 的老做法，用 <see cref="IsAllowedOnPath"/> 单独按路线拦。</para>
///
/// <para>计数字段用 <c>CanonicalKeywords</c>（跟 <see cref="CorpseSphinx"/>、
/// <see cref="Powers.CalamityFormPower"/> 同一口径）：尸兵身份是卡牌定义的属性，
/// 不会因为临时加/减词条而变化。</para>
/// </summary>
public sealed class UndyingKingBaptism() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>打出这张牌时播斯芬克斯的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "sphinx";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<PoisonPower>(5m)];

    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(2m); // 5 → 7

    /// <summary>大灾路线专属：别的路线上根本没有尸兵，这张牌就是废牌。</summary>
    public override bool IsAllowedOnPath(Runs.SubaruPath path)
        => path is Runs.SubaruPath.None or Runs.SubaruPath.Calamity;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        var target = cardPlay.Target;
        if (player == null || pcs == null || target == null)
            return;

        var soldiers = pcs.ExhaustPile.Cards.Count(c => c is SubaruCard subaru
            && subaru.CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier));

        if (soldiers <= 0)
        {
            Godot.GD.Print("[Subaru] undying king baptism: no corpse soldier in exhaust pile");
            return;
        }

        var total = DynamicVars.Poison.BaseValue * soldiers;

        await PowerCmd.Apply<PoisonPower>(
            choiceContext, target, total, player.Creature, this, false);

        Godot.GD.Print($"[Subaru] undying king baptism: {soldiers} corpse soldiers "
                       + $"x {DynamicVars.Poison.BaseValue} -> {total} poison on {target.Name}");
    }
}
