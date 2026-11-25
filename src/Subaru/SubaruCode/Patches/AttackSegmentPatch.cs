using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 攻击命令的「每段」钩子：只做打击感（<see cref="Vfx.HitFeel"/>），不插任何延时。
///
/// IL 实证：<c>AttackCommand.Execute</c> 每次「打一段」都会先 await 字段
/// <c>_beforeDamage</c>（<c>.BeforeDamage(Func&lt;Task&gt;)</c> 存进去的那个），再算伤害 ——
/// 也就是说它**每段都会被调用一次**，正好用来按「每段伤害 / 目标最大生命」分档顿帧。
///
/// 2026-11-20 用户定：原来那个「每 2 段停 0.3s」的节奏**已删除**，段与段之间
/// 不再有任何人为延时（伤害结算速度恢复原状）。
///
/// 只对本 mod 的牌生效（<c>ModelSource is SubaruCard</c>）；
/// 自己接管弹幕的牌（<see cref="Cards.SubaruCard.HandlesOwnVolley"/>，如 Celestia）
/// 跳过 —— 它的打击感在弹幕每一发的命中点处理，否则会双份。
/// </summary>
[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
public static class AttackSegmentPatch
{
    private static readonly AccessTools.FieldRef<AttackCommand, int> HitCountRef =
        AccessTools.FieldRefAccess<AttackCommand, int>("_hitCount");

    private static readonly AccessTools.FieldRef<AttackCommand, Func<Task>?> BeforeDamageRef =
        AccessTools.FieldRefAccess<AttackCommand, Func<Task>?>("_beforeDamage");

    private static readonly AccessTools.FieldRef<AttackCommand, decimal> DamagePerHitRef =
        AccessTools.FieldRefAccess<AttackCommand, decimal>("_damagePerHit");

    private static readonly AccessTools.FieldRef<AttackCommand, Creature?> SingleTargetRef =
        AccessTools.FieldRefAccess<AttackCommand, Creature?>("_singleTarget");

    /// <summary>补丁挂载时由 Harmony 调用一次 —— 这行日志是「补丁到底有没有挂上」的唯一凭证。</summary>
    static bool Prepare()
    {
        GD.Print("[Subaru] attack segment patch: Prepare() reached");
        return true;
    }

    [HarmonyPrefix]
    public static void Prefix(AttackCommand __instance)
    {
        try
        {
            if (__instance == null)
                return;

            var modelName = __instance.ModelSource?.GetType().Name ?? "null";
            var hits = HitCountRef(__instance);

            if (__instance.ModelSource is not Cards.SubaruCard card)
            {
                LogOnce($"[Subaru] attack (not ours, untouched): model={modelName} hits={hits}");
                return;
            }

            if (card.HandlesOwnVolley)
            {
                LogOnce($"[Subaru] attack (own volley, feel on impact): model={modelName} hits={hits}");
                return;
            }

            // 已经有别人挂了 beforeDamage 就别抢（本 mod 目前没人用）
            if (BeforeDamageRef(__instance) != null)
                return;

            var perHit = DamagePerHitRef(__instance);
            var target = SingleTargetRef(__instance);

            // 有专属命中音的牌（剑圣，2026-11-22 用户定）：只关通用命中音，
            // **顿帧 / 抖动照旧** —— 所以这里不能改用 HandlesOwnVolley（那个会整张牌跳过）。
            var muteSfx = card.HasCustomHitSfx;

            // 「单次群攻」= 打全体 + 只结算 1 段：走专门的音效 + 向下顿挫
            var aoeSingleHit = __instance.IsMultiTargeted && hits == 1;

            // 「对单多段」= 单体 + 多段：每段取一段打击音效，从第 1 段开始循环
            var multiHit = !__instance.IsMultiTargeted && hits > 1;
            if (multiHit)
                Audio.MultiHitSfx.Restart();

            // 「单段单体」= 单体 + 只结算 1 段：用 single_hit 那条素材
            var singleHit = !__instance.IsMultiTargeted && hits == 1;

            // 「对群多段」= 全体 + 多段：每段一段音效，不循环，用完就静音
            var aoeMultiHit = __instance.IsMultiTargeted && hits > 1;
            if (aoeMultiHit)
                Audio.MultiAoeHitSfx.Restart();

            BeforeDamageRef(__instance) = BuildHook(perHit, target, aoeSingleHit, multiHit, singleHit, aoeMultiHit, muteSfx);

            LogOnce($"[Subaru] attack segment feel installed: model={modelName} hits={hits} per={perHit} aoe={aoeSingleHit} muteSfx={muteSfx}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] attack segment patch failed: " + e.Message);
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> Logged = [];

    private static void LogOnce(string msg)
    {
        if (Logged.Add(msg))
            GD.Print(msg);
    }

    private static Func<Task> BuildHook(decimal perHit, Creature? target, bool aoeSingleHit, bool multiHit,
                                        bool singleHit, bool aoeMultiHit, bool muteSfx)
    {
        var maxHp = target?.MaxHp ?? 0;
        var fired = false;
        var segment = 0;   // 本串的第几段（0 = 第 1 段）

        return () =>
        {
            var isFirstSegment = segment++ == 0;

            if (aoeSingleHit && !fired)
            {
                fired = true;
                // 单次群攻：对群音效 + 向下踩一脚（同时打中多个目标也只有这一下）
                if (!muteSfx)
                    Audio.AoeHitSfx.Play();

                Vfx.HitFeel.AoeImpact();
            }
            else if (aoeMultiHit)
            {
                // 对群多段：每段一段音效，音效用完就不再响
                if (!muteSfx)
                    Audio.MultiAoeHitSfx.PlayNext();
            }
            else if (!aoeSingleHit)
            {
                if (multiHit)
                {
                    // 单体多段：每段依次播一段打击音效（比段数多时从头循环）
                    if (!muteSfx)
                        Audio.MultiHitSfx.PlayNext();
                }
                else if (singleHit)
                {
                    // 单段单体：一次打击音效
                    if (!muteSfx)
                        Audio.SingleHitSfx.Play();
                }

                // 顿帧 / 抖动**不受 muteSfx 影响**（用户只要求关音效）。
                // 打击感仍然只给第 1 段（2026-11-21 用户定）：12 血的小怪吃 3 段会连停 0.9 秒。
                Vfx.HitFeel.OnHit(perHit, maxHp, false, target, isFirstSegment);
            }

            return Task.CompletedTask;
        };
    }
}
