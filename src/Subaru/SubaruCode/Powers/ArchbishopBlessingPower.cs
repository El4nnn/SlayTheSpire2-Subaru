using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Powers;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 傲慢司教的加护（腐蚀）：每次死归后额外获得「2 × 层数」点力量与「1 × 层数」点敏捷。
///
/// <para>2026-11-19：改成可叠层（<see cref="PowerStackType.Counter"/>）。
/// 以前层数没有意义、纯开关；现在每多打一张傲慢司教，死归奖励就多一份。
/// 实际结算在 <see cref="DeathReturnSpike"/>（ApplyRewindBlessing 读 Amount）。</para>
/// </summary>
public sealed class ArchbishopBlessingPower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override string CustomPackedIconPath => "pride_power.png".PowerImagePath();

    public override string CustomBigIconPath => "pride_power.png".BigPowerImagePath();

    public override PowerInstanceType InstanceType => PowerInstanceType.None;
}
