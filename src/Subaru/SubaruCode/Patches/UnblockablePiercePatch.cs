using HarmonyLib;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「不可被抵挡」伤害的穿透规则（2026-11-18 玩家定）。
///
/// <para><b>判定</b>：<c>Unblockable</c> + <c>Move</c> + 没有 <c>Unpowered</c>，
/// <b>而且是玩家这一侧打出的</b> —— 敌人打过来的无格挡伤害不吃这套规则，
/// 照样被玩家的无实体 / 滑溜 / 硬化外壳挡。<br/>
/// 中毒那一跳是 <c>Unblockable | Unpowered</c>（不带 Move），同样不算，
/// 否则等于顺手把中毒 Buff 了一大截。</para>
///
/// <para><b>要吃的</b>：力量（<c>StrengthPower</c>）、易伤（<c>VulnerablePower</c>）、
/// 附魔（锋利 / 本能）、本 mod 的增伤（<c>DamageUpPower</c> / <c>WitchFragrancePower</c>）——
/// 这些都以 <c>IsPoweredAttack</c> 或「无差别」为前提，带 Move 就自动吃到了，不需要额外补丁。</para>
///
/// <para><b>不吃的</b>：虚弱、缩小、无实体、难以杀灭、滑溜、硬化外壳。
/// 前四个在引擎里 gate 的是 <c>IsPoweredAttack</c> 或「一律生效」，
/// 所以只能在这里按 props 把它们对不可被抵挡伤害的钩子短路掉；
/// 后两个（滑溜 / 硬化外壳）除了改血量，还有「消耗层数 / 累计池子」的记账钩子，
/// 一并跳过，做到真正「完全不交互」。</para>
/// </summary>
internal static class UnblockablePierce
{
    /// <summary>无视格挡 + 卡牌/招式伤害 + 不是「固定伤害」。</summary>
    internal static bool IsUnblockableHit(ValueProp props)
        => props.HasFlag(ValueProp.Unblockable)
           && props.HasFlag(ValueProp.Move)
           && !props.HasFlag(ValueProp.Unpowered);

    /// <summary>玩家侧打出的不可被抵挡伤害 —— 只有这种才穿透防御效果。</summary>
    internal static bool IsPiercing(ValueProp props, Creature dealer)
        => IsUnblockableHit(props) && dealer != null && dealer.Side == CombatSide.Player;
}

/// <summary>虚弱 ×0.75 → 对不可被抵挡伤害不生效。</summary>
[HarmonyPatch(typeof(WeakPower), "ModifyDamageMultiplicative")]
public static class UnblockableIgnoresWeakPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ValueProp __2, Creature __3, ref decimal __result)
    {
        if (!UnblockablePierce.IsPiercing(__2, __3))
            return true;

        __result = 1m;
        return false;
    }
}

/// <summary>缩小（减伤%）→ 对不可被抵挡伤害不生效。</summary>
[HarmonyPatch(typeof(ShrinkPower), "ModifyDamageMultiplicative")]
public static class UnblockableIgnoresShrinkPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ValueProp __2, Creature __3, ref decimal __result)
    {
        if (!UnblockablePierce.IsPiercing(__2, __3))
            return true;

        __result = 1m;
        return false;
    }
}

/// <summary>无实体（伤害上限压成 1）→ 不可被抵挡伤害无视它。</summary>
/// <remarks>
/// 2026-11-20 事故（玩家：打出「极彩色·夜鸣」拿到 1 层无实体后，最优骑士·尤利乌斯的伤害立刻变 0）：
/// 这个前缀原来直接 `__result = -1m` 当「没有上限」，但引擎的「没有上限」是
/// **<c>decimal.MaxValue</c>**，见 <c>IntangiblePower.ModifyDamageCap</c> / <c>HardToKillPower.ModifyDamageCap</c>
/// 在 <c>target != Owner</c> 时的 IL：<c>newobj decimal(lo:-1, mid:-1, hi:-1, sign:false, scale:0)</c>。
/// 而 <c>Hook.ModifyDamageInternal</c> 的上限相位是
/// <c>cap = min(所有返回值); if (damage &gt; c) damage = c;</c> ——
/// 返回 -1 等于把伤害**压成 -1**，再被下游钳到 0：卡面预览和实际伤害一起变 0。
/// <para>另外这里必须只处理「无实体挂在**受击者**身上」那一次：挂在攻击者身上时，
/// 原版自己就会返回无上限，插进来只会添乱（就是上面那次事故的触发条件）。</para>
/// </remarks>
[HarmonyPatch(typeof(IntangiblePower), "ModifyDamageCap")]
public static class UnblockableIgnoresIntangiblePatch
{
    [HarmonyPrefix]
    public static bool Prefix(IntangiblePower __instance, Creature __0, ValueProp __1, Creature __2,
                              ref decimal __result)
    {
        if (!ReferenceEquals(__instance.Owner, __0))
            return true;

        if (!UnblockablePierce.IsPiercing(__1, __2))
            return true;

        __result = decimal.MaxValue; // 引擎约定：MaxValue = 没有上限（不是 -1）
        return false;
    }
}

/// <summary>难以杀灭（伤害上限）→ 不可被抵挡伤害无视它。</summary>
/// <remarks>与<a cref="UnblockableIgnoresIntangiblePatch"/>同一个坑，见那边的说明。</remarks>
[HarmonyPatch(typeof(HardToKillPower), "ModifyDamageCap")]
public static class UnblockableIgnoresHardToKillPatch
{
    [HarmonyPrefix]
    public static bool Prefix(HardToKillPower __instance, Creature __0, ValueProp __1, Creature __2,
                              ref decimal __result)
    {
        if (!ReferenceEquals(__instance.Owner, __0))
            return true;

        if (!UnblockablePierce.IsPiercing(__1, __2))
            return true;

        __result = decimal.MaxValue;
        return false;
    }
}

/// <summary>滑溜：把这一击的掉血压到 1 点 → 对不可被抵挡伤害不生效。</summary>
[HarmonyPatch(typeof(SlipperyPower), "ModifyHpLostAfterOsty")]
public static class UnblockableIgnoresSlipperyPatch
{
    [HarmonyPrefix]
    public static bool Prefix(decimal __1, ValueProp __2, Creature __3, ref decimal __result)
    {
        if (!UnblockablePierce.IsPiercing(__2, __3))
            return true;

        __result = __1; // 原样返回 = 不削减
        return false;
    }
}

/// <summary>滑溜：被命中后消耗层数 → 不可被抵挡伤害不消耗它。</summary>
/// <remarks>
/// <b>必须给 <c>__result</c> 赋值</b>：原方法是 <c>async Task</c>，Harmony 跳过原方法时
/// 没人写 <c>__result</c> 就返回 <c>null</c>；引擎的 <c>Hook.AfterDamageReceived</c> 拿回来立刻
/// <c>.GetAwaiter()</c>（IL：<c>callvirt AfterDamageReceived; callvirt GetAwaiter</c>，中间没有 null 检查）
/// → <c>NullReferenceException</c>。2026-11-22 事故见 <see cref="UnblockableIgnoresHardenedShellPoolPatch"/>。
/// </remarks>
[HarmonyPatch(typeof(SlipperyPower), "AfterDamageReceived")]
public static class UnblockableIgnoresSlipperyConsumePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ValueProp __3, Creature __4, ref Task __result)
    {
        if (!UnblockablePierce.IsPiercing(__3, __4))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>硬化外壳：按剩余额度削减掉血 → 对不可被抵挡伤害不生效。</summary>
[HarmonyPatch(typeof(HardenedShellPower), "ModifyHpLostBeforeOstyLate")]
public static class UnblockableIgnoresHardenedShellPatch
{
    [HarmonyPrefix]
    public static bool Prefix(decimal __1, ValueProp __2, Creature __3, ref decimal __result)
    {
        if (!UnblockablePierce.IsPiercing(__2, __3))
            return true;

        __result = __1;
        return false;
    }
}

/// <summary>硬化外壳：把受到的伤害记进额度池 → 不可被抵挡伤害不占额度。</summary>
/// <remarks>
/// <b>2026-11-22 事故（牌悬空、不进弃牌堆）</b>：这里原来只 <c>return false</c>、没有 <c>ref Task __result</c>。
/// 原方法 <c>HardenedShellPower.AfterDamageReceived(...) -&gt; Task</c> 是 async，Harmony 跳过时
/// <c>__result</c> 保持默认值 <c>null</c>；<c>Hook.AfterDamageReceived</c> 的循环体是
/// <c>callvirt AfterDamageReceived</c> → <c>callvirt GetAwaiter</c>（IL 实证，中间无 null 检查），
/// 于是对 null 调 <c>GetAwaiter()</c> → <c>NullReferenceException</c> 抛在
/// <c>Hook.AfterDamageReceived</c> 自己这一帧里（与游戏日志的栈完全一致）。
/// 后果：<c>DamageCmd.Attack(...).Execute()</c> 整个任务炸掉 → <c>OnPlayCard</c> 抛异常 →
/// 出牌动作带异常结束，引擎不再把这张牌移进弃牌堆 → 牌停在屏幕上。
/// <para>触发组合（和玩家报告一致）：<b>不可被抵挡的攻击</b>（<c>IsPiercing</c> 为真，才会跳过原方法）
/// + 目标带<b>硬化外壳</b>（鬼祟珊瑚群 <c>AfterAddedToRoom</c> 就给 20 层）。普通攻击不会进来，所以只有这一张「强欲司教」翻车。</para>
/// </remarks>
[HarmonyPatch(typeof(HardenedShellPower), "AfterDamageReceived")]
public static class UnblockableIgnoresHardenedShellPoolPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ValueProp __3, Creature __4, ref Task __result)
    {
        if (!UnblockablePierce.IsPiercing(__3, __4))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
