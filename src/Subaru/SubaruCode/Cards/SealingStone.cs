using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 封魔石：稀有，3 费（升级 2 费），技能，目标任意敌人。[阴魔法][消耗]。
/// 该敌人永久失去「本场打出过的[阴魔法]张数」层[力量]（每张 1 层）。
///
/// 削减方式照「星蚀」：直接给官方 <see cref="StrengthPower"/> 挂负层数，
/// **不额外挂记账能力**，所以敌人状态栏只有一个「力量 ↓」图标
/// （食精灵者那种隐藏记账能力只在「临时削减要到期还原」时才需要）。
///
/// 计数器随死归回溯（见 <see cref="YinLedger"/>）；削减本身跟随战斗状态，
/// 回档会一起还原，两者口径一致。
/// </summary>
public sealed class SealingStone() : SubaruCard(3, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new SealCountVar()];

    // 升级只减费：3 → 2
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        // 本张不算：记账挂在 OnPlay 里 OnPlayCard 之后
        var count = YinLedger.Count;
        if (count <= 0)
        {
            Godot.GD.Print("[Subaru] sealing stone: no yin magic played yet, nothing to seal");
            return;
        }

        await PowerCmd.Apply<StrengthPower>(
            choiceContext, cardPlay.Target, -count, Owner.Creature, this, false);

        Godot.GD.Print($"[Subaru] sealing stone: {cardPlay.Target.Name} -{count} strength");
    }
}

/// <summary>
/// 卡面上显示「现在会削多少层」，每次渲染重新取一次值。
///
/// ⚠️ 必须继承 <c>IntVar</c> 而不是 <c>StringVar</c>：第三方 mod 枚举动态变量算选择键时，
/// 遇到没登记的**字符串**变量会抛 `InvalidOperationException: 未分类的字符串状态字段`，
/// 把回合循环搞死（战斗卡住）。数值类型用 0 占位，实际值由 ToString 现算。
/// </summary>
public sealed class SealCountVar() : IntVar("SealCount", 0m)
{
    public override string ToString() => YinLedger.Count.ToString();
}
