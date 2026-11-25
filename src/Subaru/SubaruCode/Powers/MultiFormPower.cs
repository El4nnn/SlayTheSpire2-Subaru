using System;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 复数形态（状态）：下一张打出的[尸兵]多打 <c>Amount</c> 次（1 = 两次，2 = 三次）。
///
/// <para><b>为什么这里不覆写 <c>ModifyCardPlayCount</c></b>：对话镜那次实测过，
/// 这条 hook 的监听者列表里没有把模型算进去（覆写一次都没触发）。
/// 所以改用 Harmony 给 <c>Hook.ModifyCardPlayCount</c> 挂 postfix
/// （见 <c>MultiFormPlayCountPatch</c>），由这个能力对象自己记账。</para>
///
/// <para><b>Amount 就是「多打几次」</b>，所以卡面/图标上的数字 = 剩余次数，
/// 用完把自己移除。</para>
/// </summary>
public sealed class MultiFormPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    public override string CustomPackedIconPath => "multi_form_power.png".PowerImagePath();

    public override string CustomBigIconPath => "multi_form_power.png".BigPowerImagePath();

    private bool _consuming;

    /// <summary>额外打出的次数（1 = 打两次，2 = 打三次）。</summary>
    public int ExtraPlays => (int)Amount;

    /// <summary>取走一次加成并消耗这个状态。已经消耗过就返回 0。</summary>
    public int Consume()
    {
        if (_consuming || Amount <= 0)
            return 0;

        _consuming = true;
        var extra = ExtraPlays;

        // 这个状态下一次就没了：直接移除（不需要玩家做任何选择，异步收尾即可）
        _ = PowerCmd.Remove(this);

        GD.Print($"[Subaru] multi form consumed: +{extra} play(s)");
        return extra;
    }
}
