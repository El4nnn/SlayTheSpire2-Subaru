using System;
using System.Collections.Generic;
using STS2RitsuLib.Timeline.Scaffolding;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「暴食」节点：用菜月昴累计打赢 15 场精英战后解锁。
/// 解锁内容：大兔·增殖、黑蛇·疫病、白鲸·消失之雾（魔女留下的三大魔兽）。
/// 时代与序位由 <see cref="SubaruTimeline.Register"/> 指定。
/// </summary>
public sealed class SubaruGluttonyEpoch : CardUnlockEpochTemplate
{
    public override string Id => SubaruEpochGate.GluttonyEpochId;

    protected override IEnumerable<Type> CardTypes =>
    [
        typeof(Otogi),
        typeof(Kurohebi),
        typeof(Hakugei),
    ];
}
