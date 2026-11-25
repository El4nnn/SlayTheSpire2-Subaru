using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using Subaru.SubaruCode.Relics;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「贝蒂的守护」默认解锁：图鉴列表和遗物查看页都不再显示「未解锁」。
///
/// <para><b>唯一权威来源</b>：两个界面的解锁判定都只认 <c>UnlockState.Relics</c>：</para>
/// <list type="bullet">
///   <item><c>NInspectRelicScreen.Open</c>：<c>_allUnlockedRelics.UnionWith(unlockState.Relics)</c>，
///   然后 <c>UpdateRelicDisplay</c> 里 <c>_allUnlockedRelics.Contains(relic.CanonicalInstance)</c>
///   → 不在集合里就整页显示「锁定 / 这个遗物需要在时间线中解锁…」。</item>
///   <item><c>NRelicCollection.LoadRelics</c> → <c>NRelicCollectionCategory.LoadRelicNodes</c>：
///   同样用它算 <c>unlockedRelics</c>，决定图鉴条目是不是灰的。</item>
/// </list>
///
/// <para><b>为什么它会被算成没解锁</b>：<c>UnlockState.Relics</c> 是
/// 「所有遗物池的 <c>GetUnlockedRelics</c> 并集」，而本 mod 的遗物被
/// <see cref="GrantedRelicExclusionPatch"/> 从池子里滤掉了（只允许事件发放），
/// 于是它天然不在这个集合里。</para>
///
/// <para><b>之前两版为什么没用</b>：改 <c>NRelicCollectionEntry.Create</c> 的入参 /
/// 后置改 <c>ModelVisibility</c>、以及往 <c>LoadRelicNodes</c> 的两个 HashSet 里塞 ——
/// 都只覆盖了图鉴那一条路径，查看页（InspectRelicScreen）根本不经过它们，
/// 所以还是显示「锁定」。这次直接补在源头，两个界面一起好。</para>
///
/// <para><b>边界</b>：<c>UnlockState.Relics</c> 只被 UI 读（图鉴、查看页），
/// 奖励/商店走的是 <c>RelicPoolModel.GetUnlockedRelics</c>，那条路上它依旧被滤掉。
/// </para>
/// </summary>
[HarmonyPatch(typeof(UnlockState), "get_Relics")]
public static class RelicAlwaysUnlockedPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref IEnumerable<RelicModel> __result)
    {
        try
        {
            var self = ModelDb.Relic<BeatriceGuard>();
            if (self == null)
                return;

            var list = __result?.ToList() ?? new List<RelicModel>();
            if (!list.Contains(self))
                list.Add(self);

            __result = list;
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning("[Subaru] relic unlock patch failed: " + e.Message);
        }
    }
}
