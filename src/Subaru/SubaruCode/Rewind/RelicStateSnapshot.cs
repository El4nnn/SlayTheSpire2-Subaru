using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 遗物实例字段的快照（死归要用）。
///
/// <para><b>为什么需要</b>：遗物是跨战斗的长命对象，<c>BattleSnapshot</c> 只回生物/牌堆/能量，
/// 遗物自己身上的字段一律不动。而原版有大量遗物把「本场有没有用过」「本回合打了几张」
/// 记在实例字段里（投斧 <c>_usedThisCombat</c>、臂甲 <c>_blockGainedThisCombat</c>、
/// 律动残余 <c>_damageReceivedThisTurn</c>…）。死归后这些值停在死亡那一刻，
/// 表现就是「一场一次的效果被吞掉」或「上限/计数错位」（2026-11-17 筛查出 15 件 A 级）。</para>
///
/// <para><b>做法</b>：抓快照时把每件遗物的「简单字段」（bool/整数/浮点/字符串/枚举/它们的
/// Nullable）读出来存一份；回档时原样写回。引用型字段（卡牌、能力、Task、List…）一律跳过 ——
/// 那些由各自的快照负责，硬塞旧引用会造出「半死」对象。</para>
///
/// <para><b>顺序要求</b>：必须在「补跑 AfterSideTurnStart」<b>之前</b>还原。那些在
/// <c>AfterSideTurnStart</c> 里自增的计数器（开心小花 <c>_turnsSeen</c>）需要以存档点的值
/// 为起点重新数一遍，否则每次死归都会多计一次。</para>
/// </summary>
public static class RelicStateSnapshot
{
    private sealed class Entry
    {
        public RelicModel Relic;
        public readonly List<(FieldInfo Field, object Value)> Values = new();
    }

    private static readonly List<Entry> Entries = new();

    /// <summary>哪些字段值得存：值类型/字符串/枚举，且能整块写回。</summary>
    private static bool IsRestorable(Type t)
    {
        if (t == typeof(bool) || t == typeof(int) || t == typeof(long) || t == typeof(short)
            || t == typeof(byte) || t == typeof(float) || t == typeof(double) || t == typeof(decimal)
            || t == typeof(string) || t.IsEnum)
            return true;

        var underlying = Nullable.GetUnderlyingType(t);
        return underlying != null && IsRestorable(underlying);
    }

    /// <summary>把继承链上每一层的实例字段都取出来（GetFields 不会返回基类的私有字段）。</summary>
    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                          | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return f;
            }
        }
    }

    public static void Capture(Player player)
    {
        Entries.Clear();
        if (player == null)
            return;

        foreach (var relic in player.Relics)
        {
            if (relic == null)
                continue;

            var entry = new Entry { Relic = relic };

            foreach (var field in InstanceFields(relic.GetType()))
            {
                if (field.IsInitOnly || field.IsLiteral || !IsRestorable(field.FieldType))
                    continue;

                try
                {
                    entry.Values.Add((field, field.GetValue(relic)));
                }
                catch
                {
                    // 取不到的字段直接跳过，不影响其它字段
                }
            }

            if (entry.Values.Count > 0)
                Entries.Add(entry);
        }

        GD.Print($"[Subaru] relic state captured: {Entries.Count} relic(s), "
                 + $"{Entries.Sum(e => e.Values.Count)} field(s)");
    }

    public static void Restore(Player player)
    {
        if (player == null || Entries.Count == 0)
            return;

        var restored = 0;

        foreach (var entry in Entries)
        {
            if (entry.Relic == null)
                continue;

            // 遗物已经不在身上（被换掉/卖掉）就别写了
            if (!player.Relics.Contains(entry.Relic))
                continue;

            foreach (var (field, value) in entry.Values)
            {
                try
                {
                    if (Equals(field.GetValue(entry.Relic), value))
                        continue;

                    field.SetValue(entry.Relic, value);
                    restored++;
                }
                catch (Exception e)
                {
                    GD.PushWarning($"[Subaru] restore relic field failed: "
                                   + $"{entry.Relic.GetType().Name}.{field.Name}: {e.Message}");
                }
            }
        }

        GD.Print($"[Subaru] relic state restored: {restored} field(s)");
    }

    public static void Clear() => Entries.Clear();
}
