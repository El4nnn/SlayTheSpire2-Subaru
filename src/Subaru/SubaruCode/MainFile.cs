using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Subaru.SubaruCode;

//You're recommended but not required to keep all your code in this package and all your assets in the Subaru folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "Subaru"; //Used for resource filepath
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 记下主线程号：第三方求解器在线程池线程里模拟战斗时也会调到我们的 Hook，
        // 那些调用不能记真账、不能碰 UI。见 MainThreadGuard 的注释。
        Subaru.SubaruCode.Rewind.MainThreadGuard.Capture();

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);

        // 原版存档 → modded 存档的一次性单向迁移
        // （原版只在 modded 目录「完全不存在」时才自动复制，目录已存在就跳过）
        Subaru.SubaruCode.Compatibility.VanillaSaveMigration.RunOnce();

        // 时间线节点：注入自定义 epoch（原版的 EpochModelSubtypes 是硬编码表）
        // 放在 PatchAll 之前，避免某个补丁失败把注册也带崩
        Subaru.SubaruCode.Timeline.SubaruTimeline.Register();

        // RitsuLib 的「模组设置」页：侧边栏里显示本模组的页面
        Subaru.SubaruCode.Settings.SubaruModSettings.Register();

        Harmony harmony = new(ModId);

        // 逐个补丁类单独应用，不再用 PatchAll：
        // PatchAll 只要有一个补丁和目标方法对不上（游戏小版本更新改个形参名就会），
        // 就整体抛异常、把 mod 初始化带崩（0.113.1 的 CardPileCmd.Add 就是这种情况）。
        // 现在改成「哪个补丁挂了就只跳过那一个」，其它功能照常。
        ApplyPatchesIndividually(harmony, assembly);
    }

    private static void ApplyPatchesIndividually(Harmony harmony, Assembly assembly)
    {
        var applied = 0;
        var skipped = 0;

        foreach (var type in assembly.GetTypes())
        {
            // 注意：C# 的 static class 在 IL 里是 abstract + sealed，
            // Type.IsAbstract 对它是 true —— 不能拿 IsAbstract 过滤「非补丁类」，
            // 否则所有 `public static class XxxPatch` 都会被跳过（0.2.37 就栽在这）。
            if (!type.IsClass)
                continue;

            try
            {
                // 用复数版：一个类上挂两个 [HarmonyPatch] 是合法的，单数版 GetCustomAttribute
                // 遇到多个会抛 AmbiguousMatchException（2026-11-19 就是这样把整个补丁循环带崩的）。
                // 判断也放进 try 里：反射层面的任何异常都只跳过这一个类，不影响后面的补丁。
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                    continue;

                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception e)
            {
                skipped++;
                Logger.Warn($"[Harmony] patch class skipped: {type.Name} — {e.GetBaseException().Message}");
                GD.PushWarning($"[Subaru] patch class skipped: {type.Name} — {e.GetBaseException().Message}");
            }
        }

        Logger.Info($"[Harmony] patch classes applied={applied}, skipped={skipped}");
        GD.Print($"[Subaru] patch classes applied={applied}, skipped={skipped}");
    }

}
