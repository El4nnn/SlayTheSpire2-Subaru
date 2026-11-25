using System;
using System.Collections.Generic;
using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 「一串打击音效按顺序播放」的通用实现：每个实例有自己的游标和播放器池。
///
/// <para>两类使用场景（2026-11-20 用户定）：
///   · 对单多段 → <see cref="MultiHitSfx"/>：**循环**（段数比音效多时从头再来）；
///   · 对群多段 → <see cref="MultiAoeHitSfx"/>：**不循环**（音效用完就不再响）。</para>
///
/// <para><b>播放器池</b>：单个播放器会在下一次 Play 时砍掉上一段的尾音（这些音效 0.2~0.6s，
/// 而多段牌相邻命中可能只隔几帧），所以用 3 个播放器轮转，尾音能自然叠在一起。</para>
/// </summary>
internal sealed class SfxSequence(string pathFormat, int maxSegments, float volumeDb, bool loop)
{
    private const int PoolSize = 3;

    private readonly List<AudioStream> _segments = [];
    private readonly List<AudioStreamPlayer> _players = [];
    private bool _loaded;
    private int _nextSegment;
    private int _nextPlayer;

    /// <summary>加载到的段数（0 = 资源缺失）。</summary>
    public int Count
    {
        get
        {
            EnsureLoaded();
            return _segments.Count;
        }
    }

    /// <summary>从头开始（每张牌结算开始时调）。</summary>
    public void Restart() => _nextSegment = 0;

    /// <summary>播下一段；不循环模式下用完就静默返回。</summary>
    public void PlayNext()
    {
        try
        {
            EnsureLoaded();
            if (_segments.Count == 0)
                return;

            if (!loop && _nextSegment >= _segments.Count)
                return;                                  // 段数超了：不播

            var player = NextPlayer();
            if (player == null)
                return;

            player.Stream = _segments[_nextSegment % _segments.Count];
            _nextSegment++;

            player.VolumeDb = volumeDb;
            player.Play();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sfx sequence failed: " + e.Message);
        }
    }

    private AudioStreamPlayer? NextPlayer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        if (_players.Count < PoolSize)
        {
            var created = new AudioStreamPlayer
            {
                Name = "SubaruSfxSequence" + _players.Count,
                Bus = "Master",
                VolumeDb = volumeDb,
            };
            tree.Root.AddChild(created);
            _players.Add(created);
            return created;
        }

        var index = _nextPlayer % _players.Count;
        _nextPlayer++;

        var existing = _players[index];
        if (existing != null && GodotObject.IsInstanceValid(existing))
            return existing;

        var recreated = new AudioStreamPlayer
        {
            Name = "SubaruSfxSequence" + index,
            Bus = "Master",
            VolumeDb = volumeDb,
        };
        tree.Root.AddChild(recreated);
        _players[index] = recreated;
        return recreated;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            for (var i = 1; i <= maxSegments; i++)
            {
                var path = string.Format(pathFormat, i);
                if (!ResourceLoader.Exists(path))
                    break;

                var stream = GD.Load<AudioStream>(path);
                if (stream == null)
                    break;

                _segments.Add(stream);
            }

            GD.Print($"[Subaru] sfx sequence {pathFormat}: {_segments.Count} segments (loop={loop})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sfx sequence load failed: " + e.Message);
        }
    }
}
