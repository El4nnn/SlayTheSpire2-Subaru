using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 「对单多段伤害」的命中音效序列（素材：用户 2026-11-20 提供的 多段伤害 对单.mp3，
/// 由 <c>_tools/extract_multi_hit.py</c> 剪成 12 段 → <c>res://Subaru/audio/multi/</c>）。
///
/// <para>一张多段牌开始结算时 <see cref="Restart"/>，之后每命中一段调一次
/// <see cref="PlayNext"/>；段数比音效多时**从头循环**（用户要求）。</para>
///
/// <para>实现见 <see cref="SfxSequence"/>（播放器池、加载逻辑都在那边）。</para>
/// </summary>
public static class MultiHitSfx
{
    /// <summary>响度。各段已按峰值归一到 0.9，这里整体压一点和其它音效平衡。</summary>
    private const float VolumeDb = -3f;

    private static readonly SfxSequence Seq = new(
        "res://Subaru/audio/multi/multi_hit_{0:00}.ogg", maxSegments: 32, volumeDb: VolumeDb, loop: true);

    /// <summary>加载到的段数（0 = 资源缺失）。</summary>
    public static int SegmentCount => Seq.Count;

    /// <summary>从头开始循环（每张牌结算开始时调）。</summary>
    public static void Restart() => Seq.Restart();

    /// <summary>播下一段；播完最后一段后回到第 1 段。</summary>
    public static void PlayNext()
    {
        if (!Settings.SubaruAudioSettings.HitSfxEnabled)
            return;

        Seq.PlayNext();
    }
}
