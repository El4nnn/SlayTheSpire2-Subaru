using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 邪剑·村雨 对实验体的处理：它没有「复活状态」，复活是写死的流程（Revive 换阶段血量），
/// 所以不能靠摘 Power。改成等它复活完成后，立刻吃一发「50% 最大生命」的伤害。
/// 直接拦掉复活会让第二、三阶段整段消失，战斗流程会异常。
/// </summary>
[HarmonyPatch(typeof(TestSubject), nameof(TestSubject.Revive))]
public static class MurasameRevivePatch
{
    [HarmonyPostfix]
    public static void Postfix(TestSubject __instance, ref Task __result)
    {
        __result = Wrap(__result, __instance);
    }

    private static async Task Wrap(Task inner, TestSubject monster)
    {
        await inner;

        try
        {
            var creature = monster?.Creature;
            var player = creature?.CombatState?.Players?.FirstOrDefault();
            if (creature == null || player == null)
                return;

            if (!player.Relics.Any(r => r is CursedSwordMurasame))
                return;

            var damage = creature.MaxHp * 0.5m;
            Godot.GD.Print($"[Subaru] murasame: test subject revived, taking {damage} damage");

            await CreatureCmd.Damage(
                new BlockingPlayerChoiceContext(), creature, damage, ValueProp.Unblockable, player.Creature);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushError("[Subaru] murasame revive patch failed: " + e);
        }
    }
}
