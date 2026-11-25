using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 贤者·夏乌拉射线**命中**的音效（素材：用户 2026-11-21 提供的 ground impact）。
///
/// <para>只用一个 <see cref="AudioStreamPlayer"/>：连打两张时后一次 <c>Play()</c> 重整同一个播放器，
/// 不会叠成一片（和 <see cref="EnemyDefeatSfx"/> 同一套写法）。</para>
/// </summary>
public static class ShaulaHitSfx
{
    private const string Path = "res://Subaru/audio/shaula_hit.mp3";

    /// <summary>响度。素材原样就是 0 dB；要更响就往正数抬，压一压就用负数。</summary>
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
                    Name = "SubaruShaulaHitSfx",
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
            GD.PushWarning("[Subaru] shaula hit sfx failed: " + e.Message);
        }
    }
}
