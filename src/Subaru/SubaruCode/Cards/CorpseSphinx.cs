using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 尸兵·斯芬克斯：罕见，2 费，技能（自身）。手牌中所有[尸兵]本回合内费用 -1。
/// 升级：自身费用 2 → 1（费用升级走引擎的 <c>CardEnergyCost.UpgradeBy</c>，不是数值升级）。
///
/// 只减「本回合」（<c>AddThisTurn</c>）：回合结束会由引擎自己清理，
/// 不需要我们记账，也不会跟着死归快照。
/// </summary>
public sealed class CorpseSphinx() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>打出这张牌时播斯芬克斯的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "sphinx";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var pcs = Owner?.PlayerCombatState;
        if (pcs == null)
            return Task.CompletedTask;

        var targets = pcs.Hand.Cards
            .Where(c => c.CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier))
            .ToList();

        foreach (var card in targets)
        {
            try
            {
                card.EnergyCost?.AddThisTurn(-1);
            }
            catch (System.Exception e)
            {
                Godot.GD.PushWarning("[Subaru] corpse sphinx cost down failed: " + e.Message);
            }
        }

        Godot.GD.Print($"[Subaru] corpse sphinx: {targets.Count} corpse soldier(s) cost -1 this turn");
        return Task.CompletedTask;
    }
}
