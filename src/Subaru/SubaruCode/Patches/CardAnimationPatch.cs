using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 拦掉原版给玩家角色发的 <c>Attack</c> 触发。
///
/// 原版出牌路径对**所有**攻击牌都发一个 <c>Attack</c>，不看牌型，也没有 <c>Cast</c>；
/// 而我们要按「魔法攻击 / 普通攻击」区分播哪一条，所以把原版这条统一拦掉，
/// 改由 <see cref="Cards.SubaruCard"/> 在出牌时自己决定（cast 或 attack）。
/// 只针对菜月昴，其它角色不受影响。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), "TriggerAnim")]
public static class CardAnimationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature creature, string triggerName, ref Task __result)
    {
        try
        {
            if (triggerName != "Attack")
                return true;

            if (creature == null || !creature.IsPlayer || creature.Player == null)
                return true;

            if (creature.Player.Character is not Subaru.SubaruCode.Character.Subaru)
                return true;

            // 关键：TriggerAnim 返回 Task，跳过原方法时必须自己给一个已完成的 Task。
            // 不声明 ref __result 时它会保持 null，调用方 await 一下就 NullReferenceException
            // （表现为攻击牌打出去卡在屏幕上、伤害不结算）。
            __result = Task.CompletedTask;
            return false;
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] card anim gate failed: " + e.Message);
            return true;
        }
    }
}
