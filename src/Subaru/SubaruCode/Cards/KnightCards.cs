using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 英雄：稀有，2 费，技能。获得 3 点[力量]、1 点[敏捷]、3 点[活力]；
/// 手牌中每有 1 张[魔女瘴气]，获得 1 点能量。[消耗]。
/// 升级：力量 3→4、敏捷 1→2、活力 3→4。
///
/// 2026-11-18 重做：原来是「14 伤 + 每张瘴气 8 伤」的单体攻击牌（[消耗]），
/// 改成把瘴气换成能量、再把能量换成三围的收尾牌 —— 瘴气越多，这张牌越赚。
/// </summary>
public sealed class Hero() : SubaruCard(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<StrengthPower>(3m),
        new PowerVar<DexterityPower>(1m),
        // [活力]在原版没有强类型变量，全 mod 统一用 IntVar("Vigor")（见常体和福音）
        new IntVar("Vigor", 3m),
        // 每张瘴气换多少能量（目前恒为 1，做成变量才能在卡面显示）
        new IntVar("PerCurse", 1m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Strength.UpgradeValueBy(1m);   // 3 → 4
        DynamicVars.Dexterity.UpgradeValueBy(1m);  // 1 → 2
        DynamicVars["Vigor"].UpgradeValueBy(1m);   // 3 → 4
    }

    /// <summary>
    /// 先给三围，再按手牌里的[魔女瘴气]张数换能量。
    ///
    /// 注意：**不消耗瘴气**。旧版「消耗全部瘴气」的写法在移除手牌后会让后续指令拿到失效引用
    /// （实测 NullReferenceException：卡面停在屏幕上、效果也不结算），所以这里只数不清。
    /// </summary>
    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player?.Creature == null)
            return;

        var str = DynamicVars.Strength.BaseValue;
        var dex = DynamicVars.Dexterity.BaseValue;
        var vigor = DynamicVars["Vigor"].BaseValue;

        await PowerCmd.Apply<StrengthPower>(choiceContext, player.Creature, str, player.Creature, this, false);
        await PowerCmd.Apply<DexterityPower>(choiceContext, player.Creature, dex, player.Creature, this, false);
        await PowerCmd.Apply<VigorPower>(choiceContext, player.Creature, vigor, player.Creature, this, false);

        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return;

        var miasma = pcs.Hand.Cards.Count(c => c is CurseWitchMiasma);
        var energy = (int)(DynamicVars["PerCurse"].BaseValue * miasma);

        if (energy > 0)
            pcs.GainEnergy(energy);

        GD.Print($"[Subaru] hero: str={str} dex={dex} vigor={vigor}, "
                 + $"miasma in hand={miasma}, energy +{energy}");
    }
}

/// <summary>
/// 首席骑士：罕见，2 费，能力。消耗 1 张[魔女瘴气]；清除 1 层[癫狂]；
/// 移除自身的所有其他负面状态；+1 力量 +1 敏捷。升级：[保留]。
///
/// 魔女遗香虽然是 Debuff，但它是这套机制的「代价」，不参与这里的清负面，
/// 否则一张牌就能把整局的代价清零。癫狂同理，只减 1 层，不能整条解除。
/// </summary>
public sealed class FirstKnight() : SubaruCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<StrengthPower>(1m),
        new PowerVar<DexterityPower>(1m),
        // 卡面末尾的「本场已消耗」提示：ToString 每次渲染现算，不用刷新节点
        new FirstKnightUsedVar(),
    ];

    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var pcs = Owner?.PlayerCombatState;
        if (pcs == null)
            return;

        // 消耗 1 张魔女瘴气（每场战斗一次额度）
        var curse = pcs.Hand.Cards.FirstOrDefault(c => c is CurseWitchMiasma);
        if (curse != null && CurseRemoval.TryUse(CurseRemoval.FirstKnight))
        {
            // 这张牌明确写了「消耗 1 张魔女瘴气」，是瘴气防线的允许例外
            using (Patches.MiasmaGuard.Allow())
                await CardPileCmd.RemoveFromCombat(curse, false);
        }

        // 清除自身所有负面状态（魔女遗香、癫狂除外）
        var debuffs = Owner.Creature.Powers
            .Where(p => p.Type == PowerType.Debuff
                        && p is not WitchFragrancePower
                        && p is not MadnessPower)
            .ToList();

        foreach (var debuff in debuffs)
            await PowerCmd.Remove(debuff);

        GD.Print($"[Subaru] first knight: cleared {debuffs.Count} debuffs");

        // 癫狂只减 1 层（3 层 → 2 层，等于砍掉 1 回合）
        var madness = Owner.Creature.Powers.FirstOrDefault(p => p is MadnessPower);
        if (madness != null)
        {
            madness.Amount -= 1;
            GD.Print("[Subaru] first knight: madness -1 => " + madness.Amount);

            if (madness.Amount <= 0)
                madness.RemoveInternal();
        }

        await PowerCmd.Apply<StrengthPower>(
            choiceContext, Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this, false);
        await PowerCmd.Apply<DexterityPower>(
            choiceContext, Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this, false);
    }
}

/// <summary>
/// 首席骑士卡面末尾的动态提示。值在 ToString() 里现算：卡面每次渲染都会取一次，
/// 所以打出过之后手里/抽牌堆里的下一张也会立刻显示「本场已消耗」，不需要去找节点刷新。
///
/// ⚠️ 必须继承 <c>IntVar</c> 而不是 <c>StringVar</c>：第三方 mod 会给每张牌算选择键，
/// 枚举动态变量时遇到没登记的**字符串**变量会抛
/// `InvalidOperationException: 未分类的字符串状态字段`，把整个回合循环搞死（战斗卡住）。
/// 这里只借用「ToString 现算」这个能力，用途是文本，数值类型用 0 占位。
/// </summary>
public sealed class FirstKnightUsedVar() : IntVar("FirstKnightUsed", 0m)
{
    public override string ToString() =>
        CurseRemoval.IsUsed(CurseRemoval.FirstKnight) ? "\n[gold]（本场已消耗过魔女瘴气）[/gold]" : "";
}
