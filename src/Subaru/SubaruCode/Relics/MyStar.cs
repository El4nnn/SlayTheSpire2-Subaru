using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 吾星：事件「爱丽丝与荆棘之王」专属遗物（<see cref="RelicRarity.Event"/>，不进随机奖励池）。
///
/// 每次死归回档后回复 6 点生命。结算点在 <see cref="Rewind.DeathReturnSpike"/> 的还原流程末尾 ——
/// 快照会把回档前的治疗抹掉，必须等还原完成之后再补，和「贝蒂的守护」的格挡同理。
///
/// <para><b>2026-11-19 多人版</b>：多人没有死归，改成<b>战斗开始时</b>回一次 6 点（按缺失血量封顶）。
/// 单机路径不变。</para>
/// </summary>
public class MyStar : SubaruRelic
{
    /// <summary>每次死归回档后回复的生命。</summary>
    public const int HealAfterRewind = 6;

    public override RelicRarity Rarity => RelicRarity.Event;

    /// <summary>多人：战斗开始时回一次血。</summary>
    public override async Task BeforeCombatStart()
    {
        if (!MultiplayerSupport.IsMultiplayer)
            return;

        try
        {
            var creature = Owner?.Creature;
            if (creature == null)
                return;

            var missing = creature.MaxHp - creature.CurrentHp;
            if (missing <= 0)
                return;

            var heal = Math.Min(HealAfterRewind, missing);
            if (heal <= 0)
                return;

            await CreatureCmd.Heal(creature, heal, true);
            GD.Print($"[Subaru] mp: my star healed {heal} at combat start "
                     + $"(hp={creature.CurrentHp}/{creature.MaxHp})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] mp my star failed: " + e);
        }
    }
}
