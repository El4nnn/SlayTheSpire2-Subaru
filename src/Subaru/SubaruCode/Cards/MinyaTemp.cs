using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 迷尼亚（临时）：由「埃尔·迷尼亚」生成的战斗内临时牌。
/// 面板与[迷尼亚]完全一致（3 伤 2 段，升级 3 段），额外带[消耗]。
/// 不进任何卡池、不能以其他方式获取，也不会带出战斗。
/// 2026-11-18 增强：费用 1 → 0。
/// </summary>
public sealed class MinyaTemp() : SubaruCard(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override bool IsGenerateOnly => true;

    /// <summary>不等弹幕命中再出伤（玩家反馈这张牌拖沓）：动画照播，伤害立刻结算。</summary>
    public override bool WaitsForVolleyHit => false;

    /// <summary>弹幕发数按段数走（同[迷尼亚]，见 <see cref="Vfx.SpellVolley.VisualBullets"/>）。</summary>
    public override int SpellVolleyHits
    {
        get
        {
            try
            {
                return System.Math.Max(1, (int)DynamicVars["Hits"].BaseValue);
            }
            catch
            {
                return 1;
            }
        }
    }

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(3m, ValueProp.Move),
        new IntVar("Hits", 2m),
    ];

    protected override void OnUpgrade() => DynamicVars["Hits"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitCount((int)DynamicVars["Hits"].BaseValue)
            .Execute(choiceContext);
    }
}
