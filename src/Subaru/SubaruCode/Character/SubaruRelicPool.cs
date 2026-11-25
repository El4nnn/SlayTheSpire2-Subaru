using System.Collections.Generic;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Character;

public class SubaruRelicPool : CustomRelicPoolModel
{
    public override Color LabOutlineColor => Subaru.Color;

    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();

    /// <summary>本池的遗物默认都算「已见」，图鉴里不会显示未知。</summary>
    public override bool SeenByDefault => true;

    // 注意：不要在这里导入游戏原生的通用遗物。
    // UnlockState.Relics 本来就是「所有遗物池的并集」，通用遗物会自动出现；
    // 再导入一遍只会造成重复/权重翻倍。
    // 专属遗物以后在这里 yield 即可。
}
