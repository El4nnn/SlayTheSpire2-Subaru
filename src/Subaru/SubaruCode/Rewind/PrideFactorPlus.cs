using MegaCrit.Sts2.Core.Entities.Relics;

namespace Subaru.SubaruCode.Rewind;

/// <summary>傲慢因子（升级版）：初始傲慢 4 层。其余与初始遗物相同。</summary>
public sealed class PrideFactorPlus : PrideFactorSpike
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override int StartingPride => 4;
}
