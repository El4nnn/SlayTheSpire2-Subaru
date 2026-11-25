using System.Collections.Generic;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「移除魔女瘴气」的额度：每场战斗，每种移除方式只能用一次。
///
/// 每个效果用自己的 key 登记，互相独立。
/// 注意：癫狂的「清空瘴气」不在此列——它每次触发都会清，不受此限制。
/// </summary>
public static class CurseRemoval
{
    public const string Contract = "contract";      // 契约·贝蒂：移除 1 张
    public const string Hero = "hero";              // 英雄：消耗全部
    public const string FirstKnight = "first_knight"; // 首席骑士：消耗 1 张

    private static readonly HashSet<string> Used = new();

    /// <summary>申请一次额度：本场战斗第一次返回 true，之后返回 false。</summary>
    public static bool TryUse(string key)
    {
        var ok = Used.Add(key);

        Godot.GD.Print($"[Subaru] curse removal '{key}': {(ok ? "allowed" : "already used this combat")}");

        return ok;
    }

    /// <summary>查询本场战斗这个额度是否已经用过（不改状态）。</summary>
    public static bool IsUsed(string key) => Used.Contains(key);

    public static void Reset() => Used.Clear();
}
