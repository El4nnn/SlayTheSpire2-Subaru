using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 柯尔·雷欧尼斯「每回合只能打出 1 张」的记账。
///
/// <para>为什么不塞进 <c>CorLeonisPower</c> 的 <c>Amount</c>：那三个状态位（欠债 / 升级 / 已武装）
/// 已经占满，而且这个限制的寿命是「本回合」，比能力本身（活到下一个玩家回合结束）短。</para>
///
/// <para>为什么不能只靠回合号判断：第 1 回合打出一张 → 死归回到第 1 回合，回合号一样，
/// 会被误判成「本回合已打出」，导致回档后这张牌永远打不出去。
/// 所以死归还原时由 <c>DeathReturnSpike</c> 显式调 <see cref="Reset"/>。</para>
/// </summary>
public static class CorLeonisTurnLedger
{
    private static ICombatState _state;
    private static Player _player;
    private static int _turn = -1;

    public static void MarkPlayed(ICombatState state, Player player)
    {
        if (state == null || player == null)
            return;

        _state = state;
        _player = player;
        _turn = player.PlayerCombatState?.TurnNumber ?? -1;
    }

    public static bool PlayedThisTurn(ICombatState state, Player player)
    {
        if (state == null || player == null || _state == null || _turn < 0)
            return false;

        if (!ReferenceEquals(_state, state) || !ReferenceEquals(_player, player))
            return false;

        return _turn == (player.PlayerCombatState?.TurnNumber ?? -2);
    }

    public static void Reset()
    {
        _state = null;
        _player = null;
        _turn = -1;
    }
}
