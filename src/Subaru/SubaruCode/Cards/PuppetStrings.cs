using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

using Subaru.SubaruCode.Rewind;
using Subaru.SubaruCode.Vfx;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 八重·天膳（原「操丝术」，2026-11-21 改名；卡面与数值不动）：普通，1 费，攻击。
/// 对随机敌人造成 {Damage} 点伤害 {Times} 次。升级：伤害 2→3（次数保持 4）。
///
/// <para>每一段都重新摇一次目标，所以和战士的「回旋镖」一样，可以几段打在不同敌人身上。</para>
///
/// <para>演出（2026-11-21 用户定）：先播立绘 cut-in（<see cref="CutIn"/>，id = <c>yae</c>），
/// 立绘**定格**不走；<see cref="ScreenCut"/> 的攻击演出（四根丝线依次切开画面）播完之后，
/// 才放立绘退场 —— 和夏乌拉同一个套路（夏乌拉是「立绘定格 → 射线命中 → 退场」）。</para>
///
/// <para>立绘**同一回合只播一次**（和夏乌拉同一条规则，记账在
/// <see cref="CombatStats.IsCutInFirstThisTurn"/>）：同回合第二张起只播切屏，不再出立绘。</para>
/// </summary>
public sealed class PuppetStrings() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.RandomEnemy)
{
    /// <summary>八重·天膳有切屏演出，停掉默认攻击动作。</summary>
    public override bool HasCustomAttackVfx => true;

    /// <summary>这张牌在 <c>data/cutin.json</c> 里的立绘 id。</summary>
    private const string CutInId = "yae";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(2m, ValueProp.Move),
        new IntVar("Times", 4m),
    ];

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = CombatState;
        if (state == null)
            return;

        var times = (int)DynamicVars["Times"].BaseValue;

        // 立绘切入并定格（返回到 hitAt 那一刻）。出场由「角色 cut-in」的设置决定
        // （<see cref="CutIn.MayShow"/>：总开关 + 每角色每回合一次 + 每回合最多 N 个不同角色）。
        CutIn.Session? cutIn = null;
        if (CutIn.MayShow(CutInId))
            cutIn = await CutIn.PlayAndHoldAsync(Owner, CutInId);

        // 屏幕切割演出：一刀一段，节奏跟着多段伤害走；不阻塞结算，失败了也照常打
        var fx = ScreenCut.Start(times);
        try
        {
            for (var i = 0; i < times; i++)
            {
                // 每段重新取一次活着敌人：前面的段可能已经把目标打死了
                var enemies = state.HittableEnemies.ToList();
                if (enemies.Count == 0)
                    break;

                var target = state.RunState.Rng.CombatTargets.NextItem(enemies);
                if (target == null)
                    break;

                fx?.Cut();

                await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                    .FromCard(this, cardPlay)
                    .Targeting(target)
                    .Execute(choiceContext);
            }
        }
        finally
        {
            fx?.Finish();

            // 攻击动画（合拢 + 释放）播完，才让立绘退场
            if (cutIn != null)
            {
                if (fx != null)
                    await ScreenCut.WaitReleasedAsync();

                cutIn.Exit();
            }
        }
    }
}
