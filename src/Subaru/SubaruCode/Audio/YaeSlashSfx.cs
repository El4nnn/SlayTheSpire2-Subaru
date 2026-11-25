using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 八重·天膳「原子斩」的音效（素材：用户 2026-11-21 提供的 knife_swish01.mp3）。
///
/// <para>在**切线合上**那一刻播（<c>YaeAtomicSlash</c> 的 cutAt），不是死亡瞬间 ——
/// 这样声音和「刀落下」对得上。只用一个 <see cref="AudioStreamPlayer"/>：
/// 同帧连杀两只时后一次 <c>Play()</c> 重整同一个播放器，不叠音。</para>
/// </summary>
public static class YaeSlashSfx
{
    private const string Path = "res://Subaru/audio/yae_slash.mp3";

    /// <summary>响度。素材偏轻，抬 2 dB 让它和击杀音效同一档次。</summary>
    private const float VolumeDb = -3f;

    private static AudioStream? _stream;
    private static bool _tried;
    private static AudioStreamPlayer? _player;

    public static void Play()
    {
        try
        {
            if (!_tried)
            {
                _tried = true;
                if (ResourceLoader.Exists(Path))
                    _stream = GD.Load<AudioStream>(Path);
                else
                    GD.Print($"[Subaru] sfx missing: {Path}");
            }

            if (_stream == null)
                return;

            if (_player == null || !GodotObject.IsInstanceValid(_player))
            {
                if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                    return;

                _player = new AudioStreamPlayer
                {
                    Name = "SubaruYaeSlashSfx",
                    Bus = "Master",
                    VolumeDb = VolumeDb,
                };
                tree.Root.AddChild(_player);
            }

            _player.Stream = _stream;
            _player.VolumeDb = VolumeDb;
            _player.Play();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] yae slash sfx failed: " + e.Message);
        }
    }
}
