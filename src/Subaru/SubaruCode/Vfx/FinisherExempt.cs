using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 「终结特效」判定时要**放行**的怪名单（2026-11-21 用户定）。
///
/// <para>判定口径是「这一击之后场上不再有活着的**非爪牙**敌人」，但这些怪死后会
/// **再生成敌人**（例：地精佣兵爆出两只小地精），那一刻场上的活怪被判成爪牙 →
/// 误判成终结，于是放了灰白底 / 圆 / 0.5s 停顿，而且把死亡流程挂住一秒。</para>
///
/// <para>名单命中 → 一律按**普通击杀**处理（夏乌拉 / 八重 / 贝蒂 / 通用击杀特效都走这一条）。
/// 加新怪只改下面这个表：一行一只，注释写中文名，值用**模型类型名**（不是中文名）。</para>
/// </summary>
internal static class FinisherExempt
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "GremlinMerc",      // 地精佣兵：死后爆出两只小地精
        "PhrogParasite",    // 异蛙寄生虫
    };

    /// <summary>
    /// 实验体（<c>TestSubject</c>）「第三形态」对应的 <c>Respawns</c> 值。
    /// 它的贴图/姿态是按 Respawns 选 idle_loop1/2/3（0/1/2），所以第三形态 = 2。
    /// </summary>
    private const int TestSubjectFinalRespawns = 2;

    /// <summary>
    /// 实验体还停在「会原地复活」的形态（第一、第二形态）。
    ///
    /// <para><b>2026-11-24 用户定</b>：实验体只有第三形态才吃击杀 / 终结特效。前两形态不是真死——
    /// 它走「同一只 Creature 原地复活」，共用同一个 <c>NCreature.Visuals</c> 节点。终结特效会先把本体
    /// 涂黑 + 隐藏（快照见 <see cref="LiveVisuals"/>），而复活是异步的：收尾时若还停在
    /// <c>CurrentHp == 0</c>，<see cref="LiveVisuals.RestoreIfAlive"/> 就不会还原 →
    /// 后面的形态继承「被打没」的状态，模型直接消失。</para>
    /// </summary>
    public static bool IsRevivingTestSubject(Creature? creature)
    {
        try
        {
            return creature?.Monster is TestSubject subject
                   && subject.Respawns < TestSubjectFinalRespawns;
        }
        catch
        {
            return false;
        }
    }

    public static bool Contains(Creature? creature)
    {
        try
        {
            var monster = creature?.Monster;
            if (monster == null)
                return false;

            // 实验体第一、第二形态：一律按普通击杀处理（不播终结 / 击杀特效）
            if (IsRevivingTestSubject(creature))
                return true;

            var type = monster.GetType();
            return Names.Contains(type.Name)
                   || (type.FullName != null && Names.Contains(type.FullName));
        }
        catch
        {
            return false;
        }
    }
}
