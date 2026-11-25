using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Timeline.Scaffolding;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「大灾」节点（邀约 7）：时间线上最后一条，挂在「帝国的剑狼」后面。
/// 解锁「大灾」阵营 —— 核心卡「大灾·斯芬克斯」与整条【尸兵】卡池。
///
/// <para>2026-11-18 对齐其他节点：改成 <see cref="CardUnlockEpochTemplate"/>（带 <see cref="CardTypes"/>），
/// 时间线里会像「帝国的剑狼」一样自动列出「解锁阵营：大灾 + 卡名」，
/// 解锁规则也改由 <see cref="SubaruTimeline.Register"/> 里的声明式规则（<c>UnlockEpochAfterWinAs</c>）负责，
/// 不再用自写的 <c>RunManager.WinRun</c> 补丁。</para>
///
/// <para>时代与序位由 <see cref="SubaruTimeline.Register"/> 指定（模板里的 Era / EraPosition 是 sealed）。</para>
/// </summary>
public sealed class SubaruCalamityEpoch : CardUnlockEpochTemplate
{
    public override string Id => SubaruEpochGate.CalamityEpochId;

    /// <summary>
    /// 大灾阵营：尸兵 + 两张不带尸兵词条的阵营牌（大灾形态、不死王的洗礼）。
    /// </summary>
    public static readonly Type[] Faction =
    [
        typeof(Luan),
        typeof(Yugard),
        typeof(Lamia),
        typeof(MultiForm),
        typeof(CorpseBallroy),
        typeof(CorpseSphinx),
        typeof(Valgren),
        typeof(Librey),
        typeof(CalamityForm),
        typeof(UndyingKingBaptism),
    ];

    protected override IEnumerable<Type> CardTypes => Faction;

    /// <summary>终点节点，不再展开子节点。</summary>
    public override EpochModel[] GetTimelineExpansion() => [];

    /// <summary>
    /// 和「帝国的剑狼」同一套做法：解锁文案 = <c>&lt;id&gt;.faction</c> + 换行 + 卡名（用 factionSeparator 连接）。
    /// </summary>
    public override string UnlockText
    {
        get
        {
            try
            {
                var head = new LocString("epochs", $"{Id}.faction").GetFormattedText();
                var separator = new LocString("epochs", $"{Id}.factionSeparator").GetFormattedText();

                var names = Faction
                    .Select(type => ModelDb.GetById<CardModel>(ModelDb.GetId(type)))
                    .Where(card => card != null)
                    .Select(card => $"[gold]{card.Title}[/gold]");

                return head + "\n" + string.Join(separator, names);
            }
            catch (Exception)
            {
                // 取不到卡名就退回只显示阵营行
                return new LocString("epochs", $"{Id}.faction").GetFormattedText();
            }
        }
    }
}
