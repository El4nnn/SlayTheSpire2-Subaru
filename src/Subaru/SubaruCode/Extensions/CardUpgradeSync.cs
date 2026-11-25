using System;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Subaru.SubaruCode.Extensions;

/// <summary>
/// 按引擎自己的升降级流程把卡牌的强化等级写回去。
///
/// <para><b>为什么不能直接写 <c>CurrentUpgradeLevel</c>（2026-11-25 玩家反馈）</b>：
/// 那个 setter 只做 <c>_currentUpgradeLevel = value</c>（IL 实证：断言完 MaxUpgradeLevel 就 stfld），
/// 既不重算 <c>DynamicVars</c>、也不触发 <c>Upgraded</c> 事件。于是死归把「剃刀牙在本场临时强化」
/// 的牌从 1 级写回 0 级时，卡面变成未强化，数值却还停在强化后的 —— 面板与数值对不上。</para>
///
/// <para><b>为什么走 <see cref="CardCmd"/> 而不是 <c>UpgradeInternal</c> / <c>DowngradeInternal</c></b>：
/// 这两个内部方法在编译基线（`_tools\refs\sts2-beta\sts2.dll`，10,648,064 B）里是**无参**的
/// （`UpgradeInternal()` / `DowngradeInternal()`），而线上运行的 `sts2.dll`（10,700,288 B）已经变成
/// <c>UpgradeInternal(bool silent)</c> / <c>DowngradeInternal(int amount)</c>。直接调内部方法会
/// 编译不过或运行时 MissingMethod；<see cref="CardCmd.Upgrade(CardModel, CardPreviewStyle)"/> 与
/// <see cref="CardCmd.Downgrade(CardModel)"/> 在两边签名一致，是安全的那条路。</para>
///
/// <para><b>它们做的事</b>：降级 = <c>DowngradeInternal</c>（把 DynamicVars / 关键词 / 费用按 canonical
/// 重建，再逐级升回目标等级，最后 Finalize + 触发 <c>Upgraded</c>）；升级 = <c>UpgradeInternal</c>
/// + <c>FinalizeUpgradeInternal</c>。传 <see cref="CardPreviewStyle.None"/> 可以在战斗内跳过预览弹窗。</para>
///
/// <para>注意：降级会 <c>EnergyCost.ResetForDowngrade()</c>，所以调用方要把「本场费用覆盖」
/// （<see cref="Rewind.BattleSnapshot.ApplyCombatCostOverride"/>）放在这之后补回去。</para>
/// </summary>
internal static class CardUpgradeSync
{
    /// <summary>
    /// 把一张牌升 <paramref name="levels"/> 级。
    ///
    /// <para>用途上等价于旧的 <c>card.UpgradeInternal()</c>，但**不碰内部方法**：0.114.0 把它改成了
    /// <c>UpgradeInternal(bool silent)</c>，而 0.113.x 是无参的，直接调会有一边炸
    /// （MissingMethodException / 编译不过）。<see cref="CardCmd.Upgrade(CardModel, CardPreviewStyle)"/>
    /// 两个版本签名一致，并且自带 <c>FinalizeUpgradeInternal</c>（费用也跟着升）。</para>
    /// </summary>
    public static void UpgradeNow(CardModel? card, int levels = 1)
    {
        if (card == null || levels <= 0)
            return;

        try
        {
            for (var i = 0; i < levels; i++)
            {
                if (card.CurrentUpgradeLevel >= card.MaxUpgradeLevel)
                    break;

                CardCmd.Upgrade(card, CardPreviewStyle.None);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] card upgrade failed: {card.Id}: {e.Message}");
        }
    }

    /// <summary>
    /// 把 <paramref name="card"/> 的强化等级同步到 <paramref name="target"/>。
    /// 返回 true 表示已经同步（含本来就是目标等级），false 表示失败（只记日志，不抛）。
    /// </summary>
    public static bool SetLevel(CardModel? card, int target)
    {
        if (card == null)
            return false;

        try
        {
            if (target < 0)
                target = 0;

            var max = card.MaxUpgradeLevel;
            if (target > max)
                target = max;

            var current = card.CurrentUpgradeLevel;
            if (current == target)
                return true;

            if (current > target)
            {
                for (var i = 0; i < current - target; i++)
                    CardCmd.Downgrade(card);

                GD.Print($"[Subaru] card upgrade level restored (downgrade {current} -> {target}): {card.Id}");
                return true;
            }

            for (var i = 0; i < target - current; i++)
                CardCmd.Upgrade(card, CardPreviewStyle.None);

            GD.Print($"[Subaru] card upgrade level restored (upgrade {current} -> {target}): {card.Id}");
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] card upgrade level restore failed ({target}): {card.Id}: {e.Message}");
            return false;
        }
    }
}
