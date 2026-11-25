using System;
using System.Text.Json;
using Godot;

namespace Subaru.SubaruCode.Settings;

/// <summary>
/// 音频设置：是否替换战斗 BGM、是否播开头人声段、人声段 / 循环段 / 死归音效三个音量。
///
/// <para>存在 <c>user://subaru_audio.json</c>，和 RitsuLib 设置页之间用
/// <c>ModSettingsBindings.Callback</c> 对接 —— 读/写/保存都由这里控制，
/// 所以改设置能**立刻**作用到正在播放的音乐和音效上。</para>
///
/// <para>旧的调试开关 <c>user://subaru_nomusic.txt</c> 保留：文件存在时无条件不接管音乐，
/// 排查「怀疑是我们这段音乐弄坏了 FMOD」时仍然可以一票否决。</para>
///
/// <para><b>音量的口径</b>：0–100 的百分比，100 = 原来的硬编码音量（观感不变），
/// 0 = 静音。换算走对数（人耳感知是对数的），见 <see cref="DbFromPercent"/>。</para>
/// </summary>
public static class SubaruAudioSettings
{
    private const string FilePath = "user://subaru_audio.json";
    private const string LegacyNoMusicFlag = "user://subaru_nomusic.txt";

    /// <summary>音量百分比的基准：100% 时就是这两个旧硬编码值。</summary>
    public const float MusicBaselineDb = -3.1f;
    public const float RewindSfxBaselineDb = 0.0f;   // 2026-11-22：原 +6 dB 太炸耳，降到 0

    private sealed class Data
    {
        public bool ReplaceBattleBgm { get; set; } = true;
        public bool PlayVocalIntro { get; set; } = true;
        public int VocalVolumePercent { get; set; } = 60;
        public int LoopVolumePercent { get; set; } = 45;
        public int RewindVolumePercent { get; set; } = 70;

        /// <summary>打击音效（单体/多段/群攻那四条）总开关。</summary>
        public bool HitSfxEnabled { get; set; } = true;
    }

    private static Data _data = new();
    private static bool _loaded;

    /// <summary>是否用我们的战斗 BGM 替换原版音乐。</summary>
    public static bool ReplaceBattleBgm
    {
        get { EnsureLoaded(); return _data.ReplaceBattleBgm; }
        set
        {
            EnsureLoaded();
            if (_data.ReplaceBattleBgm == value)
                return;

            _data.ReplaceBattleBgm = value;
            Save();
            Audio.SubaruMusic.OnSettingsChanged();
        }
    }

    /// <summary>是否播放曲子的开头人声段（0–9.16s）。关掉可以直接进入乐器段。</summary>
    public static bool PlayVocalIntro
    {
        get { EnsureLoaded(); return _data.PlayVocalIntro; }
        set
        {
            EnsureLoaded();
            if (_data.PlayVocalIntro == value)
                return;

            _data.PlayVocalIntro = value;
            Save();
            Audio.SubaruMusic.ApplyPlaybackMode();
        }
    }

    /// <summary>人声段音量（只在开头那段起作用），0–100。</summary>
    public static int VocalVolumePercent
    {
        get { EnsureLoaded(); return _data.VocalVolumePercent; }
        set
        {
            EnsureLoaded();
            var clamped = Math.Clamp(value, 0, 100);
            if (_data.VocalVolumePercent == clamped)
                return;

            _data.VocalVolumePercent = clamped;
            Save();
            Audio.SubaruMusic.ApplyVolumeNow();
        }
    }

    /// <summary>循环段（乐器段）音量，0–100。</summary>
    public static int LoopVolumePercent
    {
        get { EnsureLoaded(); return _data.LoopVolumePercent; }
        set
        {
            EnsureLoaded();
            var clamped = Math.Clamp(value, 0, 100);
            if (_data.LoopVolumePercent == clamped)
                return;

            _data.LoopVolumePercent = clamped;
            Save();
            Audio.SubaruMusic.ApplyVolumeNow();
        }
    }

    /// <summary>死归音效音量，0–100。</summary>
    public static int RewindVolumePercent
    {
        get { EnsureLoaded(); return _data.RewindVolumePercent; }
        set
        {
            EnsureLoaded();
            var clamped = Math.Clamp(value, 0, 100);
            if (_data.RewindVolumePercent == clamped)
                return;

            _data.RewindVolumePercent = clamped;
            Save();
            Audio.RewindSfx.ApplyVolume();
        }
    }

    /// <summary>
    /// 打击音效总开关（2026-11-20）：管 single_hit / multi_hit / aoe_hit / multi_aoe 四条，
    /// 默认开。关掉后命中不再出声，但顿帧、光爆、击杀特效都不受影响。
    /// </summary>
    public static bool HitSfxEnabled
    {
        get { EnsureLoaded(); return _data.HitSfxEnabled; }
        set
        {
            EnsureLoaded();
            if (_data.HitSfxEnabled == value)
                return;

            _data.HitSfxEnabled = value;
            Save();
        }
    }

    /// <summary>最终判定：设置允许替换，且没被旧的调试文件一票否决。</summary>
    public static bool BattleBgmReplaced => ReplaceBattleBgm && !LegacyNoMusicFlagExists();

    /// <summary>
    /// 百分比 → dB。0 直接给 -80（静音），100 = 基准值不变。
    /// </summary>
    public static float DbFromPercent(int percent, float baselineDb)
        => percent <= 0 ? -80f : (float)(baselineDb + 20.0 * Math.Log10(percent / 100.0));

    public static void Save()
    {
        try
        {
            using var file = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushWarning("[Subaru] audio settings: cannot open " + FilePath);
                return;
            }

            file.StoreString(JsonSerializer.Serialize(_data));
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] audio settings save failed: " + e.Message);
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            if (!Godot.FileAccess.FileExists(FilePath))
                return;

            using var file = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
                return;

            var parsed = JsonSerializer.Deserialize<Data>(file.GetAsText());
            if (parsed != null)
                _data = parsed;

            GD.Print($"[Subaru] audio settings loaded: replace={_data.ReplaceBattleBgm}, vocal={_data.PlayVocalIntro}, hitSfx={_data.HitSfxEnabled}, "
                     + $"vocalVol={_data.VocalVolumePercent}%, loopVol={_data.LoopVolumePercent}%, rewindVol={_data.RewindVolumePercent}%");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] audio settings load failed, using defaults: " + e.Message);
        }
    }

    private static bool LegacyNoMusicFlagExists()
    {
        try
        {
            return Godot.FileAccess.FileExists(LegacyNoMusicFlag);
        }
        catch
        {
            return false;
        }
    }
}
