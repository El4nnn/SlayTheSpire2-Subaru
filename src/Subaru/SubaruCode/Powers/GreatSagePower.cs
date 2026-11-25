using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Powers;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 大贤者的指引（腐蚀）：**死归加成额外多给一份**（2026-11-21 用户定：一共一份，不是每次翻倍）。
///
/// <para>层数没有意义（纯开关，升级只提高「减少遗香」那一半，这一条升级不变）→ Single。
/// 实际结算在 <c>Spike.ApplyRewindBlessing</c>：那里读这个能力是否存在，把应得的总份数 +1。</para>
///
/// <para>必须 <see cref="ICorrodedPower"/>：死归会把快照之后获得的能力一起还原掉，
/// 不加这条就等于「第一次死归之后大贤者失效」（和傲慢司教的加护同一个坑）。</para>
/// </summary>
public sealed class GreatSagePower : CustomPowerModel, ICorrodedPower
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "echidna_power.png".PowerImagePath();

    public override string CustomBigIconPath => "echidna_power.png".BigPowerImagePath();
}
