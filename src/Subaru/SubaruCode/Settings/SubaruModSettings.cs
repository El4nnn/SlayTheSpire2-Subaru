using System;
using System.Runtime.CompilerServices;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace Subaru.SubaruCode.Settings;

/// <summary>
/// 在 RitsuLib 的「模组设置」子菜单里给 Subaru 挂一页。
/// 目前只有基础信息（模组名 / 页面标题 / 关于卡片），实际可调项以后往
/// <see cref="Configure"/> 里加 section 和条目。
/// </summary>
public static class SubaruModSettings
{
    /// <summary>页面 ID：侧边栏和跳转都用它，别改。</summary>
    public const string PageId = "overview";

    // BaseLib 只加载固定的表名（cards / relics / settings_ui …），
    // 自己新建 settings.json 不会被加载 —— 曾经因此整页都在显示代码里的兜底文本。
    private const string Table = "settings_ui";

    /// <summary>
    /// 注册设置页。RitsuLib 太老（没有这套设置框架）时只记一条日志，不影响其它功能。
    /// </summary>
    public static void Register()
    {
        try
        {
            // 单独一个方法：万一玩家的 RitsuLib 里没有 RitsuLibFramework，
            // 类型加载失败发生在 RegisterCore 的 JIT 阶段，这里还能接住。
            RegisterCore();
            MainFile.Logger.Info("[Subaru] mod settings page registered via RitsuLib");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn("[Subaru] mod settings register failed: " + e.GetBaseException().Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterCore()
    {
        RitsuLibFramework.RegisterModSettings(MainFile.ModId, Configure, PageId);
    }

    private static void Configure(ModSettingsPageBuilder page)
    {
        page.WithModDisplayName(Text("MOD_NAME", "菜月昴"));
        page.WithTitle(Text("TITLE", "设置"));
        page.WithDescription(Text("DESCRIPTION", "菜月昴模组的选项。这里目前只有基础信息。"));

        page.AddSection("audio", section =>
        {
            section.WithTitle(Text("SECTION_AUDIO_TITLE", "音频"));

            // 开关 1：是否用我们的战斗 BGM 替换原版音乐。
            // 绑定走 Callback：读/写/保存都是我们自己的，所以改完立刻作用到正在播的音乐。
            section.AddToggle(
                "audio_bgm_replace",
                Text("AUDIO_BGM_REPLACE", "替换战斗 BGM"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "audio.bgmReplace",
                    () => SubaruAudioSettings.ReplaceBattleBgm,
                    v => SubaruAudioSettings.ReplaceBattleBgm = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                Text("AUDIO_BGM_REPLACE_DESC", "关闭后播放原版战斗音乐。"),
                null);

            // 开关 2：打击音效（单体 / 多段 / 群攻 / 多段群攻 四条）
            section.AddToggle(
                "audio_hit_sfx",
                Text("AUDIO_HIT_SFX", "打击音效"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "audio.hitSfx",
                    () => SubaruAudioSettings.HitSfxEnabled,
                    v => SubaruAudioSettings.HitSfxEnabled = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                Text("AUDIO_HIT_SFX_DESC", "关闭后命中不再出声（顿帧、光爆、击杀特效不受影响）。"),
                null);

            // 滑轮 1：死归音效音量（0–100%，100% 就是原来的默认响度）
            section.AddIntSlider(
                "audio_rewind_volume",
                Text("AUDIO_REWIND_VOLUME", "死归音效音量"),
                ModSettingsBindings.Callback<int>(
                    MainFile.ModId,
                    "audio.rewindVolume",
                    () => SubaruAudioSettings.RewindVolumePercent,
                    v => SubaruAudioSettings.RewindVolumePercent = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                0, 100, 5,
                v => v + "%",
                Text("AUDIO_REWIND_VOLUME_DESC", "0% 为静音，100% 为默认音量。"));
        });

        // 只有「替换战斗 BGM」打开时才有意义的几项，单独一段并跟着开关显示/隐藏
        page.AddSection("audio_replace", section =>
        {
            section.WithTitle(Text("SECTION_AUDIO_REPLACE_TITLE", "替换细节"));
            section.WithVisibleWhen(() => SubaruAudioSettings.ReplaceBattleBgm);

            section.AddToggle(
                "audio_vocal_intro",
                Text("AUDIO_VOCAL_INTRO", "播放开头人声段"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "audio.vocalIntro",
                    () => SubaruAudioSettings.PlayVocalIntro,
                    v => SubaruAudioSettings.PlayVocalIntro = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                Text("AUDIO_VOCAL_INTRO_DESC", "关掉则直接从乐器段开始。"),
                null);

            section.AddIntSlider(
                "audio_vocal_volume",
                Text("AUDIO_VOCAL_VOLUME", "人声段音量"),
                ModSettingsBindings.Callback<int>(
                    MainFile.ModId,
                    "audio.vocalVolume",
                    () => SubaruAudioSettings.VocalVolumePercent,
                    v => SubaruAudioSettings.VocalVolumePercent = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                0, 100, 5,
                v => v + "%",
                Text("AUDIO_VOCAL_VOLUME_DESC", "只作用于开头那一段人声。"));

            section.AddIntSlider(
                "audio_loop_volume",
                Text("AUDIO_LOOP_VOLUME", "循环段音量"),
                ModSettingsBindings.Callback<int>(
                    MainFile.ModId,
                    "audio.loopVolume",
                    () => SubaruAudioSettings.LoopVolumePercent,
                    v => SubaruAudioSettings.LoopVolumePercent = v,
                    SubaruAudioSettings.Save,
                    SaveScope.Global),
                0, 100, 5,
                v => v + "%",
                Text("AUDIO_LOOP_VOLUME_DESC", "乐器段（循环）的音量。"));
        });

        page.AddSection("visual", section =>
        {
            section.WithTitle(Text("SECTION_VISUAL_TITLE", "特效"));

            // 打击特效总开关（2026-11-20）：顿帧 / 抖屏 / 命中光爆 / 受击闪白 / 弹幕 / 切屏 / 卡牌虚化。
            // 音效和击杀特效各有自己的开关，不受这一项影响。
            section.AddToggle(
                "visual_hit_fx",
                Text("VISUAL_HIT_FX", "打击特效"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "visual.hitFx",
                    () => SubaruVisualSettings.HitFxEnabled,
                    v => SubaruVisualSettings.HitFxEnabled = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                Text("VISUAL_HIT_FX_DESC",
                    "顿帧、抖屏、镜头推、命中光爆、受击闪白、法术弹幕（含「等弹幕命中才出伤」）、"
                    + "八重·天膳的切屏演出、等待结算时的卡牌虚化。关闭后这些一律不播，卡牌按原版节奏立刻结算。"
                    + "（打击音效与击杀特效不受影响，各有独立开关。）"),
                null);

            // 击杀特效：白闪 + 紫色剪影定格 + 裂缝 + 碎片。
            // 绑定走 Callback，关掉后下一只怪立刻不再播（不用等下一场战斗）。
            section.AddToggle(
                "visual_kill_effect",
                Text("VISUAL_KILL_EFFECT", "击杀特效"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "visual.killEffect",
                    () => SubaruVisualSettings.KillEffectEnabled,
                    v => SubaruVisualSettings.KillEffectEnabled = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                Text("VISUAL_KILL_EFFECT_DESC",
                    "击杀时白闪 + 角色与敌人变成紫色剪影、敌人裂开碎掉。和普通怪同一条规则：清空场上最后一只「非爪牙」敌人的那一击是完整版，"
                    + "其余击杀是短版；只杀召唤物不触发；多人模式不播。"),
                null);

            // 滑杆：击杀特效亮度（2026-11-21）。100% = 原来的样子；改完下一只怪立刻生效。
            section.AddIntSlider(
                "visual_kill_brightness",
                Text("VISUAL_KILL_BRIGHTNESS", "击杀特效亮度"),
                ModSettingsBindings.Callback<int>(
                    MainFile.ModId,
                    "visual.killBrightness",
                    () => SubaruVisualSettings.KillEffectBrightnessPercent,
                    v => SubaruVisualSettings.KillEffectBrightnessPercent = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                50, 150, 5,
                v => v + "%",
                Text("VISUAL_KILL_BRIGHTNESS_DESC",
                    "50%–150%，默认 100%。作用于白闪、剪影、裂纹和碎块；碎块之间的缝始终是纯黑。"));
        });

        page.AddSection("cutin", section =>
        {
            section.WithTitle(Text("SECTION_CUTIN_TITLE", "角色 cut-in"));

            // 总开关（2026-11-21 用户定）：默认开，只管立绘出场。
            section.AddToggle(
                "cutin_enabled",
                Text("CUTIN_ENABLED", "角色 cut-in"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "cutin.enabled",
                    () => SubaruVisualSettings.CutInEnabled,
                    v => SubaruVisualSettings.CutInEnabled = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                Text("CUTIN_ENABLED_DESC",
                    "出牌时角色立绘切入画面（剪影 + 双框 + 立绘 + 遮罩）。默认开启。"
                    + "关掉后立绘一律不播（屏幕切割、射线、击杀特效各有自己的开关，不受影响）。"),
                null);

            // 同角色能不能一回合出多次（默认不能）
            section.AddToggle(
                "cutin_repeat",
                Text("CUTIN_REPEAT", "同一角色可重复出场"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "cutin.repeat",
                    () => SubaruVisualSettings.CutInRepeatSameCharacter,
                    v => SubaruVisualSettings.CutInRepeatSameCharacter = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                Text("CUTIN_REPEAT_DESC",
                    "关（默认）：每个角色每回合只出场一次。开：同一角色一回合可以出场多次。"),
                null);

            // 每回合最多几个不同角色（0–9，0 = 关闭）
            section.AddIntSlider(
                "cutin_max",
                Text("CUTIN_MAX", "每回合最多不同角色"),
                ModSettingsBindings.Callback<int>(
                    MainFile.ModId,
                    "cutin.max",
                    () => SubaruVisualSettings.CutInMaxDistinctPerTurn,
                    v => SubaruVisualSettings.CutInMaxDistinctPerTurn = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                0, 9, 1,
                v => v.ToString(),
                Text("CUTIN_MAX_DESC",
                    "0–9，默认 3。0 = 关闭（不出立绘）。"
                    + "超出上限后本回合不再出立绘；终结那一击不受这个上限限制。"));
        });

        page.AddSection("creator", section =>
        {
            section.WithTitle(Text("SECTION_CREATOR_TITLE", "创作者模式"));

            // 测试用开关（2026-11-21）：打开后战斗左上角出现三个按钮。
            // 按钮在战斗开始时创建，所以切换后下一场战斗生效。
            section.AddToggle(
                "creator_mode",
                Text("CREATOR_MODE", "创作者模式"),
                ModSettingsBindings.Callback<bool>(
                    MainFile.ModId,
                    "creator.enabled",
                    () => SubaruVisualSettings.CreatorModeEnabled,
                    v => SubaruVisualSettings.CreatorModeEnabled = v,
                    SubaruVisualSettings.Save,
                    SaveScope.Global),
                Text("CREATOR_MODE_DESC",
                    "打开后，菜月昴的战斗左上角会出现三个测试按钮："
                    + "「加一张牌」（从全部卡牌里挑一张直接进手牌）、「能量 +3」、「生命上限 +10」。"
                    + "只影响你自己的战斗，和存档一起保存；正式游玩请关掉。切换后下一场战斗生效。"),
                null);
        });

        page.AddSection("info", section =>
        {
            section.WithTitle(Text("SECTION_INFO_TITLE", "关于"));
            section.AddInfoCard(
                "about",
                Text("ABOUT_TITLE", "菜月昴（Subaru Natsuki）"),
                Text("ABOUT_BODY", "死亡回归 / 权能 / 魔女遗香。"),
                Text("ABOUT_DESCRIPTION", "仅兼容抢先体验版 0.113.x。"));
        });
    }

    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.LocString(Table, "SUBARU-SETTINGS." + key, fallback);
}
