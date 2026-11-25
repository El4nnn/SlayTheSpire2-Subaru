using System;
using System.Text.Json;
using Godot;

namespace Subaru.SubaruCode.Settings;

/// <summary>
/// 视觉类设置：「打击特效」总开关（2026-11-20 加）+「击杀特效」开关。
///
/// <para>存在 <c>user://subaru_visual.json</c>，和 RitsuLib 设置页之间用
/// <c>ModSettingsBindings.Callback</c> 对接（写法和 <see cref="SubaruAudioSettings"/> 一致）——
/// 读/写/保存都由这里控制，所以关掉开关下一只怪立刻就不再播特效。</para>
///
/// <para><b>打击特效总开关</b>管的是「除了音效和击杀特效以外的所有演出」：
/// 顿帧 / 抖屏 / 镜头推 / 命中光爆 / 受击闪白（<see cref="Vfx.HitFeel"/>）、
/// 法术弹幕与「等弹幕命中才出伤」的同步（<see cref="Vfx.SpellVolley"/>）、
/// 八重·天膳的立绘切入与切屏演出（<see cref="Vfx.CutIn"/> / <see cref="Vfx.ScreenCut"/>）、等待结算时的卡牌虚化（<see cref="Vfx.CardVisuals"/>）。
/// 关掉之后这些系统各自的 <c>Enabled</c> 会一起读成 false，卡牌退回原版结算节奏。</para>
/// </summary>
public static class SubaruVisualSettings
{
    private const string FilePath = "user://subaru_visual.json";

    private sealed class Data
    {
        public bool KillEffectEnabled { get; set; } = true;

        /// <summary>打击特效总开关（旧的存档里没有这个字段 → 反序列化后是 false，得靠默认值兜住）。</summary>
        public bool? HitFxEnabled { get; set; }

        /// <summary>
        /// 击杀特效亮度（百分比，50–150）。
        /// 必须可空：老存档里没有这个字段，反序列化成 <c>int</c> 会是 <b>0</b> → 整段特效全黑。
        /// </summary>
        public int? KillEffectBrightnessPercent { get; set; }

        /// <summary>
        /// 创作者模式（2026-11-21）：战斗左上角出现三个测试按钮。
        /// 同样必须可空：老存档缺字段时按「关」处理，不能靠 bool 默认值。
        /// </summary>
        public bool? CreatorModeEnabled { get; set; }

        /// <summary>角色立绘 cut-in 总开关（2026-11-21）。默认开。</summary>
        public bool? CutInEnabled { get; set; }

        /// <summary>同一角色一回合能不能出场多次（默认不能）。</summary>
        public bool? CutInRepeatSameCharacter { get; set; }

        /// <summary>每回合最多几个不同角色出场（0 = 关闭，默认 3）。</summary>
        public int? CutInMaxDistinctPerTurn { get; set; }
    }

    private static Data _data = new();
    private static bool _loaded;

    /// <summary>
    /// 打击特效总开关。默认开；老存档里没这个字段时按「开」处理
    /// （所以 <see cref="Data.HitFxEnabled"/> 用可空 bool，避免反序列化把缺省值变成 false）。
    /// </summary>
    public static bool HitFxEnabled
    {
        get { EnsureLoaded(); return _data.HitFxEnabled ?? true; }
        set
        {
            EnsureLoaded();
            if ((_data.HitFxEnabled ?? true) == value)
                return;

            _data.HitFxEnabled = value;
            Save();
        }
    }

    /// <summary>击杀特效（白闪 + 紫色剪影定格 + 裂缝 + 碎片）总开关。</summary>
    public static bool KillEffectEnabled
    {
        get { EnsureLoaded(); return _data.KillEffectEnabled; }
        set
        {
            EnsureLoaded();
            if (_data.KillEffectEnabled == value)
                return;

            _data.KillEffectEnabled = value;
            Save();
        }
    }

    /// <summary>
    /// 击杀特效亮度（百分比，50–150，默认 100）。
    ///
    /// <para>每次播放时读，所以改完滑杆下一只怪立刻生效。作用在「白闪 / 剪影 / 裂纹 / 碎块」上；
    /// 碎块之间的缝是纯黑（乘什么都是 0），不受影响。</para>
    ///
    /// <para>老存档没有这个字段 → 反序列化后是 <c>null</c> → 按 100 处理。
    /// 这就是 <see cref="Data.KillEffectBrightnessPercent"/> 必须写成 <c>int?</c> 的原因。</para>
    /// </summary>
    public static int KillEffectBrightnessPercent
    {
        get
        {
            EnsureLoaded();
            return Math.Clamp(_data.KillEffectBrightnessPercent ?? 100, 50, 150);
        }
        set
        {
            var clamped = Math.Clamp(value, 50, 150);
            EnsureLoaded();
            if ((_data.KillEffectBrightnessPercent ?? 100) == clamped)
                return;

            _data.KillEffectBrightnessPercent = clamped;
            Save();
        }
    }

    /// <summary>
    /// 创作者模式（2026-11-21）：打开后战斗左上角多出三个测试按钮
    /// （加一张牌 / 能量 +3 / 生命上限 +10），只在菜月昴的战斗里出现。
    ///
    /// <para>默认关；切换后**下一场战斗**生效（按钮在战斗开始时创建）。
    /// 老存档没有这个字段 → 可空 bool → 按「关」处理。</para>
    /// </summary>
    public static bool CreatorModeEnabled
    {
        get { EnsureLoaded(); return _data.CreatorModeEnabled ?? false; }
        set
        {
            EnsureLoaded();
            if ((_data.CreatorModeEnabled ?? false) == value)
                return;

            _data.CreatorModeEnabled = value;
            Save();
        }
    }

    /// <summary>
    /// 角色立绘 cut-in 总开关（2026-11-21 用户定）：默认开；老存档缺字段按「开」处理。
    ///
    /// <para>只管立绘出场，和「打击特效」「击杀特效」是两个独立开关 —— 关掉立绘之后
    /// 切屏、射线、击杀特效照常播。</para>
    /// </summary>
    public static bool CutInEnabled
    {
        get { EnsureLoaded(); return _data.CutInEnabled ?? true; }
        set
        {
            EnsureLoaded();
            if ((_data.CutInEnabled ?? true) == value)
                return;

            _data.CutInEnabled = value;
            Save();
        }
    }

    /// <summary>
    /// 同一角色一回合能不能出场多次。默认**不能**（用户 2026-11-21 定的规则：每角色每回合一次）。
    /// </summary>
    public static bool CutInRepeatSameCharacter
    {
        get { EnsureLoaded(); return _data.CutInRepeatSameCharacter ?? false; }
        set
        {
            EnsureLoaded();
            if ((_data.CutInRepeatSameCharacter ?? false) == value)
                return;

            _data.CutInRepeatSameCharacter = value;
            Save();
        }
    }

    /// <summary>
    /// 每回合最多几个**不同**角色出场（0–9，0 = 关闭，默认 3）。
    /// 老存档缺字段 → <c>int?</c> 为 null → 按 3 处理（写成 <c>int</c> 会变成 0 = 悄悄关掉功能）。
    /// </summary>
    public static int CutInMaxDistinctPerTurn
    {
        get
        {
            EnsureLoaded();
            return Math.Clamp(_data.CutInMaxDistinctPerTurn ?? 3, 0, 9);
        }
        set
        {
            var clamped = Math.Clamp(value, 0, 9);
            EnsureLoaded();
            if ((_data.CutInMaxDistinctPerTurn ?? 3) == clamped)
                return;

            _data.CutInMaxDistinctPerTurn = clamped;
            Save();
        }
    }

    public static void Save()
    {
        try
        {
            using var file = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushWarning("[Subaru] visual settings: cannot open " + FilePath);
                return;
            }

            file.StoreString(JsonSerializer.Serialize(_data));
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] visual settings save failed: " + e.Message);
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

            GD.Print($"[Subaru] visual settings loaded: killEffect={_data.KillEffectEnabled}, "
                     + $"killBrightness={_data.KillEffectBrightnessPercent ?? 100}%, "
                     + $"hitFx={_data.HitFxEnabled ?? true}, "
                     + $"cutIn={_data.CutInEnabled ?? true}, "
                     + $"cutInMax={_data.CutInMaxDistinctPerTurn ?? 3}, "
                     + $"cutInRepeat={_data.CutInRepeatSameCharacter ?? false}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] visual settings load failed, using defaults: " + e.Message);
        }
    }
}
