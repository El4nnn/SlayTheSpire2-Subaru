using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>真·不可视之神意：罕见，0 费，4 伤 4 次。权能 3、消耗。只能由[怠惰因子]生成。升级：4→5。</summary>
public sealed class TrueInvisibleProvidence() : SubaruCard(0, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    /// <summary>怠惰司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "sloth";

    public override bool IsGenerateOnly => true;

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, CardKeyword.Exhaust];

    protected override int Authority => 2;

    /// <summary>段数写成 DynamicVar，卡面 <c>{Hits}</c> 才会跟着代码走（不要写死「4 次」）。</summary>
    private const int Hits = 4;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(4m, ValueProp.Move),
        new IntVar("Hits", Hits),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        // 多段小数字：方便吃力量加成（4 × 4）
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);
    }
}

/// <summary>
/// 忧郁魔女：罕见，1 费。消耗 1 张牌，按它的稀有度结算：
/// 普通 / 基本 / 诅咒 / 状态 → 5 格挡 + 对随机敌人 6 伤害
/// 罕见                      → 8 格挡 + 对所有敌人 10 伤害
/// 稀有                      → 12 格挡 + 对所有敌人 14 无视格挡伤害
/// 权能 12。升级：数值升级（0.1.7 起本体不再带[消耗]）。
/// </summary>
/// <remarks>
/// 三个伤害必须是 <c>DamageVar</c>（不是 <c>IntVar</c>）：只有 DamageVar 会走
/// <c>UpdateCardPreview</c>，卡面才吃得到锋利 / 本能的显示变化。
/// 稀有档「无视格挡」写 <c>Move | Unblockable</c> —— 只写 <c>Unblockable</c> 会被
/// <c>IsPoweredAttack</c> 判成非攻击，力量 / 易伤 / 附魔全部失效。
/// </remarks>
public sealed class WitchOfMelancholy() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    private const ValueProp Props = ValueProp.Move;
    private const ValueProp RareProps = ValueProp.Move | ValueProp.Unblockable;

    // 本体不再带[消耗]（0.1.7 改）；升级让位给数值升级
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority];

    protected override int Authority => 4;

    // 三档六个数都走变量，升级才能各自加值并显示在卡面上。
    // 格挡仍是 IntVar（GainBlock 收 decimal，且本牌是技能牌，吃不到格挡附魔的档次不多），
    // 伤害是 DamageVar，见类上边的 remarks。
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("BlockCommon", 5m),
        new DamageVar("DmgCommon", 6m, Props),
        new IntVar("BlockUncommon", 8m),
        new DamageVar("DmgUncommon", 10m, Props),
        new IntVar("BlockRare", 12m),
        new DamageVar("DmgRare", 14m, RareProps),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars["BlockCommon"].UpgradeValueBy(3m);
        DynamicVars["DmgCommon"].UpgradeValueBy(3m);

        DynamicVars["BlockUncommon"].UpgradeValueBy(4m);
        DynamicVars["DmgUncommon"].UpgradeValueBy(5m);

        DynamicVars["BlockRare"].UpgradeValueBy(5m);
        DynamicVars["DmgRare"].UpgradeValueBy(7m);
    }

    private decimal V(string name) => DynamicVars[name].BaseValue;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        var pcs = player?.PlayerCombatState;
        if (player == null || state == null || pcs == null)
            return;

        // 选 1 张牌消耗（不能选自己；「消耗 1 张牌」没有明确指定魔女瘴气，也选不到它）
        var selected = await CardSelectCmd.FromHand(
            choiceContext,
            player,
            new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1),
            c => c != this && c is not CurseWitchMiasma,
            this);

        var picked = selected?.FirstOrDefault();
        if (picked == null)
            return;

        var rarity = picked.Rarity;

        // 先结算再消耗：消耗之后牌的状态可能已经变干净了
        await CardCmd.Exhaust(choiceContext, picked, false, false);

        if (rarity == CardRarity.Rare)
        {
            await CreatureCmd.GainBlock(player.Creature, V("BlockRare"), ValueProp.Move, cardPlay, false);

            var rareAttack = DamageCmd.Attack(V("DmgRare")).FromCard(this, cardPlay).TargetingAllOpponents(state);
            rareAttack.DamageProps = RareProps;
            await rareAttack.Execute(choiceContext);

            Godot.GD.Print("[Subaru] melancholy: rare");
            return;
        }

        if (rarity == CardRarity.Uncommon)
        {
            await CreatureCmd.GainBlock(player.Creature, V("BlockUncommon"), ValueProp.Move, cardPlay, false);
            await DamageCmd.Attack(V("DmgUncommon")).FromCard(this, cardPlay).TargetingAllOpponents(state).Execute(choiceContext);
            Godot.GD.Print("[Subaru] melancholy: uncommon");
            return;
        }

        // 普通（含基本 / 诅咒 / 状态 / 衍生牌）
        await CreatureCmd.GainBlock(player.Creature, V("BlockCommon"), ValueProp.Move, cardPlay, false);

        var enemies = state.HittableEnemies.ToList();
        if (enemies.Count > 0)
        {
            var target = state.RunState.Rng.CombatTargets.NextItem(enemies);
            if (target != null)
                await DamageCmd.Attack(V("DmgCommon")).FromCard(this, cardPlay).Targeting(target).Execute(choiceContext);
        }

        Godot.GD.Print("[Subaru] melancholy: common");
    }
}
