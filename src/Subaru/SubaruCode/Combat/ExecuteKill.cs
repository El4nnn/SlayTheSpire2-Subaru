using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Combat;

/// <summary>
/// 「斩杀 / 抹除」的统一入口：阿尔·纱幕、傲慢魔女·提丰，以及以后所有秒杀牌都走这里。
///
/// <para><b>口径（2026-11-20 定，动手前先读这段）</b></para>
/// <list type="bullet">
/// <item>候选集只判两件事：<b>还活着</b> + <b>血量严格小于阈值</b>。
/// 绝不能用 <c>CombatState.HittableEnemies</c> —— 它等于 <c>Enemies.Where(e =&gt; e.IsHittable)</c>，
/// 而 <c>Creature.IsHittable = !IsDead &amp;&amp; Hook.ShouldAllowHitting(...)</c>，
/// 会被 <c>IllusionPower / ReattachPower / DieForYouPower / AdaptablePower</c> 否决：
/// 「活着但这一下打不到」的怪会被静默跳过，玩家看到的就是「写着能斩、实际没斩」。
/// （2026-11-20 玩家反馈阿尔·纱幕 4×12 却斩不掉 30 血敌人，就是这道闸埋的坑。）</item>
/// <item>抹除统一走 <c>CreatureCmd.Kill(targets, force: true)</c>：同一个重载、<c>cardSource = null</c>。</item>
/// <item><c>force</c> 仍要过 <c>BeforeDeath</c> / <c>Hook.ShouldDie</c>：如果某只怪带「不能死」机制，
/// 这里不会抛错，只会没杀死 —— 日志会打出来，别再猜。</item>
/// </list>
/// </summary>
public static class ExecuteKill
{
    /// <summary>能不能斩：非空、没死、血量严格低于阈值（和卡面「低于」一致）。</summary>
    public static bool CanKill(Creature creature, decimal threshold) =>
        creature != null && !creature.IsDead && creature.CurrentHp < threshold;

    /// <summary>场上所有「能斩」的敌人（不经过 HittableEnemies）。</summary>
    public static List<Creature> Below(ICombatState state, decimal threshold)
    {
        var list = new List<Creature>();
        if (state?.Enemies == null)
            return list;

        foreach (var c in state.Enemies)
        {
            if (CanKill(c, threshold))
                list.Add(c);
        }
        return list;
    }

    /// <summary>抹除一批目标；返回真正下手的数量。已死/重复的目标会被跳过。</summary>
    public static async Task<int> KillAsync(IEnumerable<Creature> targets, string tag)
    {
        var list = new List<Creature>();
        if (targets != null)
        {
            foreach (var c in targets)
            {
                if (c == null || c.IsDead || list.Contains(c))
                    continue;
                list.Add(c);
            }
        }

        if (list.Count == 0)
        {
            GD.Print($"[Subaru] execute({tag}): nothing to kill");
            return 0;
        }

        foreach (var c in list)
            GD.Print($"[Subaru] execute({tag}) -> {c.Name}: hp={c.CurrentHp}/{c.MaxHp}");

        try
        {
            await CreatureCmd.Kill(list, true);
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] execute({tag}) failed: {e}");
            return 0;
        }

        GD.Print($"[Subaru] execute({tag}): killed={list.Count}");
        return list.Count;
    }

    /// <summary>
    /// 斩杀「所有血量低于阈值的敌人」，返回斩杀数量。
    /// <paramref name="verbose"/> = true 时把场上每只怪的血量/是否可命中都打出来（排查「为什么没斩到」用）。
    /// </summary>
    public static async Task<int> KillBelowAsync(ICombatState state, decimal threshold, string tag,
                                                 bool verbose = false)
    {
        var victims = Below(state, threshold);

        if (verbose && state?.Enemies != null)
        {
            GD.Print($"[Subaru] execute({tag}): threshold={threshold}, enemies={state.Enemies.Count}, "
                     + $"victims={victims.Count}");
            foreach (var e in state.Enemies)
            {
                if (e == null)
                    continue;
                GD.Print($"[Subaru]   enemy {e.Name}: hp={e.CurrentHp}/{e.MaxHp}, dead={e.IsDead}, "
                         + $"hittable={e.IsHittable}, inVictims={victims.Contains(e)}");
            }
        }

        return await KillAsync(victims, tag);
    }
}
