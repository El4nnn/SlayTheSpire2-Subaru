using System.Collections.Generic;
using MegaCrit.Sts2.Core.Timeline;

namespace Subaru.SubaruCode.Timeline;

/// <summary>
/// 节点展开（原版机制）：<c>EpochModel.GetTimelineExpansion()</c> 返回的节点，
/// 只有在本节点解锁之后才作为「槽位」进入时间线。
///
/// 原版就是这么做的（比如「先子星」解锁后才把药水 / 遗物 / 暗港的槽位加进来），
/// 所以菜月昴没解锁时，他的后续节点连灰色占位都不该出现。
/// RitsuLib 的 mod 槽位合并逻辑也会读这个方法判断「谁是根节点、谁挂在谁下面」。
/// </summary>
public static class SubaruEpochChain
{
    /// <summary>按 id 取节点模型；取不到（还没注册）就先跳过，不抛。</summary>
    public static EpochModel[] Expand(params string[] epochIds)
    {
        var list = new List<EpochModel>();

        if (epochIds == null)
            return list.ToArray();

        foreach (var id in epochIds)
        {
            if (string.IsNullOrEmpty(id))
                continue;

            try
            {
                var model = EpochModel.Get(id);
                if (model != null)
                    list.Add(model);
            }
            catch
            {
                // 注册顺序没到就先不算，下一次调用会补上
            }
        }

        return list.ToArray();
    }
}
