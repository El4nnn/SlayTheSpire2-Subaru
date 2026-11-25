using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Subaru.SubaruCode.Character;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Potions;

[Pool(typeof(SubaruPotionPool))]
public abstract class SubaruPotion : CustomPotionModel
{
	public override string? CustomPackedImagePath =>
		$"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".PotionImagePath();
	public override string? CustomPackedOutlinePath =>
		$"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".PotionOutlineImagePath();
}