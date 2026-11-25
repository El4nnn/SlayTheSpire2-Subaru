using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 死归交回合时跳过敌方行动（2026-11-21 用户定）。
///
/// <para><b>为什么需要</b>：玩家在自己回合内被判死时，我们会延迟 1 秒把回合交出去
/// （<see cref="DeathReturnSpike.ForceEndTurnAsync"/>），但那一回合马上就要被还原 ——
/// 让敌人真打一轮既拖时间，又会出现「昴已经倒下、敌人还在打」的画面。</para>
///
/// <para><b>为什么切在这里，而不是 <c>CombatManager.ExecuteEnemyTurn</c></b>：
/// 后者的方法体末尾负责调 <c>EndEnemyTurn</c>，整个跳过会让敌方回合永远结束不了（软锁）。
/// 切 <see cref="Creature.TakeTurn"/> 只跳「每个敌人的行动」，<c>EndEnemyTurn</c> 照常执行，
/// 回合机不会断链。</para>
///
/// <para>标记的清理在 <see cref="DeathReturnSpike.OnPlayerTurnStart"/> 与
/// <see cref="DeathReturnSpike.ResetForCombat"/>，避免泄漏到下一场战斗。</para>
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.TakeTurn))]
internal static class SkipEnemyTurnPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Creature __instance, ref Task __result)
    {
        if (!DeathReturnSpike.SkipEnemyTurn)
            return true;

        // 只跳敌人；玩家/宠物照常（它们不走这条，但防御性判断一下）
        if (__instance == null || !__instance.IsEnemy)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
