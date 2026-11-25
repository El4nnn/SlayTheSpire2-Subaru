using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「自动打出」不受怪物给的出牌次数限制（<see cref="SlothPower"/>：每回合不能打出超过 N 张）。
///
/// <para>用途：契约·贝蒂在回合结束时自动打出[临时迷尼亚]。这是贝蒂自己的效果，
/// 不应该占玩家本回合的出牌名额，也不该因为吃了[怠惰]就打不出来。</para>
///
/// <para>用法：<c>using (AutoPlayLimitBypass.Allow()) { await CardCmd.AutoPlay(...); }</c>
/// —— 和 <see cref="MiasmaGuard.Allow"/> 同一套作用域写法。</para>
/// </summary>
public static class AutoPlayLimitBypass
{
    [ThreadStatic]
    private static int _depth;

    public static bool Active => _depth > 0;

    public static Scope Allow() => new();

    public readonly struct Scope : IDisposable
    {
        public Scope() => _depth++;

        public void Dispose() => _depth = Math.Max(0, _depth - 1);
    }
}

/// <summary>怠惰的「还能不能出牌」判定：处于自动打出窗口时直接放行。</summary>
[HarmonyPatch(typeof(SlothPower), nameof(SlothPower.ShouldPlay))]
public static class SlothAutoPlayBypassPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool __result)
    {
        if (!AutoPlayLimitBypass.Active)
            return true;

        __result = true;
        return false;
    }
}
