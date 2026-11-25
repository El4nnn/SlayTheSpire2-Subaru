using BaseLib.Abstracts;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Character;

public class SubaruPotionPool : CustomPotionPoolModel
{
    public override Color LabOutlineColor => Subaru.Color;
    

    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();
}