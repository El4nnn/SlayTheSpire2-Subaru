using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 击败敌人时的「碎裂」音效（素材：用户 2026-11-20 提供的 碎裂音效.mp3 → enemy_defeat.ogg）。
///
/// 只用一个 <see cref="AudioStreamPlayer"/>：同帧多个敌人一起死时，后一次 <c>Play()</c>
/// 直接重整同一个播放器，不会叠成一片（也避免一帧里 new 出好几个节点）。
/// </summary>
public static class EnemyDefeatSfx
{
    private const string Path = "res://Subaru/audio/enemy_defeat.ogg";

    /// <summary>响度。素材峰值 -1dBFS，0 dB 即原样播放；要更响就往上抬。</summary>
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
                    Name = "SubaruEnemyDefeatSfx",
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
            GD.PushWarning("[Subaru] enemy defeat sfx failed: " + e.Message);
        }
    }
}
