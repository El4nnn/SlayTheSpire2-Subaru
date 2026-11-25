using System;
using System.Collections.Generic;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 死归黑幕里那张「嫉妒魔女」剪影的配套音频。
/// 和黑幕淡入同一帧起播，所以时长决定了黑幕要停多久——
/// 播放时把音频长度返回给调用方，由它算停留时间，不再写死常量。
///
/// 素材池里的多条素材可以各自带语音（见 <see cref="UI.RewindWitchPool"/>），
/// 按路径缓存，不会每次重新加载。
/// </summary>
public static class WitchFaceSfx
{
    /// <summary>响度。原素材偏轻，抬一点。</summary>
    private const float VolumeDb = -2.0f;

    private static readonly Dictionary<string, AudioStream> Cache = [];
    private static readonly HashSet<string> Missing = [];

    private static AudioStreamPlayer _player;

    /// <summary>
    /// 播放指定语音，返回它的时长（秒）；拿不到资源时返回 0，调用方用兜底时长。
    /// </summary>
    public static double Play(string path)
    {
        try
        {
            var stream = Load(path);
            if (stream == null)
                return 0;

            if (_player == null || !GodotObject.IsInstanceValid(_player))
            {
                if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                    return 0;

                _player = new AudioStreamPlayer
                {
                    Name = "SubaruWitchFaceSfx",
                    Bus = "Master",
                    VolumeDb = VolumeDb,
                };
                tree.Root.AddChild(_player);
            }

            _player.Stream = stream;
            _player.Play();
            return stream.GetLength();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] witch face sfx failed: " + e.Message);
            return 0;
        }
    }

    private static AudioStream Load(string path)
    {
        if (string.IsNullOrEmpty(path) || Missing.Contains(path))
            return null;

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        if (!ResourceLoader.Exists(path))
        {
            Missing.Add(path);
            GD.Print($"[Subaru] witch face sfx missing: {path}");
            return null;
        }

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            Missing.Add(path);
            return null;
        }

        Cache[path] = stream;
        return stream;
    }
}
