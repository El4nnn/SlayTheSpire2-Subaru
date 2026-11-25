using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace Subaru.SubaruCode.Compatibility;

/// <summary>
/// 把原版存档单向迁移成 modded 存档（只复制，永不回写）。
///
/// 原版自己在首次进入 modded 模式时做过一次整份复制，但条件是
/// 「<c>modded/</c> 目录完全不存在」；只要这个目录已经存在（以前开过别的 mod、
/// 或者某次启动提前建了它），就会走 `Modded saves exist. Skipping first-time save copy`，
/// 玩家看到的就是一个全新的进度。
///
/// 所以这里补一次：对每个 profile，如果 modded 那份 progress.save 不存在、
/// 或者存在但一个 epoch 都没有（全新档），而原版那份有内容，就复制过去。
/// 已有的 modded 进度绝不覆盖。
/// </summary>
public static class VanillaSaveMigration
{
    private const string ProgressFile = "progress.save";
    private const string ModdedDir = "modded";

    public static void RunOnce()
    {
        try
        {
            var root = ProjectSettings.GlobalizePath("user://");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            var migrated = 0;

            foreach (var accountDir in AccountDirs(root))
            {
                foreach (var vanillaProfile in Directory.GetDirectories(accountDir, "profile*"))
                {
                    if (TryMigrate(accountDir, Path.GetFileName(vanillaProfile)))
                        migrated++;
                }
            }

            if (migrated > 0)
                GD.Print($"[Subaru] vanilla save migration done: {migrated} profile(s)");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] vanilla save migration failed: " + e.Message);
        }
    }

    /// <summary>
    /// 找「账号目录」＝直接包含 profile* 子目录的那一层。
    /// 实际布局是 <c>user://steam/&lt;好友码&gt;/profile1</c>，
    /// 没有 Steam 时可能是 <c>user://&lt;账号&gt;/profile1</c>，所以两层都探一遍。
    /// </summary>
    private static IEnumerable<string> AccountDirs(string root)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);

            if (IsIgnored(name))
                continue;

            if (HasProfiles(dir))
            {
                yield return dir;
                continue;
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (IsIgnored(Path.GetFileName(sub)))
                    continue;

                if (HasProfiles(sub))
                    yield return sub;
            }
        }

        static bool IsIgnored(string name) =>
            name.Equals(ModdedDir, StringComparison.OrdinalIgnoreCase)
            || name.Equals("logs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mod_data", StringComparison.OrdinalIgnoreCase);

        static bool HasProfiles(string dir) => Directory.GetDirectories(dir, "profile*").Length > 0;
    }

    private static bool TryMigrate(string accountDir, string profile)
    {
        try
        {
            var source = Path.Combine(accountDir, profile, "saves", ProgressFile);
            if (!File.Exists(source))
                return false;

            var sourceEpochs = CountEpochs(source);
            if (sourceEpochs <= 0)
                return false;

            var target = Path.Combine(accountDir, ModdedDir, profile, "saves", ProgressFile);

            if (File.Exists(target) && CountEpochs(target) > 0)
                return false;

            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            File.Copy(source, target, true);
            GD.Print($"[Subaru] migrated vanilla progress -> modded ({profile}, {sourceEpochs} epochs)");
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] migrate {profile} failed: " + e.Message);
            return false;
        }
    }

    /// <summary>数一下存档里有几条 epoch；解析失败返回 -1（当作「有内容」，不覆盖）。</summary>
    private static int CountEpochs(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (document.RootElement.TryGetProperty("epochs", out var epochs)
                && epochs.ValueKind == JsonValueKind.Array)
            {
                return epochs.GetArrayLength();
            }

            return 0;
        }
        catch
        {
            return -1;
        }
    }
}
