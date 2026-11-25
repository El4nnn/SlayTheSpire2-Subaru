using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 钢人·莫古洛：罕见，2 费，技能，自身。[九神将][消耗]。
/// 失去 2 点[敏捷]（本场战斗内永久），获得 33 点[格挡]。升级：格挡 33→45。
/// </summary>
public sealed class Moguro() : SubaruCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>立绘 cut-in（2026-11-22 接）：钢人·莫古洛，技能牌只有立绘，不接斩击。</summary>
    public override string? CutInId => "moguro";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals, CardKeyword.Exhaust];

    private const decimal DexterityLoss = 2m;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(33m, ValueProp.Move)];

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(12m); // 33 → 45

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        // 失去 2 点敏捷：直接给官方 DexterityPower 挂负层数（和星蚀偷敏捷同一套写法）
        await PowerCmd.Apply<DexterityPower>(
            choiceContext, player.Creature, -DexterityLoss, player.Creature, this, false);

        await CreatureCmd.GainBlock(player.Creature, DynamicVars.Block, cardPlay);

        Godot.GD.Print($"[Subaru] moguro: -{DexterityLoss} dex, +{DynamicVars.Block.BaseValue} block");
    }
}
