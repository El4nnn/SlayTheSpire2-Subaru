using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 怠惰因子：罕见，2 费，能力。每回合开始时将 1 张[真·不可视之神意]加入手牌。
/// 权能 10。升级：费用 2→1，且生成的牌也是升级版。
/// </summary>
public sealed class SlothFactor() : SubaruCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>怠惰司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "sloth";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 5;

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<SlothFactorPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);

        // 升级过的「怠惰因子」生成的也是升级版
        var power = Owner.Creature.Powers.FirstOrDefault(p => p is SlothFactorPower) as SlothFactorPower;
        if (power != null)
            power.GrantsUpgraded = IsUpgraded;
    }
}

/// <summary>
/// 强欲的契约：稀有，4 费，能力。每回合开始 +1 能量并抽 1 张牌；
/// 每回合结束可让最多 2 张手牌[保留]，并获得 10 层[魔女遗香]。
/// [固有][保留][腐蚀]。升级：费用 4→3。
/// </summary>
public sealed class GreedContract() : SubaruCard(4, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>打出这张牌时播艾姬多娜的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "echidna";

    /// <summary>只从 mod 专属事件获得，不进卡牌奖励池 / 商店。</summary>
    public override bool IsGenerateOnly => true;

    // [固有] 从 0.1.7 起改成基础就带（升级让位给降费）
    public override HashSet<CardKeyword> CanonicalKeywords =>
    [
        .. base.CanonicalKeywords,
        CardKeyword.Innate,
        CardKeyword.Retain,
        SubaruKeywords.Corrosion,
    ];

    // 描述里用了 {Energy:energyIcons()}，必须声明变量，否则会原样显示
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1); // 4 → 3

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<GreedContractPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);
    }
}
