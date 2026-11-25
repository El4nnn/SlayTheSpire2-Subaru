using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 「单段单体攻击」的命中音效（素材：用户 2026-11-20 提供的 单次.mp3 → single_hit.ogg）。
///
/// <para>触发点见 <c>Patches.AttackSegmentPatch</c>：只对「单体 + 只结算 1 段」的本 mod 攻击牌播。
/// 原来这条素材是给「单次群攻」用的，2026-11-20 用户改了口径：
/// 单次群攻换成 对群.mp3（见 <see cref="AoeHitSfx"/>），这条给单段单体。</para>
///
/// <para>只用一个 <see cref="AudioStreamPlayer"/>：同一帧多次调用是重整，不叠音。</para>
/// </summary>
public static class SingleHitSfx
{
    private const string Path = "res://Subaru/audio/single_hit.ogg";

    /// <summary>响度。素材峰值 -0.5dBFS，压一点和其它音效平衡。</summary>
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
                    Name = "SubaruSingleHitSfx",
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
            GD.PushWarning("[Subaru] single hit sfx failed: " + e.Message);
        }
    }
}
