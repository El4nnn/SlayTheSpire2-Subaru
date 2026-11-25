using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 魔女瘴气的「回合末扣血」**不被缓冲 / 滑溜影响**（2026-11-20 玩家定）。
///
/// <para><b>为什么需要这个开关</b>：瘴气扣血走的是标准伤害管线
/// （<c>CreatureCmd.Damage(..., 1m, ValueProp.Unblockable, owner)</c>），
/// 而原版 <see cref="BufferPower"/> 挂在 <c>ModifyHpLostAfterOstyLate</c>、
/// 对 Owner 的**任何**掉血都返回 0（唯一豁免是 cardSource 为 BrightestFlame）；
/// 它比玩家更晚出手，所以在敌人攻击之前就把缓冲吃掉了 ——
/// 玩家看到的是「打了 E·M·M，回合末缓冲没了，敌人再打照样掉血」。
/// <c>ValueProp.Unblockable</c> 只穿格挡，不穿掉血修正，所以拦不住它。</para>
///
/// <para><b>滑溜</b>同理：它会把这次掉血压到 1 点（瘴气本来就是 1 点，数值上看不出），
/// 但还有 <c>AfterDamageReceived</c> 里的「消耗一层」——那一层是被白白吃掉的。</para>
///
/// <para><b>作用域</b>：只有 <see cref="Rewind.PrideFactorSpike.BeforeSideTurnEnd"/> 里
/// 结算瘴气的那几次 <c>CreatureCmd.Damage</c> 会开这个窗口
/// （<c>using (MiasmaBleed.Enter())</c>），其它任何掉血（含我们自己的自伤卡）行为不变。</para>
///
/// <para><b>连带改动</b>：<see cref="Rewind.Spike.CapLethalDamage"/> 里「有缓冲就不截断」
/// 和「滑溜会压到 1 点就不截断」这两条放行条件，在瘴气窗口里必须**跳过**——
/// 否则瘴气已经不吃缓冲了，1 血玩家被瘴气打死却不会触发死归。</para>
/// </summary>
internal static class MiasmaBleed
{
    private static int _depth;

    /// <summary>当前是否正在结算瘴气扣血。</summary>
    internal static bool Active => _depth > 0;

    /// <summary>开窗口（嵌套安全）：<c>using (MiasmaBleed.Enter()) { ... }</c>。</summary>
    internal static Scope Enter() => new();

    internal sealed class Scope : IDisposable
    {
        private bool _disposed;

        internal Scope()
        {
            _depth++;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_depth > 0)
                _depth--;
        }
    }
}

/// <summary>缓冲：瘴气扣血照常掉，不吃这一层。</summary>
[HarmonyPatch(typeof(BufferPower), "ModifyHpLostAfterOstyLate")]
internal static class MiasmaIgnoresBufferPatch
{
    [HarmonyPrefix]
    private static bool Prefix(decimal __1, ref decimal __result)
    {
        if (!MiasmaBleed.Active)
            return true;

        __result = __1;   // 原样返回 = 不削减
        return false;
    }
}

/// <summary>缓冲：瘴气不许把层数扣掉（否则「挡住」和「没挡住」都会掉层）。</summary>
[HarmonyPatch(typeof(BufferPower), "AfterModifyingHpLostAfterOsty")]
internal static class MiasmaDoesNotConsumeBufferPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        if (!MiasmaBleed.Active)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>滑溜：瘴气扣血不被压到 1 点。</summary>
[HarmonyPatch(typeof(SlipperyPower), "ModifyHpLostAfterOsty")]
internal static class MiasmaIgnoresSlipperyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(decimal __1, ref decimal __result)
    {
        if (!MiasmaBleed.Active)
            return true;

        __result = __1;
        return false;
    }
}

/// <summary>滑溜：瘴气不许消耗它的层数。</summary>
[HarmonyPatch(typeof(SlipperyPower), "AfterDamageReceived")]
internal static class MiasmaDoesNotConsumeSlipperyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        if (!MiasmaBleed.Active)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
