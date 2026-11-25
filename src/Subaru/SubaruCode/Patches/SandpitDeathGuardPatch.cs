using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;


// 说明：这里只放「玩家死亡闸门」，沙坑的回合末伤害拦截见 SandpitLethalGuardPatch 的历史版本，
// 现已由本闸门统一覆盖（死亡如果走 Kill，也会被这里接住）。

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 玩家死亡闸门：只要「还有死归可用」，玩家的非受伤型死亡（直接 Kill）就改走死归，
/// 而不是让玩家真死。典型场景：无厌沙虫的沙坑覆盖被解开时，原版会直接判死，
/// 这条路径绕过了「保 1 血」的伤害钩子，所以必须在 Kill 这一层接。
///
/// 规则（按需求）：
///   - 死归可用（有傲慢因子、且不在癫狂锁定）→ 取消击杀；
///       · 如果当前已经在回档中（IsRewinding）→ 只取消，不再触发第二次，避免嵌套；
///       · 否则 → 主动触发一次死归；
///   - 死归不可用 → 不拦截，让玩家真死。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), "Kill", [typeof(Creature), typeof(CardModel), typeof(bool)])]
public static class PlayerDeathGatePatch
{
    /// <summary>身上还有傲慢因子（初始遗物 / 升级版）= 这一条命还可以死归。</summary>
    public static bool CanRewind(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            return player != null && player.Relics.Any(
                r => r is Subaru.SubaruCode.Rewind.PrideFactorSpike
                  || r is Subaru.SubaruCode.Rewind.PrideFactorPlus);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 现在该不该放行击杀：只要「不在战斗中」或「这局正在收尾」就放行（真死）。
    /// 覆盖两种场景：放弃一局（先标记 IsAbandoned 再 GuaranteeKillAllPlayers）、
    /// 以及事件/地图里被直接处死。
    /// </summary>
    private static bool AllowRealDeath()
    {
        return !Subaru.SubaruCode.Rewind.DeathReturnSpike.RewindAllowedNow();
    }

    [HarmonyPrefix]
    public static bool Prefix(Creature creature, ref Task __result)
    {
        try
        {
            if (creature?.Player == null)
                return true;

            if (!CanRewind(creature.Player))
                return true;

            // 非战斗（事件处死）或收尾阶段（放弃本局）的击杀交给原版，让它真死
            if (AllowRealDeath())
            {
                Godot.GD.Print("[Subaru] player death gate skipped (not in combat or run ending)");
                return true;
            }

            __result = Task.CompletedTask;

            if (Subaru.SubaruCode.Rewind.DeathReturnSpike.IsRewinding)
            {
                Godot.GD.Print($"[Subaru] player death suppressed during restore: {creature.Name}");
                return false;
            }

            Godot.GD.Print("[Subaru] player direct death intercepted -> rewind");

            var state = creature.CombatState;
            if (state != null)
                _ = Subaru.SubaruCode.Rewind.DeathReturnSpike.ForceRewindAsync(state);

            return false;
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] player death gate failed: " + e);
            return true;
        }
    }
}

/// <summary>沙坑守卫的静态状态（保留给沙坑还原段使用）。</summary>
public static class SandpitGuard
{
    /// <summary>
    /// 静默期：从死归开始，到玩家下一次回合开始为止。
    /// 这期间沙坑的「收尾演出」和「位移补间」全部跳过 —— 见 SandpitSilencePatch。
    /// </summary>
    public static bool Silent;

    public static bool Rewinding;
    public static bool Intercepted;
    public static bool ClearAfterRestore;

    public static void ResetForCombat()
    {
        Silent = false;
        Rewinding = false;
        Intercepted = false;
        ClearAfterRestore = false;
    }
}

/// <summary>
/// 沙坑收尾演出（吞人）：原版在 SandpitPower 被移除且处于 IsOff 时会
///   ① 把被盖住的单位补间到沙虫嘴边（global_position:x）
///   ② 放 finisher 音效
///   ③ 播「EatPlayerTrigger」动画（就是玩家看到的「张嘴吞人」）
///   ④ 等动画播完，直接 Kill 被吞的单位
/// 死归还原过程中我们会删掉「存档点里没有的」沙坑实例，这四条就会整套跑一遍：
/// 人是没死（Kill 被死亡闸门挡了），但贴图被拖进嘴里、动画也播了 —— 看起来就是被吞了。
/// 所以静默期内整段跳过，一个字节的演出都不播。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.SandpitPower), "AfterRemoved")]
public static class SandpitAfterRemovedSilencePatch
{
    [HarmonyPrefix]
    public static bool Prefix(MegaCrit.Sts2.Core.Models.Powers.SandpitPower __instance, ref Task __result)
    {
        // ① 回档期间：不播收尾、不补间、不判死
        if (SandpitGuard.Silent)
        {
            Godot.GD.Print("[Subaru] sandpit finisher skipped (silent rewind)");
            __result = Task.CompletedTask;
            return false;
        }

        // ② 沙坑自然归零 = 闭合 = 被吞的人当场被吃。
        //    这段演出（拖到嘴边 → finisher 音效 → EatPlayerTrigger → Kill）是**这一个方法**播的，
        //    等它内部 Kill 再拦就已经全播完了。所以要在入口处按掉：一边静音一边直接起死归。
        try
        {
            var target = __instance?.Target;
            var player = target?.Player;

            if (player != null
                && PlayerDeathGatePatch.CanRewind(player)
                && !Subaru.SubaruCode.Rewind.DeathReturnSpike.IsRewinding
                && CombatStillRunning())
            {
                SandpitGuard.Silent = true;
                __result = Task.CompletedTask;

                Godot.GD.Print("[Subaru] sandpit closed on player -> rewind scheduled (finisher skipped)");

                // 只记账、不当场回档：这一刻还在敌方回合的行动链里，当场回档会和回合机打架，
                // 回档完手牌在能量在却一张打不出去。真正还原发生在下一个玩家回合开始。
                Subaru.SubaruCode.Rewind.DeathReturnSpike.SchedulePendingDeath(player);
                return false;
            }
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] sandpit intercept failed: " + e);
        }

        return true;
    }

    /// <summary>战斗还在打吗？战斗结束/结算时的能力清空不能触发死归。</summary>
    private static bool CombatStillRunning()
    {
        var manager = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
        if (manager == null || !manager.IsInProgress)
            return false;

        return !manager.IsOverOrEnding && !manager.IsAboutToLose;
    }
}

/// <summary>
/// 沙坑位移补间：SandpitPower 层数一变就会把被盖住的单位往沙虫嘴边挪。
/// 死归期间还原层数同样会触发，所以静默期一并跳过 —— 位置由快照直接写回。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.SandpitPower), "UpdateCreaturePositions")]
public static class SandpitTweenSilencePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref Task __result)
    {
        if (!SandpitGuard.Silent)
            return true;

        Godot.GD.Print("[Subaru] sandpit position tween skipped (silent rewind)");
        __result = Task.CompletedTask;
        return false;
    }
}
