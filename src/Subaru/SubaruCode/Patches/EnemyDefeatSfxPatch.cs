using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 敌人被击败时：播「碎裂」音效 + 击杀特效，并让死亡流程等特效播完。
///
/// <para>为什么挂 <c>Hook.AfterDeath</c>：它是所有死亡钩子的汇总入口，每次死亡只走一遍，
/// 而且拿得到死掉的那只 <c>Creature</c>。用 <c>object[] __args</c> 而不是具名参数 ——
/// 这个钩子在不同版本里形参名/个数改过（0.107 vs 0.113），按名字绑定会静默失效。</para>
///
/// <para><b>只给「有死亡动画」的怪</b>（用户要求）：钩子最后那个 <c>float deathAnimLength</c>
/// 就是死亡动画时长（来自 <c>NCreature.StartDeathAnim</c> 的返回值）。
/// 秒消失/直接移除的怪拿到 0，这类不播。</para>
///
/// <para><b>让奖励界面等特效</b>：这是 0.113 里唯一能「按住战斗结束」的非侵入点 ——
/// 引擎会 await 这个钩子的 <c>Task</c>。所以把 <c>__result</c> 换成
/// 「原 Task + 特效 Task」的组合：死亡处理完、特效也播完，战斗才判结束。
/// 多人 / 设置关闭 / 特效已在播 时 <c>KillEffect.NotifyKill</c> 返回 null，完全不阻塞。</para>
///
/// <para>过滤：① 死的不是玩家；② 死的是敌人；③ 队伍里有昴；④ 死亡动画 &gt; 0.1s。
/// 同帧多个敌人一起死时，音效只用一个播放器（后一次 Play 是重整，不叠音）。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterDeath")]
public static class EnemyDefeatSfxPatch
{
    /// <summary>死亡动画短于这个秒数就当「没有死亡动画」，不播。</summary>
    public const float MinDeathAnimSeconds = 0.10f;

    static bool Prepare()
    {
        GD.Print("[Subaru] enemy defeat sfx patch: Prepare() reached");
        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(object[] __args, ref Task __result)
    {
        try
        {
            var creature = __args.OfType<Creature>().FirstOrDefault();
            if (creature == null || creature.IsPlayer || !creature.IsEnemy)
                return;

            // 实验体第一、第二形态是「原地复活」的转阶段，不是真死：
            // **音效照播**（死一下该有声），但终结 / 击杀特效 / 专属演出全部跳过
            // （2026-11-24 用户定：只让第三形态吃特效）
            var reviving = Vfx.FinisherExempt.IsRevivingTestSubject(creature);
            if (reviving)
                LogOnce("[Subaru] enemy defeat: test subject reviving -> sfx only (no effects)");

            var state = creature.CombatState;
            var players = state?.Players;
            if (players == null)
                return;

            if (!players.Any(p => p?.Character is Character.Subaru))
                return;

            // 这个钩子只有一个 float 形参 = 死亡动画时长
            var deathAnim = __args.OfType<float>().FirstOrDefault();
            if (deathAnim < MinDeathAnimSeconds)
            {
                LogOnce($"[Subaru] enemy defeat sfx: skip (no death anim, len={deathAnim:F2}s)");
                return;
            }

            LogOnce($"[Subaru] enemy defeat sfx: play (death anim={deathAnim:F2}s)");

            // 贤者·夏乌拉：**不播音效**，击杀特效照播（配色由 <see cref="Vfx.KillEffect.NextStyle"/> 决定：
            // 终结 = 黑剪影，非终结击杀 = 原来的紫白）。2026-11-21 用户定。
            var source = __args.OfType<MegaCrit.Sts2.Core.Models.CardModel>().FirstOrDefault();
            var subaru = players.FirstOrDefault(p => p?.Character is Character.Subaru);

            if (!reviving && source is Cards.Shaula)
            {
                // 终结 = 这一只死后场上不再有活着的非爪牙（和通用击杀特效的 full tier 同一判据）
                var finisher = Vfx.ShaulaStrike.IsFinisherNow(subaru, creature);

                LogOnce($"[Subaru] enemy defeat: shaula kill -> 专属特效（通用完全拦截, finisher={finisher}）");
                var shaulaTask = Vfx.ShaulaFinisher.PlayAsync(creature, finisher);
                if (shaulaTask != null && __result != null)
                    __result = ChainAsync(__result, shaulaTask);
                return;
            }

            // 八重·天膳：击杀演出「原子斩」，**全部拦截通用击杀特效**（2026-11-21 用户定）。
            // 终结（清场那一击）= 刷线 → 静止 200ms → 碎块散开；普通击杀 = 刷线 → 碎块直接散开。
            // 音效保留通用击杀音（这一支不再走下面的 EnemyDefeatSfx + KillEffect）。
            if (!reviving && source is Cards.PuppetStrings)
            {
                var finisher = Vfx.ShaulaStrike.IsFinisherNow(subaru, creature);

                LogOnce($"[Subaru] enemy defeat: yae kill -> 原子斩（通用完全拦截, finisher={finisher}）");

                // 音效在演出内部（切线合上那一刻）播专属 swish；演出没播时才退回通用击杀音
                var slashTask = Vfx.YaeAtomicSlash.PlayAsync(creature, finisher, deathAnim);

                if (slashTask == null)
                {
                    Audio.EnemyDefeatSfx.Play();
                }
                else if (__result != null)
                {
                    __result = ChainAsync(__result, slashTask);
                }

                return;
            }

            // 剑圣系（莱茵哈鲁特 / 初代剑圣·雷德 / 剑鬼）：击杀/终结 -> 整屏竖切。
            // **拦掉通用击杀音**：这一支有自己的专属击杀音（音效在演出内部播）。
            // 终结（清场）= 静一拍 → 慢扫 → 长静止 → 上飘/下落 → 冷白闪；普通击杀短一档。
            if (!reviving && source is Cards.SwordSaintFriend or Cards.Reid or Cards.SwordDemonFriend)
            {
                var finisherSaint = Vfx.ShaulaStrike.IsFinisherNow(subaru, creature);
                var splitTask = Vfx.ReinhardSlash.PlayFinisherAsync(creature, finisherSaint, deathAnim);

                LogOnce($"[Subaru] enemy defeat: sword slash kill ({source.GetType().Name}) -> slash finisher (finisher={finisherSaint})");

                if (splitTask == null)
                {
                    // 演出没播成（环境不具备 / 被关掉）：退回通用击杀音，别让这一刀没声音
                    Audio.EnemyDefeatSfx.Play();
                }
                else if (__result != null)
                {
                    __result = ChainAsync(__result, splitTask);
                }

                return;
            }

            // 魔弹射手·巴尔罗伊 / 尸兵·巴尔罗伊：**这里什么都不播**（2026-11-22 用户定）。
            // 终结动画已经改成「终结一击的攻击动画」——在卡牌 / 延迟段里、伤害结算之前就播完了，
            // 所以死亡阶段只需要（a）拦掉通用碎裂音与 KillEffect，（b）等白屏收尾播完再放奖励界面。
            // 延迟两段伤害没有 card 来源 → 靠「最近伤害」标记认出是巴尔罗伊这一击。
            if (!reviving && (source is Cards.Ballroy or Cards.CorpseBallroy || Vfx.BallroyBeam.IsRecentDamage()))
            {
                var tail = Vfx.BallroyBeam.PendingTail;

                LogOnce($"[Subaru] enemy defeat: ballroy kill -> silent (attack anim already played, "
                        + $"source={source?.GetType().Name ?? "echo"}, tail={(tail != null ? "wait" : "none")})");

                if (tail != null && __result != null)
                    __result = ChainAsync(__result, tail);

                return;
            }

            LogOnce($"[Subaru] enemy defeat: generic effect (source={source?.GetType().Name ?? "none"}, "
                     + $"ballroyRecent={Vfx.BallroyBeam.IsRecentDamage()})");

            Audio.EnemyDefeatSfx.Play();

            // 贝蒂在场时用[阴魔法]终结（2026-11-21 用户定）：贝蒂的立绘出场，
            // 定格到敌人死亡动画播完再退场。这一击的力是阴魔法的，所以
            // **通用击杀音效 + 击杀特效照播**，只额外加一层立绘（不像八重那样拦截）。
            // 出场策略走 CutIn.MayShow：同一角色每回合默认只出一次（设置页可改）。
            var yinCaster = players.FirstOrDefault(p =>
                p?.Creature != null && p.Creature.HasPower<Powers.BeatricePower>());

            if (!reviving && yinCaster != null
                && source is Cards.SubaruCard yinCard
                && yinCard.CanonicalKeywords.Contains(Cards.SubaruKeywords.YinMagic))
            {
                var bettyTask = Vfx.BeatriceCutIn.PlayFinisherAsync(yinCaster, deathAnim);
                if (bettyTask != null)
                {
                    LogOnce("[Subaru] enemy defeat: yin magic kill with betty -> 贝蒂 cut-in");
                    if (__result != null)
                        __result = ChainAsync(__result, bettyTask);
                }
            }

            // 击杀特效（白闪 + 紫色剪影定格 + 裂缝 + 碎片）。
            // 这一批的第一只返回非 null → 把死亡流程挂到综艺结束之后。
            var waitForEffect = Vfx.KillEffect.NotifyKill(creature);
            if (waitForEffect != null && __result != null)
                __result = ChainAsync(__result, waitForEffect);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] enemy defeat sfx patch failed: " + e.Message);
        }
    }

    /// <summary>先等原死亡流程，再等特效；任何一边失败都不影响另一边，也不让异常冒出去。</summary>
    private static async Task ChainAsync(Task original, Task effect)
    {
        try
        {
            await original;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] after-death original failed: " + e.Message);
        }

        try
        {
            await effect;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] after-death effect failed: " + e.Message);
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> Logged = [];

    private static void LogOnce(string msg)
    {
        if (Logged.Add(msg))
            GD.Print(msg);
    }
}
