using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 贝蒂的出场演出（2026-11-21 用户定）：复用 <see cref="CutIn"/> 那套立绘切入，
/// 立绘 id = <c>beatrice</c>（<c>res://Subaru/images/cutin/beatrice.png</c>）。
///
/// <para>两个触发点，**都只在阴魔法路线**：</para>
/// <list type="number">
/// <item>打出<see cref="Cards.BeatriceContract"/>（契约·贝蒂）—— 一局只打一次，
///     走 <see cref="CutIn.MayShowSignature"/>：只受「角色 cut-in」总开关管，不占每回合角色名额；</item>
/// <item>贝蒂在场时用[阴魔法]牌打出**终结一击** —— 走 <see cref="CutIn.MayShow"/>：
///     同一角色每回合默认只出一次（设置页「同一角色可重复出场」可改），占普通名额。</item>
/// </list>
///
/// <para>终结那一击定格到敌人死亡动画播完才退场（和夏乌拉的「立绘定格 → 射线 → 退场」同一个节奏），
/// 所以死亡流程会等它 —— 别在这里做长于 9 秒的等待，<see cref="CutIn"/> 有安全上限。</para>
/// </summary>
public static class BeatriceCutIn
{
    /// <summary><c>data/cutin.json</c> 里这条立绘的 id。</summary>
    public const string Id = "beatrice";

    /// <summary>打出契约·贝蒂：招牌演出，返回到命中点（之后的收尾交给后台）。</summary>
    public static Task PlayOnContractAsync(Player? owner) => CutIn.PlaySignatureAsync(owner, Id);

    /// <summary>
    /// 贝蒂状态下用[阴魔法]终结：立绘定格到死亡动画结束再退场。
    /// 返回 null = 这次不出场（总开关关 / 本回合已经出过），调用方不需要等待。
    /// </summary>
    public static Task? PlayFinisherAsync(Player? owner, float deathAnimSeconds)
    {
        if (owner == null || !CutIn.MayShow(Id))
            return null;

        // 定格到死亡动画播完（起码留 0.6s，太短会看不见立绘）
        var holdMs = Math.Max(600.0, deathAnimSeconds * 1000.0);
        return CutIn.PlayAndHoldForAsync(owner, Id, holdMs);
    }
}
