using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 建筑师的对话「取模循环」：让通关次数超过对话段数的老玩家从头再听一遍。
///
/// <para><b>为什么需要</b>：<c>TheArchitect.LoadDialogue</c> 用
/// 「以该角色通关的次数」当 visit 序号，<c>AncientDialogueSet.GetValidDialogues</c> 只取
/// <c>VisitIndex == charVisits</c> 的**精确相等**匹配；匹配不到再收 <c>IsRepeating</c> 的段。
/// 而 BaseLib 给模组角色生成的对话**从不设置 IsRepeating**（IL 里没有 set_IsRepeating），
/// 所以从第 N+1 次登顶起（我们只写了 0/1/2 三段，N=2）候选列表直接是空的。</para>
///
/// <para><b>空列表的后果不只是「没台词」</b>：<c>rng.NextItem(空)</c> 返回 null →
/// <c>Dialogue</c> 为 null → <c>TheArchitect.CreateOptionForCurrentLine</c> 第一句就是
/// <c>Dialogue.Lines[CurrentLineIndex]</c>，**没有判空** → 空引用，直接把登顶流程打断
/// （2026-11-18 的 [TODO] 事故就是这一类）。</para>
///
/// <para><b>做法</b>：只在该方法返回空的时候接手 —— 取该角色所有段里最大的 visit 序号，
/// 用 <c>charVisits % (maxVisit + 1)</c> 重新选段。于是第 1/2/3 次看 0/1/2 段，
/// 第 4 次回到 0 段，之后循环。原版角色的「重复对话」逻辑不会被碰到（它们返回非空）。</para>
/// </summary>
[HarmonyPatch(typeof(AncientDialogueSet), nameof(AncientDialogueSet.GetValidDialogues))]
public static class ArchitectDialogueLoopPatch
{
    /// <summary>
    /// 参数按位置取：__0 = characterId(ModelId)，__1 = charVisits(int)。
    /// 不按名字取，游戏小版本改形参名不会把补丁打穿。
    /// </summary>
    [HarmonyPostfix]
    public static void Postfix(AncientDialogueSet __instance, ModelId __0, int __1,
        ref IEnumerable<AncientDialogue> __result)
    {
        try
        {
            // 有结果就完全不插手（原版角色、首次见面、重复对话都走这条）
            if (__result != null && __result.Any())
                return;

            var dialogues = __instance?.CharacterDialogues;
            if (dialogues == null || !dialogues.TryGetValue(__0.Entry, out var list) || list == null || list.Count == 0)
                return;

            var withVisit = list.Where(d => d != null && d.VisitIndex.HasValue).ToList();
            if (withVisit.Count == 0)
                return;

            var maxVisit = withVisit.Max(d => d.VisitIndex!.Value);

            // 取模循环：charVisits 0,1,2,3,4,5 → 0,1,2,0,1,2
            var index = __1 % (maxVisit + 1);
            if (index < 0)
                index += maxVisit + 1;

            var picked = withVisit.Where(d => d.VisitIndex!.Value == index).ToList();

            // 该序号没有对应段（例如玩家自己删了中间某段）→ 退到最大那段
            if (picked.Count == 0)
                picked = withVisit.Where(d => d.VisitIndex!.Value == maxVisit).ToList();

            if (picked.Count == 0)
                return;

            __result = picked;

            GD.Print($"[Subaru] architect dialogue looped: charVisits={__1}, "
                     + $"maxVisit={maxVisit} -> visitIndex={index}");
        }
        catch (Exception e)
        {
            // 宁可什么都不做，也不能把登顶流程带崩
            GD.PushWarning("[Subaru] architect dialogue loop patch failed: " + e.Message);
        }
    }
}
