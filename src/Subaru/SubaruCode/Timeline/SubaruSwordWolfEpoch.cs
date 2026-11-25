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
/// 「帝国的剑狼」节点：以菜月昴身份累计击败 10 个首领后揭示。
///
/// 解锁的是整个阵营——佛拉基亚：九神将十人 + 鲜血帝·文森特。
/// 时代与序位由 <see cref="SubaruTimeline.Register"/> 指定；
/// 解锁规则由 RitsuLib 的 UnlockEpochAfterBossVictories 负责
/// （原来挂的是「每日挑战」，但塔2的每日挑战是指定角色，菜月昴摸不到）。
///
/// 原版（和 RitsuLib 的模板）的解锁文本只读前三个解锁项，
/// 所以这里重写 <see cref="UnlockText"/>，改成「解锁阵营：佛拉基亚」+ 全部十一张牌。
/// </summary>
public sealed class SubaruSwordWolfEpoch : CardUnlockEpochTemplate
{
    public override string Id => SubaruEpochGate.SwordWolfEpochId;

    /// <summary>帝国军阵营：原九神将十人 + 鲜血帝·文森特 + 战虫将·卡夫马。</summary>
    public static readonly Type[] Faction =
    [
        typeof(SpiritEater),
        typeof(BlueLightning),
        typeof(SoulMarriage),
        typeof(Orbart),
        typeof(Gurobi),
        typeof(Madeleine),
        typeof(Moguro),
        typeof(Gotz),
        typeof(Ballroy),
        typeof(Kishia),
        typeof(Kafma),
        typeof(Kulgan),
        typeof(VincentContract),
    ];

    protected override IEnumerable<Type> CardTypes => Faction;

    /// <summary>这一条解锁后，「大灾」（邀约7）才出现在时间线上。</summary>
    public override EpochModel[] GetTimelineExpansion() =>
        SubaruEpochChain.Expand(SubaruEpochGate.CalamityEpochId);

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
                // 取不到卡名就退回只显示阵营行，别把时间线界面弄崩
                return new LocString("epochs", $"{Id}.faction").GetFormattedText();
            }
        }
    }
}
