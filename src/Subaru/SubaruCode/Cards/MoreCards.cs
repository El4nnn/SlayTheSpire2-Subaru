using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>纱幕：普通，群体虚弱 + 易伤。初始牌组 ×1。升级：虚弱 2→3，易伤 1→2。</summary>
public sealed class Shamak() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
{
    // [阴魔法]：贝蒂在场时，每回合第一张阴魔法费用 -1（纱幕不进初始牌组的贝蒂限制）
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<WeakPower>(2m),
        new PowerVar<VulnerablePower>(1m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Weak.UpgradeValueBy(1m);
        DynamicVars.Vulnerable.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach (var enemy in CombatState.HittableEnemies)
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, enemy, DynamicVars.Weak.BaseValue, Owner.Creature, this, false);
            await PowerCmd.Apply<VulnerablePower>(choiceContext, enemy, DynamicVars.Vulnerable.BaseValue, Owner.Creature, this, false);
        }
    }
}

/// <summary>帕特拉修：普通，8 伤害 + 1 易伤。升级：8→10，易伤 1→2。</summary>
public sealed class Patrasche() : SubaruCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6m, ValueProp.Move),
        new PowerVar<VulnerablePower>(1m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2m);
        DynamicVars.Vulnerable.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        await PowerCmd.Apply<VulnerablePower>(
            choiceContext, cardPlay.Target, DynamicVars.Vulnerable.BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>跑酷：普通，能力，获得 3 敏捷。升级：3→5。</summary>
public sealed class Parkour() : SubaruCard(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<DexterityPower>(2m)];

    protected override void OnUpgrade() => DynamicVars.Dexterity.UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DexterityPower>(
            choiceContext, Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>星蚀：稀有，7 伤害并偷取目标力量与敏捷。[消耗]。升级：伤害 7→10。</summary>
public sealed class StarEclipse() : SubaruCard(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary>星蚀的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "eclipse";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, CardKeyword.Exhaust];

    protected override int Authority => 5;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(7m, ValueProp.Move),
        new PowerVar<StrengthPower>(2m),
        new PowerVar<DexterityPower>(2m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        DynamicVars.Strength.UpgradeValueBy(1m);
        DynamicVars.Dexterity.UpgradeValueBy(1m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var target = cardPlay.Target;
        var stealStr = DynamicVars.Strength.BaseValue;
        var stealDex = DynamicVars.Dexterity.BaseValue;

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(target)
            .Execute(choiceContext);

        // 偷取：从目标身上扣（负数层数），加到自己身上
        try
        {
            var before = target.GetPowerAmount<StrengthPower>();
            await PowerCmd.Apply<StrengthPower>(choiceContext, target, -stealStr, Owner.Creature, this, false);
            Godot.GD.Print($"[Subaru] eclipse steal str: {before} -> {target.GetPowerAmount<StrengthPower>()}");

            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, stealStr, Owner.Creature, this, false);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] eclipse strength failed: " + e);
        }

        try
        {
            var before = target.GetPowerAmount<DexterityPower>();
            await PowerCmd.Apply<DexterityPower>(choiceContext, target, -stealDex, Owner.Creature, this, false);
            Godot.GD.Print($"[Subaru] eclipse steal dex: {before} -> {target.GetPowerAmount<DexterityPower>()}");

            await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, stealDex, Owner.Creature, this, false);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] eclipse dexterity failed: " + e);
        }
    }
}

/// <summary>不可视之神意：罕见，9 点伤害 2 次 + 1 张晕眩进抽牌堆。权能 4。升级：9→13。</summary>
public sealed class InvisibleProvidence() : SubaruCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords => [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    /// <summary>段数写成 DynamicVar，卡面 <c>{Hits}</c> 才会跟着代码走（不要写死「两次」）。</summary>
    private const int Hits = 2;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(9m, ValueProp.Move),
        new IntVar("Hits", Hits),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);

        // 把 1 张[晕眩]洗进抽牌堆
        try
        {
            var dazed = CombatState.CreateCard<Dazed>(Owner);
            if (dazed != null)
                await CardPileCmd.Add(dazed, PileType.Draw, CardPilePosition.Random, null, false);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] add dazed failed: " + e);
        }
    }
}

/// <summary>
/// 傲慢司教：稀有，2 费，能力。获得 3 层[傲慢]；此后每次死归，额外获得 2 点力量与 2 点敏捷。
/// 权能 18、腐蚀。升级：固有。（0.2.43：3 费 → 2 费）
///
/// <para>2026-11-27：稀有度由「先古」改为「稀有」。先古卡在引擎里只由两件遗物发放
/// （远古之牙 / 尘封之书），玩家几乎摸不到；改为稀有后正常进奖励池与商店。
/// 卡面同时按普通卡比例重裁（500×380 / 1000×760）。</para>
/// </summary>
public sealed class ArchbishopPride() : SubaruCard(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>傲慢司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "pride";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, SubaruKeywords.Corrosion];

    protected override int Authority => 18;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<PridePower>(3m)];

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<PridePower>(
            choiceContext, Owner.Creature, DynamicVars["PridePower"].BaseValue, Owner.Creature, this, false);

        // 加护本体：死归时由 DeathReturnSpike 读取
        await PowerCmd.Apply<ArchbishopBlessingPower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);

        Godot.GD.Print("[Subaru] archbishop pride played");
    }
}

/// <summary>
/// 魔女的茶会：罕见，获得 2 能量，失去 3 点生命。[消耗]。权能 2。
/// 升级：取消[消耗]（照「治愈魔法」的写法，OnUpgrade 里 RemoveKeyword）。
/// 2026-11-19 削弱：基础加上[消耗]；升级后不再消耗，一局只能升级一次，等于只能回本一次。
/// </summary>
public sealed class WitchTeaParty() : SubaruCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    /// <summary>打出这张牌时播艾姬多娜的 cut-in（2026-11-21 用户定）。</summary>
    public override string? CutInId => "echidna";

    /// <summary>只从 mod 专属事件获得，不进卡牌奖励池 / 商店。</summary>
    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, CardKeyword.Exhaust];

    protected override int Authority => 2;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new EnergyVar(2),
        new HpLossVar(3m),
    ];

    protected override void OnUpgrade()
    {
        RemoveKeyword(CardKeyword.Exhaust);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Owner.PlayerCombatState?.GainEnergy(DynamicVars.Energy.BaseValue);

        await CreatureCmd.Damage(choiceContext, Owner.Creature,
            DynamicVars.HpLoss.BaseValue,
            ValueProp.Unblockable, Owner.Creature);

        // 强欲的契约：本回合打过茶会 → 费用 -1（本回合有效）
        try
        {
            var hand = Owner.PlayerCombatState?.Hand;
            if (hand != null)
            {
                foreach (var card in hand.Cards.Where(c => c is GreedContract))
                    card.EnergyCost.AddThisTurn(-1);
            }
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] tea party discount failed: " + e);
        }
    }
}

/// <summary>龙血：稀有，能力，获得 1 层缓冲。升级：费用 1→0。（2026-11-17 平衡：罕见 → 稀有）</summary>
public sealed class DragonBlood() : SubaruCard(1, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    // 升级只改费用，没有数值变化（走 EnergyCost.UpgradeBy 才生效）
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<BufferPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);
    }
}
