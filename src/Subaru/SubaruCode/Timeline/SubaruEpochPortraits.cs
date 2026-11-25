using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 时间线上的节点图标：解锁后用小图，未解锁时用「原立绘模糊 + 灰度」的版本。
///
/// 原版 <c>EpochModel.Portrait</c> 读的是 <c>PackedPortraitPath</c>（图集里的小图），
/// mod 节点没有图集资源，于是槽位一直显示占位符。这里直接接管这个属性：
///   - 已获得 → <c>timeline/epoch_portraits/&lt;id&gt;.png</c>
///   - 未获得 → <c>timeline/epoch_portraits/&lt;id&gt;_locked.png</c>（构建前预生成的模糊灰度图）
/// 槽位自己还会按状态叠一层 saturate=0 / value&lt;1 的着色，效果和原版未解锁节点一致。
/// </summary>
public static class SubaruEpochPortraits
{
    /// <summary>我们自己的节点 id（和 <see cref="SubaruEpochGate"/> 保持一致）。</summary>
    public static readonly string[] EpochIds =
    [
        SubaruEpochGate.CallEpochId,
        SubaruEpochGate.RewindEpochId,
        SubaruEpochGate.WitchEndEpochId,
        SubaruEpochGate.LoveEpochId,
        SubaruEpochGate.SwordWolfEpochId,
        SubaruEpochGate.CollapseEpochId,
        SubaruEpochGate.GluttonyEpochId,
        SubaruEpochGate.CalamityEpochId,
    ];

    public static string ArtPath(string epochId) =>
        $"timeline/epoch_portraits/{epochId.ToLowerInvariant()}.png".ImagePath();

    public static string LockedPath(string epochId) =>
        $"timeline/epoch_portraits/{epochId.ToLowerInvariant()}_locked.png".ImagePath();

    public static bool IsOurs(string epochId)
    {
        if (string.IsNullOrEmpty(epochId))
            return false;

        foreach (var id in EpochIds)
        {
            if (id == epochId)
                return true;
        }

        return false;
    }

    public static bool IsObtained(string epochId)
    {
        try
        {
            var progress = SaveManager.Instance == null ? null : SaveManager.Instance.Progress;
            return progress != null && progress.IsEpochObtained(epochId);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 解锁弹窗 / 节点详情读的是 <c>RealPortraitPath</c>（大图），
/// mod 节点的默认值是原版图集路径 → 加载失败 → 显示「占位符」。
/// 这里接管成自己的立绘。
/// </summary>
[HarmonyPatch(typeof(EpochModel), "get_RealPortraitPath")]
public static class SubaruEpochRealPortraitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EpochModel __instance, ref string __result)
    {
        try
        {
            var id = __instance == null ? null : __instance.Id;
            if (!SubaruEpochPortraits.IsOurs(id))
                return true;

            __result = SubaruEpochPortraits.ArtPath(id);
            return false;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] epoch real portrait path failed: " + e.Message);
            return true;
        }
    }
}

/// <summary>占位图也指向自己的模糊灰度版，别再出现「占位符」字样。</summary>
[HarmonyPatch(typeof(EpochModel), "get_PlaceholderPortraitPath")]
public static class SubaruEpochPlaceholderPortraitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EpochModel __instance, ref string __result)
    {
        try
        {
            var id = __instance == null ? null : __instance.Id;
            if (!SubaruEpochPortraits.IsOurs(id))
                return true;

            __result = SubaruEpochPortraits.LockedPath(id);
            return false;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] epoch placeholder path failed: " + e.Message);
            return true;
        }
    }
}

/// <summary>把 <c>PackedPortraitPath</c>（时间线上的小图）换成 mod 自己的图；未解锁时给模糊灰度版。</summary>
[HarmonyPatch(typeof(EpochModel), "get_PackedPortraitPath")]
public static class SubaruEpochPortraitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EpochModel __instance, ref string __result)
    {
        try
        {
            var id = __instance == null ? null : __instance.Id;
            if (!SubaruEpochPortraits.IsOurs(id))
                return true;

            if (!SubaruEpochPortraits.IsObtained(id))
            {
                var locked = SubaruEpochPortraits.LockedPath(id);
                if (ResourceLoader.Exists(locked))
                {
                    __result = locked;
                    return false;
                }
            }

            __result = SubaruEpochPortraits.ArtPath(id);
            return false;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] epoch portrait path failed: " + e.Message);
            return true;
        }
    }
}
