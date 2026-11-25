using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Timeline.Scaffolding;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「魔女时代的终结」节点：用菜月昴通关一次后解锁，
/// 解锁内容列出三张牌：神龙·波尔肯尼卡、大贤者、初代剑圣·雷德。
/// 时代与序位由 <see cref="SubaruTimeline.Register"/> 指定（模板里的 Era / EraPosition 是 sealed）。
/// </summary>
public sealed class SubaruWitchEndEpoch : CardUnlockEpochTemplate
{
    public override string Id => "SUBARU_WITCH_END_EPOCH";

    protected override IEnumerable<Type> CardTypes =>
    [
        typeof(Volcanica),
        typeof(GreatSage),
        typeof(Reid),
    ];

    /// <summary>这一条解锁后，帝国的剑狼 / 崩坏的开始 才出现在时间线上。</summary>
    public override EpochModel[] GetTimelineExpansion() =>
        SubaruEpochChain.Expand(
            SubaruEpochGate.SwordWolfEpochId,
            SubaruEpochGate.CollapseEpochId);
}
