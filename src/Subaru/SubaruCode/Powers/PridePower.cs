using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>傲慢：癫狂的阈值层数。</summary>
public sealed class PridePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => false;

    public override string CustomPackedIconPath => "pride_power.png".PowerImagePath();

    public override string CustomBigIconPath => "pride_power.png".BigPowerImagePath();

    // 关键：只允许一个实例。默认的 Instanced 会为每次 Apply 新建一份，
    // 而伤害修正按「所有实例的倍率求和」，多份就会变成 12×1.5 这种爆炸。
    public override PowerInstanceType InstanceType => PowerInstanceType.None;
}
