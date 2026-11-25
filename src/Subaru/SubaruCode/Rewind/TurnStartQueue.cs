using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「下回合开始时」类效果的队列（硬撑的脆弱、帕克的格挡、E·M·T 的能量）。
/// 不能直接在下回合开始加，因为回合开始的钩子不止一处，容易加两次。
/// </summary>
public static class TurnStartQueue
{
    private static int _frail;
    private static int _energy;
    private static int _block;
    private static int _vigor;

    public static void AddFrail(int amount)
    {
        _frail += amount;
        GD.Print($"[Subaru] pending frail +{amount} (total {_frail})");
    }

    /// <summary>下回合开始时回复能量（E·M·T）。</summary>
    public static void AddEnergy(int amount)
    {
        _energy += amount;
        GD.Print($"[Subaru] pending energy +{amount} (total {_energy})");
    }

    /// <summary>下回合开始时获得格挡（帕克）。</summary>
    public static void AddBlock(int amount)
    {
        _block += amount;
        GD.Print($"[Subaru] pending block +{amount} (total {_block})");
    }

    /// <summary>下回合开始时获得活力（福音书）。</summary>
    public static void AddVigor(int amount)
    {
        _vigor += amount;
        GD.Print($"[Subaru] pending vigor +{amount} (total {_vigor})");
    }

    public static void Reset()
    {
        _frail = 0;
        _energy = 0;
        _block = 0;
        _vigor = 0;
    }

    public static async Task Apply(PlayerChoiceContext ctx, Player player)
    {
        if (_frail > 0)
        {
            var amount = _frail;
            _frail = 0;

            try
            {
                await PowerCmd.Apply<FrailPower>(ctx, player.Creature, amount, player.Creature, null, false);
                GD.Print($"[Subaru] pending frail applied: {amount}");
            }
            catch (Exception e)
            {
                GD.PushError("[Subaru] pending frail failed: " + e);
            }
        }

        if (_energy > 0)
        {
            var amount = _energy;
            _energy = 0;

            try
            {
                player.PlayerCombatState?.GainEnergy(amount);
                GD.Print($"[Subaru] pending energy applied: +{amount}");
            }
            catch (Exception e)
            {
                GD.PushError("[Subaru] pending energy failed: " + e);
            }
        }

        if (_vigor > 0)
        {
            var amount = _vigor;
            _vigor = 0;

            try
            {
                await PowerCmd.Apply<VigorPower>(ctx, player.Creature, amount, player.Creature, null, false);
                GD.Print($"[Subaru] pending vigor applied: {amount}");
            }
            catch (Exception e)
            {
                GD.PushError("[Subaru] pending vigor failed: " + e);
            }
        }

        if (_block > 0)
        {
            var amount = _block;
            _block = 0;

            try
            {
                await CreatureCmd.GainBlock(player.Creature, amount, ValueProp.Move, null, false);
                GD.Print($"[Subaru] pending block applied: +{amount}");
            }
            catch (Exception e)
            {
                GD.PushError("[Subaru] pending block failed: " + e);
            }
        }
    }
}
