using System;
using System.Threading.Tasks;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 演出用的**按真实毫秒推进**的帧循环 + 缓动。
///
/// <para>2026-11-22 为剑圣 T2 改造加的：以前都用 <c>Tween</c>，一旦要「第 70ms 这一帧交回伤害结算」、
/// 「第 16/33ms 各拍一张残影」，Tween 不好插桩。这里直接 while + <c>ProcessFrame</c>，
/// <paramref name="step"/> 拿到的是**从开始到现在的毫秒数**（不是 0~1），时间轴上的每一拍都能写成
/// <c>if (el &gt;= 某毫秒)</c>，和策划表一一对应。</para>
///
/// <para>注意：必须跑在主线程（所有调用点都是 Godot 信号 awaiter），别在别的线程里 await。</para>
/// </summary>
internal static class Anim
{
    internal static double NowMs() => Time.GetTicksMsec();

    /// <summary>跑 <paramref name="durationMs"/> 毫秒，每帧调一次 <paramref name="step"/>（参数 = 已过毫秒）。</summary>
    internal static async Task Ms(SceneTree tree, float durationMs, Action<float> step)
    {
        var t0 = NowMs();
        var guard = 0;

        while (true)
        {
            var el = (float)(NowMs() - t0);
            step(el);

            if (el >= durationMs || ++guard > 4096)
                break;

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>缓出（三次）：起手快、收尾慢 —— 刀光扫过用这个。</summary>
    internal static float OutCubic(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    /// <summary>缓出（五次）：比 OutCubic 更「啪」的一下 —— 抽刀那 70ms 用这个。</summary>
    internal static float OutQuint(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return 1f - Mathf.Pow(1f - t, 5f);
    }
}
