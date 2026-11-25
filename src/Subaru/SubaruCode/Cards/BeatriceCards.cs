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

/// <summary>幼女使者：稀有，能力，2 费，获得 3 层[幼女使者]。[贝蒂][保留]。升级：固有，且关联牌升级。</summary>
public sealed class Lolimancer() : SubaruCard(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Retain];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<LolimancerPower>(3m)];

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var amount = DynamicVars["LolimancerPower"].BaseValue;

        await PowerCmd.Apply<LolimancerPower>(choiceContext, Owner.Creature, amount, Owner.Creature, this, false);

        // 升级过的「幼女使者」生成的 E·M·T / E·M·M 也是升级版
        var power = Owner.Creature.Powers.FirstOrDefault(p => p is LolimancerPower) as LolimancerPower;
        if (power != null)
            power.GrantsUpgraded = IsUpgraded;
    }
}

/// <summary>E·M·T：稀有，0 费，技能。本回合受到伤害减半；若受过伤害，下回合 +1 能量。[贝蒂][消耗]，升级：追加[保留]（消耗保留）。</summary>
public sealed class Emt() : SubaruCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override bool IsGenerateOnly => true;

    // 卡面写 {Energy:energyIcons()}，这里得把变量声明出来，否则会直接显示原始文本
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    // 升级只加[保留]，不摘[消耗]——否则升级版能在牌组里无限循环
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<EmtPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);
    }
}

/// <summary>E·M·M：稀有，0 费，技能。获得 1 层[缓冲]。[贝蒂][消耗]，升级：追加[保留]（消耗保留）。</summary>
public sealed class Emm() : SubaruCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override bool IsGenerateOnly => true;

    // 卡面写 {BufferPower:diff()}，必须声明变量，否则原样显示
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<BufferPower>(1m)];

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    // 升级只加[保留]，不摘[消耗]
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<BufferPower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);
    }
}
