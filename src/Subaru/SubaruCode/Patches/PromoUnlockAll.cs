using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 宣发用：一次性把菜月昴的 7 个时间线节点强制置为「已完成」。
///
/// **开关是一个文件，不是编译常量** —— 只有当 <c>user://subaru_promo.txt</c> 存在时才生效，
/// 所以发出去的 dll 里带着这段代码也是惰性的，玩家不会莫名其妙全解锁。
/// 要启用就在 <c>%APPDATA%\SlayTheSpire2\</c> 下建一个空的 <c>subaru_promo.txt</c>；删掉即恢复。
///
/// 走的是官方接口 <c>SaveManager.ObtainEpochOverride(id, EpochState.Revealed)</c>
/// （RitsuLib 的时间线扩展也用这个），比直接改存档可靠：直接改存档会被游戏在加载时按槽位可见性校正回去。
/// </summary>
public static class PromoUnlockAll
{
    private const string FlagPath = "user://subaru_promo.txt";

    private static bool _applied;

    public static bool Enabled
    {
        get
        {
            try
            {
                return Godot.FileAccess.FileExists(FlagPath);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Apply(string source)
    {
        if (_applied || !Enabled)
            return;

        try
        {
            var save = SaveManager.Instance;
            if (save == null)
                return;

            _applied = true;

            foreach (var id in SubaruEpochPortraits.EpochIds)
            {
                try
                {
                    save.ObtainEpochOverride(id, EpochState.Revealed);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"[Subaru] PROMO: reveal {id} failed: {e.Message}");
                }
            }

            save.SaveProgressFile();
            GD.Print($"[Subaru] PROMO: forced {SubaruEpochPortraits.EpochIds.Length} epochs to revealed ({source})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] PROMO unlock failed: " + e.Message);
        }
    }
}

/// <summary>进度加载完成时套用（每次启动/切换档案都会走）。</summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.InitProgressData))]
public static class PromoUnlockOnInitPatch
{
    [HarmonyPostfix]
    public static void Postfix() => PromoUnlockAll.Apply("progress init");
}

/// <summary>切档案 / 需要解锁状态时也会走到这里，再兜一次。</summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.GenerateUnlockStateFromProgress))]
public static class PromoUnlockOnLoadPatch
{
    [HarmonyPostfix]
    public static void Postfix() => PromoUnlockAll.Apply("unlock state generated");
}
