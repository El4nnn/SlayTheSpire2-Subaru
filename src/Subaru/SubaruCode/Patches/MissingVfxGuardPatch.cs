using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 特效丢失兜底：<c>VfxCmd.PlayVfx</c> 在场景加载失败时会直接空引用。
///
/// <para><b>为什么需要</b>：2026-11-18 玩家日志里，一次资源缓存污染
/// （<c>Invalid Task ID</c> → <c>alloc_static mem is null</c> → 之后所有贴图读取 0 字节）
/// 让 <c>res://scenes/vfx/vfx_attack_lightning.tscn</c> 永久加载失败；
/// 于是 <c>TheArchitect.WinRun() → AnimArchitectAttackIfNecessary → VfxCmd.PlayOnCreature</c>
/// 抛 <c>NullReferenceException</c>，**通关结算整个中断**，玩家就卡死在建筑师那里。</para>
///
/// <para>这里只吞 <see cref="NullReferenceException"/>：特效播不出来是小事，
/// 但让它把「胜利结算」这条链炸掉就不可接受了。其它类型的异常照原样抛。</para>
/// </summary>
[HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayVfx))]
public static class MissingVfxGuardPatch
{
    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            GD.PushWarning("[Subaru] a vfx failed to load and PlayVfx null-referenced; skipping it"
                           + " (ending/combat flow kept alive)");
            return null;
        }

        return __exception;
    }
}
