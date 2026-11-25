using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// <c>CharacterModel.MapMarker</c> 兜底。
///
/// <para><b>为什么要这个补丁</b>：地图界面初始化时走
/// <c>NMapScreen.Initialize → NMapMarker.Initialize(player) → CharacterModel.MapMarker</c>，
/// 而 <c>MapMarker</c> 的实现是 <c>AssetCache.GetCompressedTexture2D(path)</c> —— 它一旦加载失败，
/// 会把这路径记成「永久失败」，之后**每次**读都抛
/// <c>AssetLoadException: Asset previously failed to load</c>。
/// 这个异常会从 <c>NRun._Ready()</c> 里冒出来，地图建不出来，整局都进不去（玩家看到的就是卡死）。</para>
///
/// <para>2026-11-18 玩家日志里的实证：一次显存/内存分配失败
/// （<c>alloc_static mem is null</c>）把
/// <c>Subaru/images/charui/map_marker_char_name.png</c> 搞成永久失败，随后就抛在上面这条链上。</para>
///
/// <para><b>做法</b>：给这个 getter 挂 finalizer —— 出异常就吞掉，退回原版（铁甲战士）的地图标记。
/// 退回的图标再失败就让结果保持 null（地图上看不到自己，但不会把整局卡死）。</para>
/// <para><b>2026-11-19 修：这个补丁一直没生效</b>。原来写的是
/// <c>[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.MapMarker))]</c> ——
/// <c>nameof</c> 展开成字符串 "MapMarker"，而 Harmony 默认按**方法名**去找，
/// 属性在 IL 里叫 <c>get_MapMarker</c>，于是每次都报
/// <c>Undefined target method for patch method ... Finalizer</c> 被跳过（日志里那条 WARN 就是它）。
/// 现在直接写 getter 名。</para>
/// </summary>
[HarmonyPatch(typeof(CharacterModel), "get_MapMarker")]
public static class MapMarkerFallbackPatch
{
    /// <summary>原版就有的地图标记，作为兜底。</summary>
    private const string VanillaMarker = "res://images/packed/map/icons/map_marker_ironclad.png";

    private static bool _warned;

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception, ref CompressedTexture2D __result)
    {
        if (__exception == null)
            return null;

        try
        {
            var fallback = ResourceLoader.Load<CompressedTexture2D>(
                VanillaMarker, cacheMode: ResourceLoader.CacheMode.Replace);

            if (fallback != null)
            {
                if (!_warned)
                {
                    _warned = true;
                    GD.PushWarning("[Subaru] map marker failed to load (" + __exception.GetType().Name
                                   + "), using vanilla marker instead");
                }

                __result = fallback;
                return null;   // 吞掉异常：地图界面照常构建
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] map marker fallback failed: " + e.Message);
        }

        // 连兜底都拿不到：返回 null，至少不让异常把 NRun._Ready 打断
        __result = null;
        return null;
    }
}
