using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 本场战斗的计数器（阿尔·纱幕要看本场打出的技能牌数）。
///
/// 所有「自增」入口都过 <see cref="MainThreadGuard"/>：第三方求解器会在线程池线程里
/// 模拟战斗，模拟里的出牌/伤害同样会调到我们的 hook。非主线程的调用一律不记账，
/// 否则塞蕾丝缇雅 / 阿尔·纱幕的计数会被模拟抬高（2026-11-17 玩家日志实证）。
/// </summary>
public static class CombatStats
{
    public static int SkillsPlayed { get; private set; }

    /// <summary>死归回档时把技能计数退回存档点的值（和 <see cref="SetDamageGiven"/> 同一个用法）。</summary>
    public static void SetSkillsPlayed(int value) => SkillsPlayed = value < 0 ? 0 : value;

    /// <summary>本回合开始时的生命值，-1 表示还没记过。</summary>
    private static int _hpAtTurnStart = -1;

    /// <summary>
    /// 贤者·夏乌拉：**已经打出过的那些牌**（第一次打是 2 段）。
    ///
    /// <para>2026-11-21 用户澄清：卡面写的「本场战斗第一次打出此牌」指的是**这一张牌本身**，
    /// 不是「同名牌」。所以两张夏乌拉各自都算第一次 —— 用「牌实例」记账，而不是一个全局 bool。</para>
    ///
    /// <para>必须用 <see cref="ReferenceEqualityComparer"/>：<c>CardModel</c> 若按内容判等，
    /// 两张同名牌会被当成同一张。</para>
    /// </summary>
    private static readonly HashSet<MegaCrit.Sts2.Core.Models.CardModel> ShaulaPlayed =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>这张牌是不是本场第一次打出。</summary>
    public static bool IsShaulaFirstPlay(MegaCrit.Sts2.Core.Models.CardModel card) =>
        card != null && !ShaulaPlayed.Contains(card);

    public static void MarkShaulaPlayed(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null || !MainThreadGuard.IsMainThread)
            return;

        ShaulaPlayed.Add(card);
    }

    /// <summary>死归后重置：夏乌拉「本场第一次打出」的判定也要回到起点。</summary>
    public static void ResetShaula()
    {
        ShaulaPlayed.Clear();
    }

    /// <summary>
    /// 立绘 cut-in 本回合的出场记账（按角色 id）。
    ///
    /// <para>2026-11-21 用户定的规则：**每角色每回合一次**、**每回合最多 N 个不同角色**
    /// （默认 3，设置页「角色 cut-in」里可调，0 = 关闭）。每回合开始、死归回档都要清空。</para>
    ///
    /// <para>用 id 做键而不是 bool：以后每个角色各有各的立绘，互不影响。</para>
    /// </summary>
    private static readonly HashSet<string> CutInsThisTurn = [];

    /// <summary>这个角色本回合已经出过场吗。</summary>
    public static bool HasCutInThisTurn(string id) =>
        !string.IsNullOrEmpty(id) && CutInsThisTurn.Contains(id);

    /// <summary>本回合已经出场过的角色数。</summary>
    public static int CutInsThisTurnCount => CutInsThisTurn.Count;

    /// <summary>
    /// 给这个角色占一个「本回合出场」名额。已经有记录的直接放行（不占新名额）；
    /// 名额用尽返回 false。
    /// </summary>
    public static bool TryTakeCutInSlot(string id, int maxDistinct)
    {
        if (string.IsNullOrEmpty(id) || !MainThreadGuard.IsMainThread)
            return false;

        if (CutInsThisTurn.Contains(id))
            return true;

        if (CutInsThisTurn.Count >= (maxDistinct < 0 ? 0 : maxDistinct))
            return false;

        CutInsThisTurn.Add(id);
        return true;
    }

    public static void ResetCutInsThisTurn()
    {
        CutInsThisTurn.Clear();
    }

    public static void OnSkillPlayed()
    {
        if (!MainThreadGuard.IsMainThread)
            return;

        SkillsPlayed++;
    }

    /// <summary>
    /// 本场战斗「造成过伤害」的次数（过路魔·塞蕾丝缇雅的 N）。
    /// 每出现一次 DamageResult（未被打空、即真的掉了血）就 +1。
    /// 死归会把计数退回到存档点时的值。
    /// </summary>
    public static int DamageGiven { get; private set; }

    public static void OnDamageGiven()
    {
        if (!MainThreadGuard.IsMainThread)
            return;

        DamageGiven++;
    }

    public static void SetDamageGiven(int value) => DamageGiven = value < 0 ? 0 : value;

    public static void OnTurnStart(int currentHp)
    {
        if (!MainThreadGuard.IsMainThread)
            return;

        _hpAtTurnStart = currentHp;

        // 立绘 cut-in：每回合重置「哪些角色已经出过场」
        CutInsThisTurn.Clear();
    }

    /// <summary>本回合是否已经受过伤（三分的加护用）。</summary>
    public static bool TookDamageThisTurn(Creature? creature)
    {
        if (creature == null || _hpAtTurnStart < 0)
            return false;

        return creature.CurrentHp < _hpAtTurnStart;
    }

    public static void Reset()
    {
        SkillsPlayed = 0;
        _hpAtTurnStart = -1;
        ShaulaPlayed.Clear();
        CutInsThisTurn.Clear();
        DamageGiven = 0;
    }
}
