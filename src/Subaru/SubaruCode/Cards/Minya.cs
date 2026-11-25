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
/// 迷尼亚：普通，1 费，攻击（单体）。造成 3 点伤害 2 次。升级：2 次 → 3 次。
///
/// 贝蒂的阴魔法。只能由「契约·贝蒂」把[打击]变化而来：
/// IsGenerateOnly = true（不进奖励池/商店），也不在任何随机生成池里。
/// 按战斗范围变化的——战斗结束后[打击]回到牌组，迷尼亚随之消失，下一场再来一次。
/// </summary>
public sealed class Minya() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override bool IsGenerateOnly => true;

    /// <summary>不等弹幕命中再出伤（玩家反馈这张牌拖沓）：动画照播，伤害立刻结算。</summary>
    public override bool WaitsForVolleyHit => false;

    /// <summary>
    /// 弹幕发数按**段数**走（1 段 5 发 / 2 段 8 发 / 3 段 13 发，表在
    /// <see cref="Vfx.SpellVolley.VisualBullets"/>）。默认值 1 会让多段牌永远是 5 发 —— 2026-11-21 补上。
    /// </summary>
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
                return 1;   // 变量还没建好（构造期）就当 1 段
            }
        }
    }

    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

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
