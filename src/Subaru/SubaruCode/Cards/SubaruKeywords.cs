using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 自定义卡牌关键词。
///
/// BaseLib 会在所有模型注册完之后，把带 [CustomEnum] 的字段赋成新生成的枚举值，
/// 所以这里不要自己写初始值。文字走 localization/eng/card_keywords.json 里的 SUBARU-* 键。
///
/// AutoKeywordPosition.None：不要让引擎把「权能」两个字自动拼到描述末尾——
/// 卡面里本来就写了「权能 3」，自动追加会变成「…权能 3权能」。
/// </summary>
public static class SubaruKeywords
{
    /// <summary>
    /// 权能 N：打出后增加 N 层[魔女遗香]。
    /// N 是每张牌自己的数值，写在卡面描述里，所以关键词本身不带数字。
    /// </summary>
    [CustomEnum("AUTHORITY")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Authority;

    // [BEATRICE]「贝蒂」词条已取消（2026-11-12）：
    // 贝蒂是每局开局必打的牌，「贝蒂在场才能打」没有实际意义。
    // 字段连同本地化一起删掉，卡面上不会再出现这个词条。

    /// <summary>
    /// 腐蚀：整场战斗只能打出一次；打出后再死归，这张牌从本场战斗移除（战斗结束回到牌组）。
    /// 但它授予的能力不随死归重置——这是「进入后续轮回」的部分。
    /// Before：跟[固有][保留]一样单独占一行。
    /// </summary>
    [CustomEnum("CORROSION")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword Corrosion;

    /// <summary>
    /// 阴魔法：贝蒂在场时，你每回合打出的第一张带[阴魔法]词条的牌费用 -1。
    /// 折扣逻辑在 BeatricePower.TryModifyEnergyCostInCombat 里（贝蒂不在场就没有折扣）。
    /// </summary>
    [CustomEnum("YIN_MAGIC")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword YinMagic;

    /// <summary>
    /// 九神将：本回合打出任意一张[九神将]后，本回合下一张[九神将]费用 -1。
    /// 折扣逻辑在 NineGeneralsPower 里（打出第一张时挂上的隐藏能力）。
    /// 目前成员：食精灵者·亚拉基亚、蓝色闪电·塞西鲁斯、极彩色·夜鸣、
    /// 毒辣翁·奥尔巴特、咒具师·葛路比、飞龙将·玛德琳（都由「帝国的剑狼」解锁）。
    /// </summary>
    [CustomEnum("NINE_GENERALS")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword NineGenerals;

    /// <summary>
    /// 尸兵：大灾阵营的成员标签。它不是规则性词条（本身不给任何效果），
    /// 作用是「被别的效果引用」——斯芬克斯会从所有[尸兵]里随机取一张加入手牌。
    /// 2026-11-18 随「天剑·劳安」一起加入。
    /// </summary>
    [CustomEnum("CORPSE_SOLDIER")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword CorpseSoldier;

    /// <summary>
    /// 契约：开局在涅奥处签下的三张先古能力牌（贝蒂 / 文森特 / 大灾）。
    /// 本身不给效果，只统一描述它们的进出牌堆方式 ——
    /// 战斗开始时压到抽牌堆最底，第一回合抽牌结束后进入手牌，不占起手抽牌位。
    /// 实现见 <see cref="AncientContractHand"/>。
    /// 2026-11-18 加入。
    /// </summary>
    [CustomEnum("CONTRACT")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword Contract;

    /// <summary>
    /// 魔女的诅咒：[魔女瘴气]专用。把这张牌原本挂在卡面上的几条规则并成一条 ——
    /// 不可打出、保留、不可被丢弃 / 消耗 / 移出战斗。
    /// 行为分别落在 <see cref="CurseWitchMiasma.IsPlayable"/>、
    /// <c>Patches.MiasmaRetainPatch</c> 和 <c>Patches.MiasmaGuard</c> 里。
    /// 2026-11-18 加入。
    /// </summary>
    [CustomEnum("WITCH_CURSE")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword WitchCurse;
}
