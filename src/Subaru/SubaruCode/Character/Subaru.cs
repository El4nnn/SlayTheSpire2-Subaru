using System.Collections.Generic;
using BaseLib.Abstracts;
using BaseLib.Utils.NodeFactories;
using STS2RitsuLib.Scaffolding.Characters;
using Subaru.SubaruCode.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Relics;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Character;

/// <summary>
/// 菜月昴。
///
/// <para>
/// <b>必须实现 <see cref="IModCharacterEpochTimelineRequirement"/></b>（2026-11-19）：
/// RitsuLib 的「15 首领 / 15 精英」解锁补丁第一句是
/// <c>if (!IsOwnedOrUsesTimelinePolicy(character)) return true;</c>（放行原版），
/// 而「归属」只有两种：角色注册在 RitsuLib 的 ModContentRegistry，或角色实现本接口。
/// 昴是 BaseLib 的 PlaceholderCharacterModel，从来没走 RitsuLib 注册
/// （<c>SubaruTimeline.Register</c> 只交 Story / Epoch / 解锁规则），也没实现接口
/// → 补丁放行原版 → 原版 <c>CheckFifteenBossesDefeatedEpoch</c> 里是一串
/// <c>isinst 铁甲战士/静默猎手/…</c>，模组角色落到 else 直接
/// <c>throw new ArgumentOutOfRangeException("character")</c>。
///
/// 结果就是「每次用昴打掉 BOSS 或精英都在局后结算里抛异常」，剑狼的 10 次首领
/// 判定一次都没跑过（玩家通关多次也解锁不了）。实现接口后 RitsuLib 会接管这两条检查，
/// 换成我们注册的 10 次规则。启动时会有一条
/// "[Content] Unregistered character uses RitsuLib timeline policy" 的 warn，属正常。
/// </para>
/// </summary>
public class Subaru : PlaceholderCharacterModel, IModCharacterEpochTimelineRequirement
{
    public const string CharacterId = "Subaru";

    /// <summary>这个角色要用 RitsuLib 的时间线（epoch）机制。</summary>
    public bool RequiresEpochAndTimeline => true;
    
    /// <summary>
    /// 角色主题色（紫）。这个名字颜色、遗物/药水池的实验室描边都用它；
    /// 卡背、卡组小卡、能量描边在 SubaruCardPool 里各有一处对应的颜色。
    /// 色值沿用「立即死归」按钮的紫 #A966FF，保持整套 UI 一致。
    /// </summary>
    public static readonly Color Color = new("a966ff");

    public override Color NameColor => Color;
#if GAME_BETA
    // 0.113 才有：0.107 的 CharacterModel 没有这个属性
    public override Color DreadNameColor => Color;
#endif
    public override Color DialogueColor => Color;
    public override Color MapDrawingColor => Color;
    public override Color RemoteTargetingLineColor => Color;
    public override Color EnergyLabelOutlineColor => Color;
    public override CharacterGender Gender => CharacterGender.Neutral;
    // 低血量是设计：普通怪打不穿，精英/BOSS 必然打穿 → 把死归变成精英与 BOSS 的必经流程。
    //
    // 2026-11-20 用户定：**单人 / 多人统一 50**。之前多人给 +10，但那时候的判据
    // （MultiplayerSupport.IsMultiplayer）在开局创建期间会误判成多人，结果单人开局也是 60；
    // 与其修判据、不如直接把这条补偿去掉 —— 多人里血一样，就不存在判错的余地。
    public override int StartingHp => 56;
    
    // 按卡表：打击 ×4、防御 ×4、纱幕 ×1、基尔提之鞭 ×1
    // （契约·贝蒂 不在初始牌组：它和[鲜血帝·文森特]都由开局的涅奥契约页二选一发放）
    public override IEnumerable<CardModel> StartingDeck =>
    [
        ModelDb.Card<StrikeSubaru>(),
        ModelDb.Card<StrikeSubaru>(),
        ModelDb.Card<StrikeSubaru>(),
        ModelDb.Card<StrikeSubaru>(),
        ModelDb.Card<DefendSubaru>(),
        ModelDb.Card<DefendSubaru>(),
        ModelDb.Card<DefendSubaru>(),
        ModelDb.Card<DefendSubaru>(),
        ModelDb.Card<Shamak>(),
        ModelDb.Card<Guiltywhip>(),
    ];

    public override IReadOnlyList<RelicModel> StartingRelics =>
    [
        ModelDb.Relic<PrideFactorSpike>()
    ];
    
    public override CardPoolModel CardPool => ModelDb.CardPool<SubaruCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<SubaruRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<SubaruPotionPool>();
    
    /*  自定义立绘：走和奶蛙一样的路子 —— 工程里装了官方 spine GDExtension，
        .atlas/.spine-json 能被编辑器正常导入，所以直接用 .tscn 场景即可。 */
    public override string CustomVisualPath => "res://Subaru/scenes/creature_visuals/subaru.tscn";

    // 商店立绘：**故意不提供**（2026-11-20）。
    // 不设置时商店显示的是原版的占位角色；玩家若装了商人美化 mod（如 Merchant2CuteII）就套他自己的。
    // 曾经这里指向 res://Subaru/scenes/merchant/subaru_merchant.tscn（根节点 SpineSprite），
    // 而 RitsuLib 的 NMerchantRoomProceduralCharacterInstantiationPatch 会把这个场景
    // 直接 Instantiate<NMerchantCharacter>() 硬转 —— 根节点不是 NMerchantCharacter 就抛
    // InvalidCastException，异常发生在 NMerchantRoom._Ready() 里 → 按钮没接好 →
    // 进场时 OnActiveScreenUpdated 空引用 → MoveToMapCoordAction 中断 → 进商店卡死。
    // 我们本来就不做商人形象，直接不提供这条路径最干净。

    /// <summary>休息处（篝火）的立绘。不设置的话显示原版角色（铁甲战士）。</summary>
    public override string CustomRestSiteAnimPath => "res://Subaru/scenes/rest_site/subaru_rest.tscn";

    // CustomCharacterModel 要求实现：打建筑师时的特效路径，我们先给空列表（没有专属特效）
    public override List<string> GetArchitectAttackVfx() => [];

    public override Control CustomIcon
    {
        get
        {
            var icon = NodeFactory<Control>.CreateFromResource(CustomIconTexturePath);
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            return icon;
        }
    }
    public override string CustomIconTexturePath => "character_icon_char_name.png".CharacterUiPath();
    public override string CustomCharacterSelectIconPath => "char_select_char_name.png".CharacterUiPath();
    public override string CustomCharacterSelectLockedIconPath => "char_select_char_name_locked.png".CharacterUiPath();
    public override string CustomMapMarkerPath => "map_marker_char_name.png".CharacterUiPath();

    /// <summary>
    /// 战斗内的能量球：BaseLib 用「层号 → 图片路径」的函数把五张图叠起来
    /// （对应原版 energy_counters/&lt;角色&gt;/&lt;角色&gt;_orb_layer_1..5.png 那套结构）。
    ///
    /// 素材是手绘油画六芒星：第 1 层是主体（描边已经烘焙在图里），
    /// 第 2 层是一圈很淡的柔光，第 3 层留空——数字直接压在笔触上，不额外垫核心盘。
    /// </summary>
    private const string EnergyLayerFormat = "res://Subaru/images/charui/energy/subaru_orb_layer_{0}.png";

    private static bool _energyLayerLogged;

    public override CustomEnergyCounter? CustomEnergyCounter => new(
        i =>
        {
            if (!_energyLayerLogged)
            {
                _energyLayerLogged = true;
                GD.Print($"[Subaru] energy counter layer requested: {i}");
            }

            return string.Format(EnergyLayerFormat, i);
        },
        new Godot.Color("160a20"),
        Color);

    /// <summary>
    /// 角色选择界面的背景。注意 CharacterModel.CharacterSelectBg 返回的是
    /// **场景路径**（引擎那边是 GetScene(path).Instantiate&lt;Control&gt;()），
    /// 不是贴图路径，所以这里给的是一个只含 Control + TextureRect 的小场景。
    ///
    /// BaseLib 的补丁：CustomCharacterSelectBg 非 null 就用它，null 则回落到原版。
    /// 场景缺失时返回 null，避免把缺少资源的路径直接丢给引擎。
    /// </summary>
    private const string CharSelectBgScene = "res://Subaru/scenes/char_select/char_select_bg_subaru.tscn";

    private static bool _bgLogged;

    public override string CustomCharacterSelectBg
    {
        get
        {
            var ok = ResourceLoader.Exists(CharSelectBgScene);

            if (!_bgLogged)
            {
                _bgLogged = true;
                MainFile.Logger.Info($"[Subaru] character select bg: {CharSelectBgScene} exists={ok}");
            }

            return ok ? CharSelectBgScene : null;
        }
    }
}