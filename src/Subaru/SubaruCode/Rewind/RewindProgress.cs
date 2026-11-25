using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Saves;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 跨局累计的死归次数。用来解锁「死亡回归」这个历史节点（累计 10 次）。
///
/// 存到 <c>user://</c> 下一个小文件里，不碰游戏的存档结构：
/// 这个计数是 mod 自己的进度，跟 run 存档的字段耦合没有好处。
/// </summary>
public static class RewindProgress
{
    /// <summary>解锁「死亡回归」节点（→ 立即死归按钮）需要的累计次数。</summary>
    public const int UnlockThreshold = 10;

    /// <summary>解锁「Love love love love love love YOU」节点需要的累计次数。</summary>
    public const int LoveThreshold = 99;

    private const string FilePath = "user://subaru_rewind_total.txt";

    private static bool _loaded;
    private static int _total;

    /// <summary>累计死归次数。</summary>
    public static int Total
    {
        get
        {
            EnsureLoaded();
            return _total;
        }
    }

    /// <summary>「立即死归」按钮是否已经解锁（节点解锁后才出现）。</summary>
    public static bool ImmediateRewindUnlocked => Total >= UnlockThreshold;

    /// <summary>每次死归记一次；到阈值时去解锁对应节点。</summary>
    public static void Add()
    {
        EnsureLoaded();
        _total++;
        Save();
        GD.Print($"[Subaru] rewind total: {_total} (button={UnlockThreshold}, love={LoveThreshold})");

        if (_total >= UnlockThreshold)
            Subaru.SubaruCode.Timeline.SubaruEpochGranter.Grant<Subaru.SubaruCode.Timeline.SubaruRewindEpoch>(
                Subaru.SubaruCode.Timeline.SubaruEpochGate.RewindEpochId);

        if (_total >= LoveThreshold)
            Subaru.SubaruCode.Timeline.SubaruEpochGranter.Grant<Subaru.SubaruCode.Timeline.SubaruLoveEpoch>(
                Subaru.SubaruCode.Timeline.SubaruEpochGate.LoveEpochId);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Read);
            if (file != null && int.TryParse(file.GetAsText().Trim(), out var value))
                _total = Math.Max(0, value);

            GD.Print($"[Subaru] rewind total loaded: {_total}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] load rewind total failed: " + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            using var file = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(_total.ToString());
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] save rewind total failed: " + e.Message);
        }
    }
}
