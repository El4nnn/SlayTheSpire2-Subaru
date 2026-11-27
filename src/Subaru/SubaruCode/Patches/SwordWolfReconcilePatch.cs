using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 读档后立刻重算一次「帝国的剑狼」计数（含老玩家补偿）。
///
/// <para>挂在 <see cref="SaveManager.InitProgressData"/> 之后：每次启动、切档案都会走，
/// 所以玩家的历史首领战绩能在开局前就被算出来，不需要等下一场战斗。</para>
/// </summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.InitProgressData))]
public static class SwordWolfReconcilePatch
{
    [HarmonyPostfix]
    public static void Postfix() => SwordWolfProgress.Reconcile("progress init");
}
