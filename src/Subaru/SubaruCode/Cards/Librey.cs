using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 毒蛇·历布莱：普通，1 费，技能（单体）。施加 6 层[中毒]，然后让该敌人的中毒**立即结算一次**。
/// 升级：6 → 10。
///
/// 「立即结算一次」直接调原版的 <c>PoisonPower.Trigger()</c> —— 和对方回合开始时那一次完全等价
/// （掉血 = 当前层数，层数再 -1），所以不需要我们自己算伤害或改层数。
/// </summary>
public sealed class Librey() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    public override string? CutInId => "librey";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<PoisonPower>(6m)];

    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(4m); // 6 → 10

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target;
        var player = Owner;
        if (target == null || player == null)
            return;

        await PowerCmd.Apply<PoisonPower>(
            choiceContext, target, DynamicVars.Poison.BaseValue, player.Creature, this, false);

        var poison = target.Powers.OfType<PoisonPower>().FirstOrDefault();
        if (poison != null)
            await poison.Trigger();

        Godot.GD.Print($"[Subaru] librey: poison {DynamicVars.Poison.BaseValue} + tick on {target.Name}");
    }
}
