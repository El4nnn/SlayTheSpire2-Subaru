using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Potions;

/// <summary>
/// 多娜茶：罕见药水。抽牌，直到抽到一张[技能]牌为止（抽到的那张留在手里）。
///
/// 实现就是「一张一张抽，抽到技能牌就停」：
///   · 抽牌堆抽空（洗牌也抽不到）→ 停
///   · 手牌已满 → 停（再抽会溢出）
/// 抽牌走 <see cref="CardPileCmd.Draw(PlayerChoiceContext, MegaCrit.Sts2.Core.Entities.Players.Player)"/>，
/// 和「强欲的契约」每回合抽牌是同一个入口。
/// </summary>
public class DonnasTea : SubaruPotion
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.Self;

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        var player = Owner;
        var pcs = player?.PlayerCombatState;
        if (player == null || pcs == null)
            return;

        var drawn = 0;
        while (true)
        {
            if (pcs.Hand.Cards.Count >= CardPile.MaxCardsInHand)
                break;

            var card = await CardPileCmd.Draw(choiceContext, player);
            if (card == null)
                break;

            drawn++;

            if (card.Type == CardType.Skill)
            {
                Godot.GD.Print($"[Subaru] donna tea: drew {drawn} card(s), stopped at skill {card.Id}");
                return;
            }
        }

        Godot.GD.Print($"[Subaru] donna tea: drew {drawn} card(s), no skill found");
    }
}
