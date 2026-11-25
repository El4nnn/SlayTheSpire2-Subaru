using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 邪剑·村雨 对残杀千足虫的处理：不摘[接续]，改成等它真的复活完成后补 999 伤害。
/// 这样不用改它的回合流程，也不会出现「摘下状态后招式还挂在身上」那种半截状态。
/// 打死之后顺手把[接续]摘掉，避免它 2 回合后又复活一次、无限循环。
/// </summary>
[HarmonyPatch(typeof(ReattachPower), nameof(ReattachPower.DoReattach))]
public static class MurasameReattachPatch
{
    [HarmonyPostfix]
    public static void Postfix(ReattachPower __instance, ref Task __result)
    {
        __result = Wrap(__result, __instance);
    }

    private static async Task Wrap(Task inner, ReattachPower power)
    {
        await inner;

        try
        {
            var creature = power?.Owner;
            var player = creature?.CombatState?.Players?.FirstOrDefault();
            if (creature == null || player == null)
                return;

            if (!player.Relics.Any(r => r is CursedSwordMurasame))
                return;

            Godot.GD.Print("[Subaru] murasame: millipede reattached, dealing 999");

            await CreatureCmd.Damage(
                new BlockingPlayerChoiceContext(), creature, 999m, ValueProp.Unblockable, player.Creature);

            // 已经被打死了就把复活开关摘掉，否则它 2 回合后会再来一次
            var leftover = creature.Powers.FirstOrDefault(p => p is ReattachPower);
            if (leftover != null)
                await PowerCmd.Remove(leftover);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] murasame reattach patch failed: " + e);
        }
    }
}
