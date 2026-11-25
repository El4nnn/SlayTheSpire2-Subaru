using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 前代剑圣特蕾西娅的「死神的加护」——被她砍伤的对象，伤口即使愈合也会再度裂开。
///
/// <para>①（原效果）己方回合结束时检查敌人身上有没有[虚弱]/[易伤]，
/// 有就把那个计数器直接推到 99（本场战斗内等同于不衰减）。
/// 不去改能力的自然衰减逻辑，只在上限上做手脚。</para>
///
/// <para>②（2026-11-21 用户新增）同一个时间点，再给**所有敌人**叠「层数」层[中毒]
/// （层数 = 这张能力自己的 <c>Amount</c>，卡面给 3、升级后 4）。</para>
/// </summary>
public sealed class TheresiaPower : CustomPowerModel, ICorrodedPower
{
    private const int Permanent = 99;

    public override PowerType Type => PowerType.Buff;

    // 层数现在有意义了（每回合结束时叠多少中毒）→ 显示数字
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "theresia_power.png".PowerImagePath();
    public override string CustomBigIconPath => "theresia_power.png".BigPowerImagePath();

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        try
        {
            var owner = Owner;
            if (owner == null || side != CombatSide.Player || !participants.Contains(owner))
                return;

            var state = owner.CombatState;
            if (state == null)
                return;

            var touched = 0;

            foreach (var enemy in state.HittableEnemies)
            {
                foreach (var power in enemy.Powers)
                {
                    if (power is not (WeakPower or VulnerablePower))
                        continue;

                    if (power.Amount >= Permanent)
                        continue;

                    power.Amount = Permanent;
                    touched++;
                }
            }

            if (touched > 0)
                GD.Print($"[Subaru] theresia: pinned {touched} debuff(s) to {Permanent}");

            // 新增：回合结束给所有敌人叠毒（2026-11-21 用户定）
            var poison = (int)System.Math.Max(0m, Amount);
            if (poison <= 0)
                return;

            var targets = state.HittableEnemies.ToList();
            foreach (var enemy in targets)
            {
                if (enemy == null || enemy.IsDead || enemy.CurrentHp <= 0)
                    continue;

                await PowerCmd.Apply<PoisonPower>(choiceContext, enemy, poison, owner, null, false);
            }

            GD.Print($"[Subaru] theresia: {poison} poison to {targets.Count} enemy(ies)");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] theresia failed: " + e);
        }
    }
}
