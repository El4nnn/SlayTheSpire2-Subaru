using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 把原版[缓慢]（<see cref="SlowPower"/>）改成**可叠层**，不设上限。
///
/// 原版算式（IL 实证）：
///   倍率 = 1 + 0.1 × SlowAmount
///   SlowAmount 由 <c>AfterCardPlayed</c> 每打出一张牌 +1，
///   敌人回合开始时在 <c>AfterSideTurnStart</c> 里清零。
///   `Amount`（层数）**完全没有参与计算** —— 所以打第二张「蓝色闪电」没有任何收益。
///
/// 改成：倍率 = 1 + 0.1 × SlowAmount × Amount。
/// 层数能叠起来是因为 <c>PowerCmd.Apply</c> 命中已有 Counter 能力时走 `ModifyAmount`
/// （同类型叠加层数），所以打第二张 → Amount 2。
///
/// 只改原版、不新造能力：自带[缓慢]的怪（原版 <c>BygoneEffigy</c>）挂的层数是 1，
/// 走 `stacks &lt;= 1` 分支直接 return，行为和改动前完全一致。
/// </summary>
[HarmonyPatch(typeof(SlowPower), nameof(SlowPower.ModifyDamageMultiplicative))]
public static class SlowPowerStackPatch
{
    /// <summary>每张牌、每层：+10%。</summary>
    private const decimal PerCardPerStack = 0.1m;

    [HarmonyPostfix]
    public static void Postfix(SlowPower __instance, ref decimal __result)
    {
        try
        {
            // 原版「目标不是自己」或「不是攻击牌」时返回 1 —— 那种情况不该被改
            if (__result == 1m)
                return;

            var stacks = __instance.Amount;
            if (stacks <= 1)
                return;

            __result = 1m + PerCardPerStack * SlowAmount(__instance) * stacks;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] slow stack multiplier failed: " + e);
        }
    }

    /// <summary>本回合已打出的牌数（原版的动态变量）。</summary>
    private static decimal SlowAmount(SlowPower power) => power.DynamicVars["SlowAmount"].BaseValue;
}

/// <summary>
/// [缓慢]图标上的数字 = 当前总增伤百分比 = SlowAmount × 10 × 层数。
/// 原版只显示 SlowAmount × 10，叠层后会低报，所以一起改。
/// </summary>
[HarmonyPatch(typeof(SlowPower), nameof(SlowPower.DisplayAmount), MethodType.Getter)]
public static class SlowPowerStackDisplayPatch
{
    [HarmonyPostfix]
    public static void Postfix(SlowPower __instance, ref int __result)
    {
        try
        {
            if (__instance.Amount > 1)
                __result *= __instance.Amount;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] slow stack display failed: " + e);
        }
    }
}
