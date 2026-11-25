using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 鲜血帝·文森特：先古，能力，2 费，[永恒]。
/// 打出：召唤文森特（状态），并立即获得 1 张随机[九神将]；
/// 之后每 2 个回合开始时再获得 1 张（升级：加入的牌为升级版）。
///
/// 「只能从该会话获取」= <see cref="IsGenerateOnly"/>：不进卡池，
/// 只能由开局的涅奥契约页发放。
/// </summary>
public sealed class VincentContract() : SubaruCard(2, CardType.Power, CardRarity.Ancient, TargetType.Self)
{
    public override bool IsGenerateOnly => true;

    // 2026-11-18：只移除[固有]（[保留]留着），改成「第一回合开始后直接进手牌」
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Contract, CardKeyword.Retain, CardKeyword.Eternal];

    public override bool EntersHandAtCombatStart => true;

    // 升级不改费用：升级只让文森特之后塞进来的[九神将]变成升级版。
    // 能力用 Amount 记「1 = 普通 / 2 = 升级」，所以这里不需要动 DynamicVars。
    protected override void OnUpgrade()
    {
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        if (player == null)
            return;

        // 出场演出（2026-11-21 用户定）：只在打出这张契约时触发，击杀不触发。
        // 契约一局只打一次 → 招牌演出，不占每回合角色名额，只受「角色 cut-in」总开关管。
        await Vfx.CutIn.PlaySignatureAsync(player, "vincent");

        // 文森特在场：一个能力同时负责「显示状态」和「每 2 回合塞牌」。
        // Amount 记升级（1 = 普通 / 2 = 升级），层数不显示给玩家。
        await PowerCmd.Apply<VincentPower>(
            choiceContext, player.Creature, IsUpgraded ? 2m : 1m, player.Creature, this, false);

        // 打出时立即生效一次
        await VincentPower.GrantAsync(choiceContext, player, IsUpgraded);
    }
}
