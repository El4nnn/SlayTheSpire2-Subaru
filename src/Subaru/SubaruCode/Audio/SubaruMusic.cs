using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Subaru.SubaruCode.Audio;

/// <summary>
/// 昴的战斗音乐替换。
///
/// 游戏底子是 FMOD，`PlayCustomMusic` 只能切 bank 里已有的音乐状态，
/// 塞不进外部曲子。所以这里走「压掉原版 + 自己播」：
///   挂钩 NRunMusicController.UpdateTrack —— 进房间、开战、战斗结束都会调它，
///   这是个天然的状态同步点。
///
/// 只对昴生效；普通战斗、精英、首领用同一首；非战斗房间不接管。
/// </summary>
public static class SubaruMusic
{
    private const string BattleTrack = "res://Subaru/audio/battle.ogg";

    /// <summary>
    /// 循环起点。
    ///
    /// 曲子的人声是四句，气口在 9.16 / 17.28 / 25.68 / 33.98 s。现在只保留第一句：
    /// 0 → 9.16s 淡出，跳到原曲 35.17s（第 18 小节，人声结束后的整小节）淡入，
    /// 一直到结尾。新时长 133.89s。
    ///
    /// 循环点的映射：原曲 52.112s（第 25 小节附近的鼓点进入）
    /// → 新时间轴 = 9.16 + (52.112 - 35.17) = 26.102s。
    /// </summary>
    private const double LoopStartSeconds = 26.102;

    /// <summary>战斗曲响度：原曲太抢，压到 70%（-3.1 dB）。</summary>
    private const float VolumeDb = -3.1f;

    /// <summary>
    /// 排查开关：<c>user://subaru_nomusic.txt</c> 存在时完全不动原版音乐。
    ///
    /// 用途：怀疑「战斗开始调 StopMusic()」把 FMOD 的篝火环境音状态弄坏了，
    /// 导致进休息处时 `UpdateTrack(RoomType)` 里的 campfire Call 崩（0xc0000005）。
    /// 建这个文件 → 重启游戏 → 再进一次休息处，就能确认到底是不是我们这段的锅。
    /// </summary>
    private const string DisableFlag = "user://subaru_nomusic.txt";

    /// <summary>第一段人声在 9.16s 淡出结束（曲子是外部剪辑过的：0→9.16 人声，随后淡出接乐器段）。</summary>
    private const double VocalEndSeconds = 9.16;

    /// <summary>当前音量用的是不是「人声段」那一档；越过 <see cref="VocalEndSeconds"/> 后切成循环段音量。</summary>
    private static bool _vocalVolumeActive;

    /// <summary>按「人声段 / 循环段」取对应设置算出的 dB。</summary>
    private static float MusicVolumeDb(bool vocalSegment) =>
        Subaru.SubaruCode.Settings.SubaruAudioSettings.DbFromPercent(
            vocalSegment
                ? Subaru.SubaruCode.Settings.SubaruAudioSettings.VocalVolumePercent
                : Subaru.SubaruCode.Settings.SubaruAudioSettings.LoopVolumePercent,
            Subaru.SubaruCode.Settings.SubaruAudioSettings.MusicBaselineDb);

    public static bool Enabled
    {
        get
        {
            // 设置页的开关 + 旧的 subaru_nomusic.txt 一票否决都在这里判定
            return Subaru.SubaruCode.Settings.SubaruAudioSettings.BattleBgmReplaced;
        }
    }

    /// <summary>最后一次触发 UpdateTrack 的音乐控制器：设置改开关时用它让原版音乐立刻回来。</summary>
    private static NRunMusicController _lastController;

    /// <summary>
    /// 设置页改了「替换战斗 BGM」时调用：
    ///   · 关掉 → 停我们的曲子，并重新跑一次 UpdateTrack 把原版音乐接回来
    ///   · 打开 → 同样重跑一次，让我们的曲子立刻接管
    /// 重跑 UpdateTrack 会再次进 OnTrackUpdated，那边按新设置决定播谁，不会递归。
    /// </summary>
    public static void OnSettingsChanged()
    {
        try
        {
            StopOwn();

            if (_lastController != null && GodotObject.IsInstanceValid(_lastController))
                _lastController.UpdateTrack();

            GD.Print($"[Subaru] music setting changed: replace={Enabled}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] music setting apply failed: " + e.Message);
        }
    }

    private static AudioStreamPlayer _player;
    private static AudioStream _stream;
    private static bool _triedLoad;
    private static bool _owning;
    private static bool _combatEndHooked;

    /// <summary>UpdateTrack 之后调用：决定这一帧该不该由我们接管音乐。</summary>
    public static void OnTrackUpdated(NRunMusicController controller)
    {
        try
        {
            _lastController = controller;

            if (!Enabled)
            {
                StopOwn();
                return;
            }

            EnsureCombatEndHook();

            // 判据必须是「战斗是否还在进行」，不能用房间类型：
            // 战斗结束后的结算、领奖励阶段房间仍然是 Monster，
            // 按房间类型判会导致战斗打完了曲子还在放。
            var inCombat = IsCombatActive();
            if (!inCombat || !IsSubaruRun())
            {
                StopOwn();
                return;
            }

            var stream = Load();
            if (stream == null)
            {
                // 文件还没放进来：保持原版音乐，别把人搞哑了
                StopOwn();
                return;
            }

            var player = EnsurePlayer();
            if (player == null)
                return;

            // 已经在放我们的曲子：什么都不用做
            if (_owning && player.Playing)
                return;

            // 先停原版音乐，再播我们的。
            //
            // **不能调 controller.StopMusic()**：它除了停音乐，还会
            // `_proxy.Call(_stopAmbience)` 把环境音事件一起停掉并置空 _currentTrack/_failedTrack。
            // 之后进休息处时，原版 UpdateTrack(RoomType) 里那一句
            // `_proxy.Call(_updateCampfireAmbienceCallback, true)` 会作用在已经被放掉的事件上，
            // 直接段错误（0xc0000005，整个进程挂掉，日志里看不到托管异常）。
            //
            // 所以这里只停「音乐」这一路，环境音一概不动。
            StopVanillaMusicOnly(controller);

            player.Stream = stream;
            _vocalVolumeActive = Subaru.SubaruCode.Settings.SubaruAudioSettings.PlayVocalIntro;
            player.VolumeDb = MusicVolumeDb(_vocalVolumeActive);
            player.Play();
            if (!_vocalVolumeActive)
                player.Seek((float)VocalEndSeconds);   // 不播人声：直接从乐器段开始
            _owning = true;

            GD.Print($"[Subaru] custom battle music started (vocal={_vocalVolumeActive})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] music override failed: " + e.Message);
        }
    }

    private static RoomType GetRoomType()
    {
        try
        {
            var run = RunManager.Instance?.State;
            if (run == null)
                return RoomType.Unassigned;

            return run.CurrentRoom?.RoomType ?? RoomType.Unassigned;
        }
        catch
        {
            return RoomType.Unassigned;
        }
    }

    /// <summary>战斗是否真的在进行（结算/奖励阶段为 false）。</summary>
    private static bool IsCombatActive()
    {
        try
        {
            var manager = CombatManager.Instance;
            return manager != null && manager.IsInProgress && !manager.IsOverOrEnding;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>兜底：万一战斗结束没有触发 UpdateTrack，这里也能把曲子停掉。</summary>
    private static void EnsureCombatEndHook()
    {
        if (_combatEndHooked)
            return;

        try
        {
            var manager = CombatManager.Instance;
            if (manager == null)
                return;

            manager.CombatEnded += _ => StopOwn();
            _combatEndHooked = true;
        }
        catch
        {
            // 忽略
        }
    }

    private static bool IsSubaruRun()
    {
        try
        {
            var run = RunManager.Instance?.State;
            var character = run?.Players?.FirstOrDefault()?.Character;
            return character is Subaru.SubaruCode.Character.Subaru;
        }
        catch
        {
            return false;
        }
    }

    private static AudioStream Load()
    {
        if (_triedLoad)
            return _stream;

        _triedLoad = true;

        try
        {
            if (!ResourceLoader.Exists(BattleTrack))
            {
                GD.Print($"[Subaru] music file missing: {BattleTrack}");
                return null;
            }

            var stream = GD.Load<AudioStream>(BattleTrack);
            if (stream is AudioStreamOggVorbis ogg)
            {
                // 播完一整遍之后，从 LoopOffset 开始接回，无缝循环
                ogg.Loop = true;
                ogg.LoopOffset = LoopStartSeconds;
            }
            else if (stream is AudioStreamWav wav)
            {
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = (int)(LoopStartSeconds * wav.MixRate);
            }

            _stream = stream;
            return _stream;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] load music failed: {e.Message}");
            return null;
        }
    }

    private static AudioStreamPlayer EnsurePlayer()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
            return _player;

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        _player = new AudioStreamPlayer
        {
            Name = "SubaruBattleMusic",
            Bus = "Master",
            VolumeDb = VolumeDb,
        };
        tree.Root.AddChild(_player);

        // 兜底哨兵：退回主菜单、结束本局、退出战斗这些路径都不一定触发 UpdateTrack，
        // 由它每 0.5 秒看一次「还在不在昴的战斗里」，不在就把曲子停掉。
        _player.AddChild(new SubaruMusicGuard { Name = "Guard" });

        return _player;
    }

    /// <summary>哨兵每帧调用：不在战斗里就不该继续放。</summary>
    /// <summary>
    /// 设置页改了「播不播人声段」：重启一遍，让新的起播位置立刻生效。
    /// 没在播的时候不用管，下次开战自然会按新设置起播。
    /// </summary>
    public static void ApplyPlaybackMode()
    {
        try
        {
            if (!_owning || _player == null || !GodotObject.IsInstanceValid(_player))
                return;

            var playVocal = Subaru.SubaruCode.Settings.SubaruAudioSettings.PlayVocalIntro;
            _player.Stop();
            _vocalVolumeActive = playVocal;
            _player.VolumeDb = MusicVolumeDb(playVocal);
            _player.Play();
            if (!playVocal)
                _player.Seek((float)VocalEndSeconds);

            GD.Print($"[Subaru] music playback mode changed (vocal={playVocal})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] apply playback mode failed: " + e.Message);
        }
    }

    /// <summary>设置页改了人声/循环音量：按当前播放位置立刻算一遍。</summary>
    public static void ApplyVolumeNow()
    {
        try
        {
            if (!_owning || _player == null || !GodotObject.IsInstanceValid(_player))
                return;

            _vocalVolumeActive = _player.GetPlaybackPosition() < VocalEndSeconds;
            _player.VolumeDb = MusicVolumeDb(_vocalVolumeActive);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] apply music volume failed: " + e.Message);
        }
    }

    public static void Tick()
    {
        if (!_owning)
            return;

        // 人声段结束 → 换成循环段音量。0.5s 粒度足够：切点本来就落在淡出里，听不出来。
        if (_vocalVolumeActive && _player != null && GodotObject.IsInstanceValid(_player)
            && _player.GetPlaybackPosition() >= VocalEndSeconds)
        {
            _vocalVolumeActive = false;
            _player.VolumeDb = MusicVolumeDb(false);
        }

        if (!IsCombatActive() || !IsSubaruRun())
        {
            GD.Print("[Subaru] battle music stopped (left combat)");
            StopOwn();
        }
    }

    /// <summary>
    /// 只停原版的音乐，不动环境音。
    /// 原版 StopMusic() 会把音乐和环境音一起停（还有 _currentTrack 记账），
    /// 那会让篝火环境音事件变成空壳，进休息处就段错误。
    /// </summary>
    private static void StopVanillaMusicOnly(NRunMusicController controller)
    {
        if (controller == null)
            return;

        try
        {
            var proxy = AccessTools.Field(typeof(NRunMusicController), "_proxy")?.GetValue(controller) as Node;
            if (proxy == null || !GodotObject.IsInstanceValid(proxy))
                return;

            var stop = AccessTools.Field(typeof(NRunMusicController), "_stopMusic")?.GetValue(null);
            if (stop is StringName stopName)
                proxy.Call(stopName);

            // 记账清零：出战斗后原版才会重新播正确的曲子
            AccessTools.Field(typeof(NRunMusicController), "_currentTrack")?.SetValue(controller, null);
            AccessTools.Field(typeof(NRunMusicController), "_failedTrack")?.SetValue(controller, null);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] stop vanilla music failed: " + e.Message);
        }
    }

    private static void StopOwn()
    {
        if (!_owning)
            return;

        try
        {
            if (_player != null && GodotObject.IsInstanceValid(_player))
                _player.Stop();
        }
        catch
        {
            // 忽略
        }

        _owning = false;
    }
}

/// <summary>
/// 进房间 / 开战 / 战斗结束都会走 UpdateTrack，正好当状态同步点。
///
/// 注意：NRunMusicController 有两个 UpdateTrack 重载（无参 / (string, float)），
/// 只写 nameof 会让 Harmony 抛 AmbiguousMatchException —— 一旦抛出，
/// 整个 PatchAll 会中断，本 mod 其它所有补丁（包括立即死归按钮）都会失效。
/// 所以两个重载都用显式参数类型分别挂。
/// </summary>
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new Type[0])]
public static class RunMusicUpdateTrackPatch
{
    private static void Postfix(NRunMusicController __instance)
    {
        SubaruMusic.OnTrackUpdated(__instance);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new[] { typeof(string), typeof(float) })]
public static class RunMusicUpdateTrackParamPatch
{
    private static void Postfix(NRunMusicController __instance)
    {
        SubaruMusic.OnTrackUpdated(__instance);
    }
}

/// <summary>挂在播放器上的哨兵节点：定时确认自己还该不该响。</summary>
public partial class SubaruMusicGuard : Node
{
    private double _accum;

    public override void _Process(double delta)
    {
        _accum += delta;
        if (_accum < 0.5)
            return;

        _accum = 0;
        SubaruMusic.Tick();
    }
}
