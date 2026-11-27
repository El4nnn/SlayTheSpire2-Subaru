using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 「帝国的剑狼」自建计数器（2026-11-25）。
///
/// <para>
/// <b>为什么不再依赖 RitsuLib 的 <c>UnlockEpochAfterBossVictories</c></b>：
/// 那条规则只在 <c>ProgressSaveManager.UpdateAfterCombatWon</c> 里算一次，
/// 错过就不补；而它的计数源（<c>EncounterStats[id].FightStats[character].Wins</c>）
/// 只要有一处对不上，结果就永远是 0，且没有任何日志能看出来。
/// 玩家反馈「A0 打到 A10 都没解锁」正是这种「静默失败」的形态。
/// </para>
///
/// <para>现在改成自己算、自己存：</para>
/// <list type="number">
///   <item>每次读档 / 战斗开始 / 战斗结束都重算「历史首领胜利数」（<see cref="DerivedWins"/>）；</item>
///   <item>把它们与我们自己落盘的计数取较大值（<see cref="Wins"/>），只增不减——
///         即使引擎把统计洗掉，也不会倒退；</item>
///   <item>≥ <see cref="RequiredWins"/> 就发放，写 <c>Revealed</c>，不依赖玩家去时间线点一下；</item>
///   <item>老玩家补偿：<c>MaxAscension &gt;= <see cref="CompensationAscension"/></c> 直接发放
///         （A4 = 至少通关 4 局 = 至少 12 个首领，不会误发）。</item>
/// </list>
/// </summary>
public static class SwordWolfProgress
{
    /// <summary>解锁需要的首领胜利数。</summary>
    public const int RequiredWins = 10;

    /// <summary>补偿门槛：已解锁进阶 4（= 至少通关 4 局 ≈ 12 个首领）。</summary>
    public const int CompensationAscension = 4;

    private const string SavePath = "user://subaru_swordwolf.json";

    private static int _wins = -1;
    private static bool _hooked;
    private static bool _loggedOnce;

    /// <summary>当前计数值（自有存档与历史统计的较大值）。</summary>
    public static int Wins
    {
        get
        {
            EnsureLoaded();
            return _wins;
        }
    }

    /// <summary>读档 / 战斗开始 / 战斗结束统一入口。</summary>
    public static void Reconcile(string source)
    {
        try
        {
            EnsureLoaded();
            EnsureHook();

            var derived = DerivedWins(out var bossCount);
            if (derived > _wins)
            {
                _wins = derived;
                Persist();
            }

            var maxAscension = MaxAscensionOfSubaru();

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                GD.Print($"[Subaru] sword wolf: derived={derived}, stored={_wins}, " +
                         $"required={RequiredWins}, maxAscension={maxAscension}, " +
                         $"bossEncounters={bossCount}, characterId={ModelDb.GetId(typeof(Character.Subaru))}");
            }

            // 老玩家补偿：已解锁进阶 4 → 直接发放（A4 意味着至少 4 次通关、约 12 个首领）
            if (maxAscension >= CompensationAscension)
            {
                Grant($"maxAscension={maxAscension} (compensation)");
                return;
            }

            if (_wins >= RequiredWins)
                Grant($"{_wins}/{RequiredWins} boss wins");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sword wolf reconcile failed (" + source + "): " + e.Message);
        }
    }

    /// <summary>
    /// 战斗结束（首领战会传 true）：先按统计同步，统计没涨就用自己的计数器 +1。
    /// 这样即使引擎的战斗统计没有记到昴名下，我们也能自己累计。
    /// </summary>
    public static void OnCombatEnded(bool bossWon)
    {
        try
        {
            EnsureLoaded();
            EnsureHook();

            var derived = DerivedWins(out _);
            if (derived > _wins)
            {
                _wins = derived;
                Persist();
            }
            else if (bossWon)
            {
                _wins++;
                Persist();
                GD.Print($"[Subaru] sword wolf: own counter +1 -> {_wins}/{RequiredWins}");
            }

            Reconcile("combat end");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sword wolf combat-end failed: " + e.Message);
        }
    }

    /// <summary>兜底同步（战斗管理器事件）：只按统计对齐，不主动 +1。</summary>
    private static void SyncFromStats()
    {
        try
        {
            EnsureLoaded();
            var derived = DerivedWins(out _);
            if (derived > _wins)
            {
                _wins = derived;
                Persist();
                Reconcile("combat ended (event)");
            }
        }
        catch
        {
            // 忽略：读档时还会再算
        }
    }

    private static void Grant(string reason)
    {
        if (SubaruEpochGate.IsObtained(SubaruEpochGate.SwordWolfEpochId)
            && SubaruEpochGranter.IsRevealed(SubaruEpochGate.SwordWolfEpochId))
        {
            return;
        }

        GD.Print($"[Subaru] sword wolf unlocked: {reason}");
        SubaruEpochGranter.Grant<SubaruSwordWolfEpoch>(SubaruEpochGate.SwordWolfEpochId);
    }

    /// <summary>从存档统计里数「以菜月昴身份击败过的首领数」。</summary>
    public static int DerivedWins(out int bossEncounters)
    {
        bossEncounters = 0;

        var progress = SaveManager.Instance?.Progress;
        if (progress?.EncounterStats == null)
            return 0;

        var characterId = ModelDb.GetId(typeof(Character.Subaru));
        var bossIds = new HashSet<ModelId>();

        foreach (var act in ModelDb.Acts)
        {
            if (act?.AllBossEncounters == null)
                continue;

            foreach (var encounter in act.AllBossEncounters)
            {
                if (encounter != null)
                    bossIds.Add(encounter.Id);
            }
        }

        bossEncounters = bossIds.Count;

        var total = 0;
        foreach (var stats in progress.EncounterStats.Values)
        {
            if (stats?.FightStats == null || !bossIds.Contains(stats.Id))
                continue;

            foreach (var fight in stats.FightStats)
            {
                if (fight != null && fight.Character == characterId)
                {
                    total += fight.Wins;
                    break; // 每个遭遇同一角色只有一条记录
                }
            }
        }

        return total;
    }

    private static int MaxAscensionOfSubaru()
    {
        try
        {
            var progress = SaveManager.Instance?.Progress;
            if (progress == null)
                return 0;

            var stats = progress.GetStatsForCharacter(ModelDb.GetId(typeof(Character.Subaru)));
            return stats?.MaxAscension ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void EnsureHook()
    {
        if (_hooked)
            return;

        try
        {
            var manager = CombatManager.Instance;
            if (manager == null)
                return;

            manager.CombatEnded += _ => SyncFromStats();
            _hooked = true;
        }
        catch
        {
            // 拿不到战斗管理器就算了，读档时仍会补算
        }
    }

    private static void EnsureLoaded()
    {
        if (_wins >= 0)
            return;

        _wins = 0;

        try
        {
            if (!Godot.FileAccess.FileExists(SavePath))
                return;

            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
                return;

            var text = file.GetAsText();
            var marker = text.IndexOf("\"wins\"", StringComparison.Ordinal);
            if (marker < 0)
                return;

            var colon = text.IndexOf(':', marker);
            if (colon < 0)
                return;

            var digits = string.Empty;
            for (var i = colon + 1; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsDigit(c))
                    digits += c;
                else if (digits.Length > 0)
                    break;
            }

            if (digits.Length > 0 && int.TryParse(digits, out var value) && value > 0)
                _wins = value;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sword wolf counter load failed: " + e.Message);
        }
    }

    private static void Persist()
    {
        try
        {
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString($"{{\n  \"wins\": {_wins}\n}}\n");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sword wolf counter save failed: " + e.Message);
        }
    }
}
