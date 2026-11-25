using System;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 死归「复活」那一刻的音效：从素材视频里裁出来的置气场音，
/// 和紫色闪光 B 同一帧触发（见 RewindAnimation）。
/// </summary>
public static class RewindSfx
{
    private const string Path = "res://Subaru/audio/rewind.ogg";

    /// <summary>音效响度：翻倍（+6 dB）。</summary>
    private static float CurrentVolumeDb =>
        Settings.SubaruAudioSettings.DbFromPercent(
            Settings.SubaruAudioSettings.RewindVolumePercent,
            Settings.SubaruAudioSettings.RewindSfxBaselineDb);

    /// <summary>设置页改了音量时调用：播放器还在就立刻生效（不用等下一次播放）。</summary>
    public static void ApplyVolume()
    {
        try
        {
            if (_player != null && GodotObject.IsInstanceValid(_player))
                _player.VolumeDb = CurrentVolumeDb;
        }
        catch
        {
            // 忽略：改音量失败不该影响音效本身
        }
    }

    private static AudioStream _stream;
    private static bool _tried;
    private static AudioStreamPlayer _player;

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
                    Name = "SubaruRewindSfx",
                    Bus = "Master",
                    VolumeDb = CurrentVolumeDb,
                };
                tree.Root.AddChild(_player);
            }

            _player.Stream = _stream;
            _player.VolumeDb = CurrentVolumeDb;
            _player.Play();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] rewind sfx failed: " + e.Message);
        }
    }
}
