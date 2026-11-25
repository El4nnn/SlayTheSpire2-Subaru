using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 毒辣翁·奥尔巴特：稀有，2 费，技能，目标任意敌人。[消耗][虚无]。
///
/// 使该敌人缩小：挂原版 <see cref="ShrinkPower"/>，层数传 <c>-1</c>。
/// ShrinkPower 的 <c>IsInfinite =&gt; Amount &lt; 0</c>，所以负层数就是「本场战斗永久」
/// （缩小甲虫自己也是传 -1；这个"永久"只在本场战斗内，死归回档会跟着战斗状态一起还原）。
///
/// 升级：取消[虚无]（照原版「融合」的写法，OnUpgrade 里 RemoveKeyword）。
/// </summary>
public sealed class Orbart() : SubaruCard(2, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
{
    /// <summary><c>data/cutin.json</c> 里的毒辣翁·奥尔巴特立绘（2026-11-22 用户提供）。</summary>
    public override string? CutInId => "orbart";

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust, CardKeyword.Ethereal,
         SubaruKeywords.NineGenerals];

    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Ethereal);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        // 缩小：-1 层 = 本场永久（ShrinkPower.IsInfinite => Amount < 0）
        await PowerCmd.Apply<ShrinkPower>(
            choiceContext, cardPlay.Target, -1m, Owner.Creature, this, false);

        Godot.GD.Print($"[Subaru] orbart: {cardPlay.Target.Name} shrunk");
    }
}
