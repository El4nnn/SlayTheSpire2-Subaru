using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 「单次群攻牌」的命中音效（素材：用户 2026-11-20 提供的 单次.mp3 → aoe_hit.ogg）。
///
/// <para>触发点见 <c>Patches.AttackSegmentPatch</c>：只对
/// 「<c>IsMultiTargeted &amp;&amp; 段数 == 1</c>」的本 mod 攻击牌播一次，
/// 和向下的屏幕顿挫（<c>Vfx.HitFeel.JoltDown</c>）同一帧。</para>
///
/// <para>只用一个 <see cref="AudioStreamPlayer"/>：同时命中多个目标不会叠出多份。</para>
/// </summary>
public static class AoeHitSfx
{
    private const string Path = "res://Subaru/audio/aoe_hit.ogg";

    /// <summary>响度。素材峰值 -0.5dBFS，稍微压一点避免和其它音效打架。</summary>
    private const float VolumeDb = -2f;

    private static AudioStream? _stream;
    private static bool _tried;
    private static AudioStreamPlayer? _player;

    public static void Play()
    {
            if (!Settings.SubaruAudioSettings.HitSfxEnabled)
                return;

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
                    Name = "SubaruAoeHitSfx",
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
            GD.PushWarning("[Subaru] aoe hit sfx failed: " + e.Message);
        }
    }
}
