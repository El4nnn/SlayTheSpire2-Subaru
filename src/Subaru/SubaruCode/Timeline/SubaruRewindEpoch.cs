using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Timeline.Scaffolding;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「死亡回归」节点：累计发动 10 次死归后解锁，解锁的是「立即死归」按钮。
/// 时代与序位由 <see cref="SubaruTimeline.Register"/> 里的
/// <c>ModEpochTimelineSlot</c> 指定（模板里的 Era / EraPosition 是 sealed，不能重写）。
/// </summary>
public sealed class SubaruRewindEpoch : ModEpochTemplate
{
    public override string Id => "SUBARU_REWIND_EPOCH";

    /// <summary>这一条解锁后，Love 节点才出现在时间线上。</summary>
    public override EpochModel[] GetTimelineExpansion() =>
        SubaruEpochChain.Expand(SubaruEpochGate.LoveEpochId);
}
