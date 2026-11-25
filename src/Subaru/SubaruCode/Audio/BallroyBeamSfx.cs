using Godot;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 巴尔罗伊「天降光柱」的音效：三条素材、三个独立播放器。
///
/// <list type="bullet">
/// <item><b>一档</b>（<see cref="PlayTier"/> tier 1）：<c>ballroy_beam1.mp3</c>（用户 2026-11-22 提供的 1.mp3）</item>
/// <item><b>二 / 三档</b>：<c>ballroy_beam2.mp3</c>（2.mp3）；三档音高略降（0.94）压出更重的落点</item>
/// <item><b>终结</b>：<c>ballroy_finisher.mp3</c>（终结.mp3）——蓄力那一下；
/// 光柱砸进白屏时再叠 <see cref="PlayImpact"/>（复用二档素材，原调）</item>
/// </list>
///
/// <para>三个播放器互不干扰：同一回合里回响的二三段、终结的蓄力 + 砸落都能叠在一起。</para>
/// </summary>
public static class BallroyBeamSfx
{
    private const string Tier1Path = "res://Subaru/audio/ballroy_beam1.mp3";
    private const string Tier2Path = "res://Subaru/audio/ballroy_beam2.mp3";
    private const string FinisherPath = "res://Subaru/audio/ballroy_finisher.mp3";

    private const float TierVolumeDb = -5f;
    private const float FinisherVolumeDb = -4f;

    private static AudioStream? _tier1Stream;
    private static AudioStream? _tier2Stream;
    private static AudioStream? _finisherStream;
    private static bool _tier1Tried;
    private static bool _tier2Tried;
    private static bool _finisherTried;

    private static AudioStreamPlayer? _tier1Player;
    private static AudioStreamPlayer? _tier2Player;
    private static AudioStreamPlayer? _finisherPlayer;

    /// <summary>光柱砸下的那一帧（按档选素材；三档降调）。</summary>
    public static void PlayTier(int tier)
    {
        if (tier <= 1)
        {
            PlayOn(ref _tier1Player, ref _tier1Stream, ref _tier1Tried,
                Tier1Path, "SubaruBallroyBeam1", TierVolumeDb, 1f);
            GD.Print($"[Subaru] ballroy sfx: beam tier=1 ({Tier1Path})");
            return;
        }

        var pitch = tier >= 3 ? 0.94f : 1f;
        PlayOn(ref _tier2Player, ref _tier2Stream, ref _tier2Tried,
            Tier2Path, "SubaruBallroyBeam2", TierVolumeDb, pitch);
        GD.Print($"[Subaru] ballroy sfx: beam tier={tier} ({Tier2Path}, pitch={pitch:F2})");
    }

    /// <summary>终结的蓄力（白屏之前那一段）。</summary>
    public static void PlayCharge()
    {
        PlayOn(ref _finisherPlayer, ref _finisherStream, ref _finisherTried,
            FinisherPath, "SubaruBallroyFinisherSfx", FinisherVolumeDb, 1f);
        GD.Print($"[Subaru] ballroy sfx: charge ({FinisherPath})");
    }

    /// <summary>光柱砸进白屏的那一帧（和蓄力音错开，形成「先响一下、再砸一下」）。</summary>
    public static void PlayImpact() =>
        PlayOn(ref _tier2Player, ref _tier2Stream, ref _tier2Tried,
            Tier2Path, "SubaruBallroyBeam2", TierVolumeDb + 1f, 0.9f);

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
                    Stream = stream,
                    PitchScale = pitch,
                };
                tree.Root.AddChild(slot);
            }

            slot.VolumeDb = volumeDb;
            slot.PitchScale = pitch;
            slot.Play();
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] ballroy sfx failed: " + e.Message);
        }
    }
}
