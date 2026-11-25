using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace SubaruVariants;

/// <summary>
/// 多版本引导器。游戏加载的是本程序集（根目录的 Subaru.dll），
/// 它按当前游戏版本挑选 lib\release 或 lib\beta 下真正的 Subaru.dll 并加载、初始化。
///
/// 设计参考 naiwa（工坊 3765203639）：根目录一个几十 KB 的 shim，
/// 真正的实现按版本各放一份，资源 pck 共用。
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class Bootstrap : Node
{
    private const string ModId = "Subaru";
    private const string ManifestName = "Subaru-variants.manifest";

    public static void Initialize()
    {
        try
        {
            var modDir = GetModDirectory();
            GD.Print($"[Subaru.Variants] bootstrap: mod dir = {modDir}");

            var variants = ReadVariants(modDir);
            if (variants.Count == 0)
            {
                GD.PushError("[Subaru.Variants] 没有可用的变体，检查 " + ManifestName);
                return;
            }

            var gameVersion = ReadGameVersion();
            GD.Print($"[Subaru.Variants] game version = {gameVersion}");

            var chosen = SelectVariant(variants, gameVersion);
            if (chosen == null)
            {
                GD.PushError("[Subaru.Variants] 找不到匹配当前版本的变体：" + gameVersion);
                return;
            }

            var dllPath = Path.Combine(modDir, chosen.Directory.Replace('/', Path.DirectorySeparatorChar),
                chosen.Assembly);
            if (!File.Exists(dllPath))
            {
                GD.PushError("[Subaru.Variants] 变体文件缺失: " + dllPath);
                return;
            }

            GD.Print($"[Subaru.Variants] loading variant '{chosen.CompatTarget}' -> {dllPath}");

            // 关键：必须加载到「BaseLib 所在的那个加载上下文」里。
            // 加载到 Default 会导致 BaseLib 出现两份副本，基类身份对不上，
            // 于是所有 override 都报 "does not have an implementation"。
            var context = FindBaseLibContext();
            GD.Print($"[Subaru.Variants] load context: {context.Name}");

            InstallResolver(context, modDir);

            var assembly = context.LoadFromAssemblyPath(dllPath);

            // 先确认能拿到初始化方法，再把这个程序集登记给 Mod；
            // 拿不到就整体放弃，绝不让游戏带着半个 Mod 启动。
            var initializer = FindInitializer(assembly);
            if (initializer == null)
            {
                GD.PushError("[Subaru.Variants] 变体程序集里没有可用的 [ModInitializer]，本 Mod 已跳过");
                return;
            }

            Assoc(assembly);
            GD.Print($"[Subaru.Variants] invoking initializer {initializer.DeclaringType?.FullName}.{initializer.Name}");
            initializer.Invoke(null, null);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru.Variants] bootstrap failed: " + e);
        }
    }

    private static string GetModDirectory()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(location))
            return Path.GetDirectoryName(location);

        // 极端情况下的兜底：用 Godot 的 mod 路径约定
        var fromGodot = ProjectSettings.GlobalizePath($"res://mods/{ModId}");
        return Directory.Exists(fromGodot) ? fromGodot : Directory.GetCurrentDirectory();
    }

    /// <summary>找 BaseLib 所在的加载上下文；找不到就退回 Default。</summary>
    private static AssemblyLoadContext FindBaseLibContext()
    {
        try
        {
            foreach (var name in new[] { "BaseLib", "STS2-RitsuLib.Runtime", "STS2-RitsuLib" })
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                if (asm == null)
                    continue;

                var ctx = AssemblyLoadContext.GetLoadContext(asm);
                if (ctx != null)
                {
                    GD.Print($"[Subaru.Variants] context taken from '{name}'");
                    return ctx;
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru.Variants] 找加载上下文失败: " + e.Message);
        }

        return AssemblyLoadContext.Default;
    }

    /// <summary>
    /// 变体 dll 里引用的 RitsuLib / BaseLib 由别的 mod 加载，可能不在同一个上下文里。
    /// 加载前先挂解析器：先按简单名找已加载的程序集，再按文件名在 mod 目录里找。
    /// </summary>
    private static void InstallResolver(AssemblyLoadContext ctx, string modDir)
    {
        ctx.Resolving += (c, name) =>
        {
            try
            {
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        GD.Print($"[Subaru.Variants] resolved '{name.Name}' from loaded assemblies");
                        return loaded;
                    }
                }

                if (string.IsNullOrEmpty(name.Name))
                    return null;

                foreach (var candidate in Directory.GetFiles(modDir, name.Name + ".dll", SearchOption.AllDirectories))
                {
                    GD.Print($"[Subaru.Variants] resolved '{name.Name}' from {candidate}");
                    return c.LoadFromAssemblyPath(candidate);
                }
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru.Variants] resolve '" + name.Name + "' failed: " + e.Message);
            }

            return null;
        };
    }

    /// <summary>把加载的真程序集登记到本 Mod 名下，游戏才知道它属于 Subaru。</summary>
    private static void Assoc(Assembly assembly)
    {
        try
        {
            ModManager.AssociateAssemblyWithMod(ModId, assembly);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru.Variants] associate assembly failed: " + e.Message);
        }
    }

    /// <summary>在真程序集里找 [ModInitializer] 方法；找不到返回 null（不抛异常）。</summary>
    private static MethodInfo FindInitializer(Assembly assembly)
    {
        GD.Print($"[Subaru.Variants] variant assembly: {assembly.GetName().Name} {assembly.GetName().Version}");

        var types = SafeTypes(assembly).ToList();
        GD.Print($"[Subaru.Variants] visible types: {types.Count}");

        foreach (var type in types)
        {
            if (type == null)
                continue;

            var isMain = type.Name.Contains("MainFile");

            ModInitializerAttribute attr;
            try
            {
                attr = type.GetCustomAttribute<ModInitializerAttribute>();
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru.Variants] 读取 " + type.FullName + " 的初始化标记失败: " + e.Message);
                continue;
            }

            if (isMain)
                GD.Print($"[Subaru.Variants] MainFile found: {type.FullName}, hasAttr={attr != null}");

            if (attr == null)
                continue;

            // 属性字段是私有的 initializerMethod，只能反射取
            var name = typeof(ModInitializerAttribute)
                .GetField("initializerMethod", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(attr) as string;

            if (string.IsNullOrWhiteSpace(name))
                name = "Initialize";

            var method = type.GetMethod(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (method != null)
                return method;

            GD.PushError($"[Subaru.Variants] 找不到初始化方法 {type.FullName}.{name}");
        }

        return null;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            GD.PushError($"[Subaru.Variants] 类型加载失败：{e.Types.Count(t => t != null)}/{e.Types.Length} 可加载");

            var shown = 0;
            foreach (var le in e.LoaderExceptions.Where(x => x != null))
            {
                GD.PushError("[Subaru.Variants] loader: " + le.Message);
                if (++shown >= 5)
                    break;
            }

            return e.Types.Where(t => t != null);
        }
    }

    // ---------- 版本 ----------

    /// <summary>从 release_info.json 读版本；读不到就返回空串。</summary>
    private static string ReadGameVersion()
    {
        foreach (var candidate in VersionFileCandidates())
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    var text = v.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        GD.Print($"[Subaru.Variants] version source: {candidate}");
                        return Normalize(text);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru.Variants] 读取 " + candidate + " 失败: " + e.Message);
            }
        }

        return "";
    }

    private static IEnumerable<string> VersionFileCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(List<string> list, string path)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(path))
                list.Add(path);
        }

        var list = new List<string>();

        try
        {
            var res = ProjectSettings.GlobalizePath("res://");
            Add(list, Path.Combine(res, "release_info.json"));

            var dir = new DirectoryInfo(res);
            // 从 exe 目录往上找两层（data_sts2_windows_x86_64 的父目录就是游戏根目录）
            for (var i = 0; i < 3 && dir != null; i++)
            {
                Add(list, Path.Combine(dir.FullName, "release_info.json"));
                dir = dir.Parent;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru.Variants] res:// 不可用: " + e.Message);
        }

        try
        {
            var exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
            if (!string.IsNullOrEmpty(exeDir))
            {
                Add(list, Path.Combine(exeDir, "release_info.json"));
                var parent = Directory.GetParent(exeDir);
                if (parent != null)
                    Add(list, Path.Combine(parent.FullName, "release_info.json"));
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    private static string Normalize(string version)
    {
        var v = version.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            v = v[1..];
        return v;
    }

    // ---------- 变体清单 ----------

    private sealed class VariantEntry
    {
        public string CompatTarget { get; set; } = "";
        public string Directory { get; set; } = "";
        public string Assembly { get; set; } = "";
    }

    private static List<VariantEntry> ReadVariants(string modDir)
    {
        var path = Path.Combine(modDir, ManifestName);
        if (!File.Exists(path))
        {
            GD.PushError("[Subaru.Variants] 缺清单: " + path);
            return new List<VariantEntry>();
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<VariantEntry>();

        if (!doc.RootElement.TryGetProperty("variants", out var variants)
            || variants.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in variants.EnumerateArray())
        {
            list.Add(new VariantEntry
            {
                CompatTarget = item.TryGetProperty("compatTarget", out var c) ? c.GetString() ?? "" : "",
                Directory = item.TryGetProperty("directory", out var d) ? d.GetString() ?? "" : "",
                Assembly = item.TryGetProperty("assembly", out var a) ? a.GetString() ?? "" : "",
            });
        }

        return list;
    }

    /// <summary>
    /// 选变体：优先「主次版本号相同」，否则退到「不超过当前版本里最高的那个」。
    /// 例：游戏 0.113.1 → 命中 compatTarget 0.113.0；游戏 0.111.1 → 命中 0.111.0。
    /// </summary>
    private static VariantEntry SelectVariant(List<VariantEntry> variants, string gameVersion)
    {
        var gv = Parse(gameVersion);
        if (gv == null)
        {
            GD.PushWarning("[Subaru.Variants] 版本号解析失败，用最后一个变体兜底");
            return variants[variants.Count - 1];
        }

        VariantEntry best = null;
        Version bestKey = null;
        VariantEntry fallback = null;

        foreach (var v in variants)
        {
            var cv = Parse(v.CompatTarget);
            if (cv == null)
                continue;

            if (cv.Major == gv.Major && cv.Minor == gv.Minor)
                return v;

            if (cv <= gv && (bestKey == null || cv > bestKey))
            {
                best = v;
                bestKey = cv;
            }

            if (fallback == null || cv > Parse(fallback.CompatTarget))
                fallback = v;
        }

        return best ?? fallback;
    }

    private static Version Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = Normalize(text).Split('.');
        var nums = new List<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n))
                break;
            nums.Add(n);
        }

        while (nums.Count < 3)
            nums.Add(0);

        return new Version(nums[0], nums[1], nums[2]);
    }
}
