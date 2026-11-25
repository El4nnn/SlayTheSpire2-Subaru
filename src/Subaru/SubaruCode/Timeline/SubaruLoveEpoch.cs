using System;
using System.Collections.Generic;
using STS2RitsuLib.Timeline.Scaffolding;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「Love love love love love love YOU」节点：累计死归 99 次后解锁。
/// 解锁内容：嫉妒魔女·莎缇拉、大贤者、迷失。
/// 时代与序位由 <see cref="SubaruTimeline.Register"/> 指定。
/// </summary>
public sealed class SubaruLoveEpoch : CardUnlockEpochTemplate
{
    public override string Id => SubaruEpochGate.LoveEpochId;

    protected override IEnumerable<Type> CardTypes =>
    [
        typeof(SatellaWitch),
        typeof(GreatSage),
        typeof(Lost),
    ];
}
