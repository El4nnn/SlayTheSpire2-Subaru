using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 迷失（能力牌，0 费，稀有）：把死归的存档点定在「打出它的这一刻」，并获得 1 层[傲慢]；
/// 此后每回合开始时多抽 1 张牌。[腐蚀]，所以那份「多抽一张」跨死归保留。权能 3。
/// 升级：获得[保留]。
///
/// 因为游戏的原生存档只到房间粒度，无法存战斗中途，
/// 这里改为在内存里记录一份「玩家状态快照」：
/// 死亡时把生命 / 格挡 / 能力恢复到这一刻，而不是回退整个回合。
/// </summary>
public sealed class Lost() : SubaruCard(0, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    // 【腐蚀】：每回合多抽的那张跨死归保留（能力实现 ICorrodedPower）
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Authority, SubaruKeywords.Corrosion];

    protected override int Authority => 7;

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var state = CombatState;
            var player = Owner;
            if (state == null || player == null)
            {
                GD.PushError("[Subaru] Lost: combat state is null");
                return;
            }

            var turn = player.PlayerCombatState?.TurnNumber ?? -1;

            // 先结算傲慢，再抓快照：这样存档点里就带着这层数，回档后不会凭空少一层
            await PowerCmd.Apply<PridePower>(choiceContext, player.Creature, 1m, player.Creature, this, false);

            // 权能 3 也提前结算——否则回档会把 3 层遗香退回去
            await ApplyAuthorityNow(choiceContext);

            // 先挂标记再抓快照：图标必须和存档点本身同步，慢一步就会显示成旧回合
            // （「每回合多抽 1 张」已经并进这个状态里了，不再单独挂 LostDriftPower）
            await SetSavepointMark(choiceContext, player, turn);

            DeathReturnSpike.SetManualSavepoint(state);
            GD.Print($"[Subaru] manual savepoint set at turn {turn}, pride +1");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] Lost failed: " + e);
        }

        await Task.CompletedTask;
    }

    /// <summary>升级：获得[保留]。</summary>
    protected override void OnUpgrade()
    {
        try
        {
            AddKeyword(CardKeyword.Retain);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] Lost upgrade failed: " + e);
        }
    }

    /// <summary>把「存档点第几回合」写进状态栏；多张迷失以最后一张为准。</summary>
    private async Task SetSavepointMark(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Players.Player player, int turn)
    {
        var existing = player.Creature.Powers.FirstOrDefault(p => p is LostSavepointPower);

        if (existing != null)
        {
            existing.Amount = turn;
            existing.AmountOnTurnStart = turn;
            GD.Print("[Subaru] savepoint mark overwritten: turn " + turn);
            return;
        }

        await PowerCmd.Apply<LostSavepointPower>(choiceContext, player.Creature, System.Math.Max(1, turn), player.Creature, this, false);

        var mark = player.Creature.Powers.FirstOrDefault(p => p is LostSavepointPower);
        if (mark != null)
        {
            mark.Amount = turn;
            mark.AmountOnTurnStart = turn;
        }
    }
}
