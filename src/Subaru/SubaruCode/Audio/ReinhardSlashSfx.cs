using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 剑圣·莱茵哈鲁特的音效。两条素材、两个独立播放器：
///
/// <list type="bullet">
/// <item><b>出刀</b>（<see cref="Play"/>）：<c>reinhard_slash.mp3</c>
/// （用户 2026-11-22 提供的 dragon-studio-sword-slice-2）。在斩线扫出的同一帧播，
/// **一张牌只播一次**（调用点在牌的 OnPlayCard 里，不在每段伤害）。</item>
/// <item><b>击杀 / 终结</b>（<see cref="PlayKill"/>）：<c>reinhard_kill.mp3</c>
/// （用户 2026-11-22 提供的 freesound hit-swing-sword-small-2）。之前用同一份素材降调顶上，
/// 用户给了专属素材后换成真音、原调。</item>
/// </list>
///
/// <para>两个播放器互不影响：击杀音在切开的瞬间响时出刀音早已走完；同类连打时后一次 Play
/// 重整同一个播放器，不叠音。</para>
/// </summary>
public static class ReinhardSlashSfx
{
    private const string SlashPath = "res://Subaru/audio/reinhard_slash.mp3";
    private const string KillPath = "res://Subaru/audio/reinhard_kill.mp3";

    /// <summary>出刀响度。素材偏亮，压到 -4 dB。</summary>
    private const float VolumeDb = -4f;

    /// <summary>击杀 / 终结响度（比出刀更靠前一点）。</summary>
    private const float KillVolumeDb = -2.5f;

    /// <summary>击杀 / 终结音高（专属素材，原调）。</summary>
    private const float KillPitch = 1f;

    private static AudioStream? _slashStream;
    private static AudioStream? _killStream;
    private static bool _slashTried;
    private static bool _killTried;
    private static AudioStreamPlayer? _player;
    private static AudioStreamPlayer? _killPlayer;

    /// <summary>出刀（一张牌一次）。</summary>
    public static void Play()
        => PlayOn(ref _player, ref _slashStream, ref _slashTried,
            SlashPath, "SubaruReinhardSlashSfx", VolumeDb, 1f);

    /// <summary>击杀 / 终结（专属素材，原调）。</summary>
    public static void PlayKill()
    {
        PlayOn(ref _killPlayer, ref _killStream, ref _killTried,
            KillPath, "SubaruReinhardKillSfx", KillVolumeDb, KillPitch);

        GD.Print($"[Subaru] reinhard sfx: kill ({KillPath}, db={KillVolumeDb:F1}, pitch={KillPitch:F2})");
    }

    private static void PlayOn(ref AudioStreamPlayer? slot, ref AudioStream? stream, ref bool tried,
        string path, string name, float volumeDb, float pitch)
    {
        try
        {
            if (!tried)
            {
                tried = true;
                if (ResourceLoader.Exists(path))
                    stream = GD.Load<AudioStream>(path);
                else
                    GD.Print($"[Subaru] sfx missing: {path}");
            }

            if (stream == null)
                return;

            if (slot == null || !GodotObject.IsInstanceValid(slot))
            {
                if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                    return;

                slot = new AudioStreamPlayer
                {
                    Name = name,
                    Bus = "Master",
                    VolumeDb = volumeDb,
                };
                tree.Root.AddChild(slot);
            }

            slot.Stream = stream;
            slot.VolumeDb = volumeDb;
            slot.PitchScale = pitch;
            slot.Play();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard sfx failed: " + e.Message);
        }
    }
}
