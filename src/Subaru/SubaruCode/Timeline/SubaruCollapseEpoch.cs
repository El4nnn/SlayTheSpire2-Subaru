using System;
using System.Collections.Generic;
using STS2RitsuLib.Timeline.Scaffolding;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「崩坏的开始」节点：在「魔女的契约」事件里选择把手掌贴上去（签订契约）后解锁。
/// 解锁内容：虚饰魔女·潘多拉、忧郁魔人·赫克特、宫廷小丑。
/// 前置：魔女时代的终结。
/// </summary>
public sealed class SubaruCollapseEpoch : CardUnlockEpochTemplate
{
    public override string Id => SubaruEpochGate.CollapseEpochId;

    protected override IEnumerable<Type> CardTypes =>
    [
        typeof(PandoraWitch),
        typeof(Hector),
        typeof(RoswaalFriend),
    ];
}
