using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 魔女瘴气：诅咒牌。
/// 不可打出、保留、不可被丢弃 / 消耗 / 移出战斗；
/// 在手牌中回合结束失去 1 点生命；
/// 进入手牌时结算「权能10」：+10 层魔女遗香。
///
/// <para>2026-11-18：卡面词条合并为单一的[魔女的诅咒]。
/// 引擎的[保留]和[不可打出]关键词各自会顶一行字，所以两条都不再挂在 CanonicalKeywords 上：
/// 保留由 <see cref="Patches.MiasmaRetainPatch"/> 直接回答 ShouldRetainThisTurn，
/// 不可打出由这里的 IsPlayable 返回 false，其余防护见 <see cref="Patches.MiasmaGuard"/>。</para>
/// </summary>
public sealed class CurseWitchMiasma() : SubaruCard(-1, CardType.Curse, CardRarity.Curse, TargetType.None)
{
    public override HashSet<CardKeyword> CanonicalKeywords => [SubaruKeywords.WitchCurse];

    // 不可打出：CanPlay 会查 IsPlayable（也会查[不可打出]关键词，但那个关键词不再挂）
    protected override bool IsPlayable => false;

    // 2026-11-13：这张牌不再挂「权能」标签——那 20 层遗香是死归本身的代价，
    // 已经写进卡面描述，不该再显示成这张牌自己的权能。

    // 诅咒不可升级
    public override int MaxUpgradeLevel => 0;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Task.CompletedTask;
    }

}
