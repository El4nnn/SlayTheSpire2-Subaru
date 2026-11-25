using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 晕眩（对敌人）：让它跳过下一次行动。
///
/// 游戏里没有「通用晕眩敌人」的接口——每只怪只有自己写了晕眩招才能被晕（实际上一只都没有）。
/// 所以这里自己造一个「空招」塞进它的招式状态机：
///   1) 造一个什么都不做的 MoveState（意图图标用官方 StunIntent）
///   2) 记住它本来要出的招，作为这个空招的后续
///   3) SetMoveImmediate 强制切过去 —— 意图立刻显示晕眩
///   4) 它行动时执行空招，然后状态机顺着「后续」回到原本的招
/// 这样既不破坏它的出招循环，也不用改游戏代码。
///
/// 注意：不挂状态图标。晕眩的视觉提示就是怪物头顶的意图图标，状态栏里不再重复显示。
/// </summary>
public static class StunHelper
{
    private const string StunStateId = "__SUBARU_STUN";

    public static void ApplyStun(Creature creature)
    {
        try
        {
            var monster = creature.Monster;
            var machine = monster?.MoveStateMachine;
            if (monster == null || machine == null)
            {
                GD.PushWarning("[Subaru] stun: target is not a monster");
                return;
            }

            // 它本来要出的招：晕完接着出这一招
            var originalId = monster.NextMove?.StateId;

            machine.States.TryGetValue(StunStateId, out var existing);

            var followUp = originalId;
            if (string.IsNullOrEmpty(followUp) || followUp == StunStateId)
            {
                // 已经在晕眩状态又被晕：沿用之前记下的后续招
                followUp = (existing as MoveState)?.FollowUpStateId;
            }

            // FollowUpStateId 是 init-only，只能构造时给，所以每次重建一个
            var stun = new MoveState(StunStateId, _ => Task.CompletedTask, new StunIntent())
            {
                FollowUpStateId = followUp,
                MustPerformOnceBeforeTransitioning = false,
            };

            machine.States[StunStateId] = stun;
            monster.SetMoveImmediate(stun, true);

            GD.Print($"[Subaru] stunned {creature.Name}: will resume '{stun.FollowUpStateId}'");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] stun failed: " + e);
        }
    }
}
