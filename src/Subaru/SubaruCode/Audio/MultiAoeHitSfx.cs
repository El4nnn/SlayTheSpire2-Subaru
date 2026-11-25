using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 「对群多段伤害」的命中音效序列（素材：用户 2026-11-20 提供的 多段对群.mp3，
/// 由 <c>_tools/extract_multi_hit.py</c> 剪成 4 段 → <c>res://Subaru/audio/multi_aoe/</c>）。
///
/// <para>处理方式和对单多段一致（每命中一段播一段），**但不循环**：
/// 段数超过音效段数时，多出来的段就不播了（用户 2026-11-20 定）。</para>
///
/// <para>实现见 <see cref="SfxSequence"/>。</para>
/// </summary>
public static class MultiAoeHitSfx
{
    /// <summary>响度。各段已按峰值归一到 0.9。</summary>
    private const float VolumeDb = -3f;

    private static readonly SfxSequence Seq = new(
        "res://Subaru/audio/multi_aoe/multi_aoe_{0:00}.ogg", maxSegments: 16, volumeDb: VolumeDb, loop: false);

    /// <summary>加载到的段数（0 = 资源缺失）。</summary>
    public static int SegmentCount => Seq.Count;

    /// <summary>从头开始（每张牌结算开始时调）。</summary>
    public static void Restart() => Seq.Restart();

    /// <summary>播下一段；用完就不再响（不循环）。</summary>
    public static void PlayNext()
    {
        if (!Settings.SubaruAudioSettings.HitSfxEnabled)
            return;

        Seq.PlayNext();
    }
}
