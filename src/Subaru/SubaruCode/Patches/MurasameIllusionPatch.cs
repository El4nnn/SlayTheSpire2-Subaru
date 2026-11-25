using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Patches;

internal static class MurasameCheck
{
    /// <summary>这场战斗的玩家身上有没有「邪剑·村雨」。</summary>
    public static bool OwnerHasMurasame(Creature creature)
    {
        try
        {
            var player = creature?.CombatState?.Players?.FirstOrDefault();
            return player != null && player.Relics.Any(r => r is CursedSwordMurasame);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 邪剑·村雨 剥夺[幻影]：**不摘 power**，改成拦复活。
///
/// 之前的做法是 AfterApplied 时把 IllusionPower 移除，结果踩到原版的坑：
/// 寄生惧魔的动画回调是 <c>return !GetPower&lt;IllusionPower&gt;().IsReviving;</c>，**没有判空**，
/// power 被摘掉后一挨打就 NullReferenceException → PlayCardAction 失败 → 战斗卡死。
/// 现在保留 power（动画回调拿得到对象），只把「复活」这一步拦掉。
/// </summary>
[HarmonyPatch(typeof(IllusionPower), nameof(IllusionPower.AfterDeath))]
public static class MurasameIllusionRevivePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature creature, ref System.Threading.Tasks.Task __result)
    {
        if (!MurasameCheck.OwnerHasMurasame(creature))
            return true;

        Godot.GD.Print($"[Subaru] murasame: blocked illusion revive on {creature?.Name}");

        // 关键：AfterDeath 是 async Task 方法，跳过时必须回一个已完成 Task。
        // 直接 return false 会让调用方（Hook.AfterDeath → TaskHelper.WhenAny）拿到 null，
        // 抛 ArgumentNullException("task1")，整张牌卡在半空。
        __result = System.Threading.Tasks.Task.CompletedTask;
        return false;
    }
}

/// <summary>
/// 既然不复活了，就让尸体正常从战斗里移除（原版靠幻影复活，会刻意留着尸体）。
/// </summary>
[HarmonyPatch(typeof(IllusionPower), nameof(IllusionPower.ShouldCreatureBeRemovedFromCombatAfterDeath))]
public static class MurasameIllusionRemovalPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature creature, ref bool __result)
    {
        if (!MurasameCheck.OwnerHasMurasame(creature))
            return true;

        __result = true;
        return false;
    }
}
