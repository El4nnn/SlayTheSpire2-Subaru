using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 埃尔·迷尼亚：普通，1 费，技能。[阴魔法]。升级：生成的两张[迷尼亚]也是升级版。
/// 向手牌加入 2 张[迷尼亚（临时）]——面板与迷尼亚完全一致，额外带[消耗]，
/// 只能由这张牌生成，不进任何卡池、也带不出战斗。
/// </summary>
public sealed class ElMinya() : SubaruCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    // table-upgrade: 生成的迷尼亚为升级版
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Count", 2m)];

    // 升级不改变数值，只让生成出来的迷尼亚是升级版（读 IsUpgraded）

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        var pcs = player?.PlayerCombatState;
        if (player == null || state == null || pcs == null)
            return;

        var count = (int)DynamicVars["Count"].BaseValue;

        for (var i = 0; i < count; i++)
        {
            var minya = state.CreateCard<MinyaTemp>(player);
            if (minya == null)
                continue;

            if (IsUpgraded)
                Extensions.CardUpgradeSync.UpgradeNow(minya);

            if (pcs.Hand.Cards.Count >= 10)
            {
                // 手牌满了就进抽牌堆顶，别把牌弄丢
                await CardPileCmd.Add(minya, pcs.DrawPile, CardPilePosition.Top, null, false);
            }
            else
            {
                await CardPileCmd.Add(minya, pcs.Hand, CardPilePosition.Top, null, false);
            }
        }

        Godot.GD.Print($"[Subaru] el minya: {count} temporary minya (upgraded={IsUpgraded})");
    }
}
