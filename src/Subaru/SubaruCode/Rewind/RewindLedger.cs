using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「记事本」：记录哪些状态不随死归回归，以及它们回档时应该保持的数值。
///
/// 用法：
///   RewindLedger.Register<MadnessPower>();          // 登记为「不回归」的状态
///   RewindLedger.Capture(player);                    // 回档前把当前数值记下来
///   await RewindLedger.Reapply(ctx, player);         // 回档后把数值盖回去
///
/// 注意：登记过的能力在 BattleSnapshot.RestoreCreature 里会被跳过「多余能力移除」，
/// 所以大多数情况下这里只需要改数值；真的整个能力被删掉时才走重建分支。
/// </summary>
public static class RewindLedger
{
    private sealed class Entry
    {
        public Type PowerType = null!;
        public int Amount;
    }

    private static readonly Dictionary<Type, int> Saved = new();
    private static readonly HashSet<Type> Registered = new();

    /// <summary>登记一种「不随死归回归」的状态。</summary>
    public static void Register<T>() where T : PowerModel
    {
        RegisterType(typeof(T));
    }

    /// <summary>登记一种「不随死归回归」的状态（非泛型，供「腐蚀」自动扫描用）。</summary>
    public static void RegisterType(Type type)
    {
        if (type == null)
            return;

        Registered.Add(type);
    }

    public static bool IsRegistered(Type t) => Registered.Contains(t);

    /// <summary>回档前：把这些状态的当前层数写进记事本。</summary>
    public static void Capture(Player player)
    {
        try
        {
            Saved.Clear();

            foreach (var power in player.Creature.Powers)
            {
                if (!Registered.Contains(power.GetType()))
                    continue;

                Saved[power.GetType()] = power.Amount;
            }

            GD.Print("[Subaru] ledger captured: " + Saved.Count + " entries");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] ledger capture failed: " + e);
        }
    }

    /// <summary>回档后：把记事本里的层数盖回去（能力没了就重建一个）。</summary>
    public static async Task Reapply(PlayerChoiceContext ctx, Player player)
    {
        try
        {
            foreach (var (type, amount) in Saved)
            {
                try
                {
                    var power = player.Creature.Powers.FirstOrDefault(p => p.GetType() == type);

                    if (power != null)
                    {
                        power.Amount = amount;
                        continue;
                    }

                    // 能力整个被删掉了：重建一个。走 PowerCmd.Apply 而不是手工 ToMutable，
                    // 免得拿不到可变实例时抛空引用（曾经就是这么把遗香弄丢的）。
                    if (ModelDb.Get(type) is not PowerModel canonical)
                    {
                        GD.PushWarning($"[Subaru] ledger: no canonical power for {type.Name}");
                        continue;
                    }

                    await PowerCmd.Apply(ctx, canonical, player.Creature, amount, null, null, false);
                    GD.Print($"[Subaru] ledger: recreated {type.Name} x{amount}");
                }
                catch (Exception e)
                {
                    GD.PushError($"[Subaru] ledger reapply {type.Name} failed: {e}");
                }
            }

            GD.Print("[Subaru] ledger reapplied: " + Saved.Count + " entries");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] ledger reapply failed: " + e);
        }
    }
}
