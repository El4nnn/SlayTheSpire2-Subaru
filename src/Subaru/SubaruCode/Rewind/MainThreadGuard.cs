using System;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 主线程判定：给「模拟出来的战斗」和「真战斗」划一条界线。
///
/// 为什么需要：
///   第三方求解器（CombatSolver 一类）会在线程池线程里跑整场战斗的模拟，
///   模拟走的是 Mirror 的 hook 镜像 —— 也就是说**我们的 Hook 补丁会被它调用**。
///   一旦我们在那条路径上改真状态（死归记账、遗香、欠诅咒）或碰 Godot 节点，
///   就会得到两种后果，2026-11-17 玩家日志里两种都出现了：
///     · ERROR: Adding children to a node inside the SceneTree is only allowed from the main thread
///     · 求解器每试一个分支，就把「模拟里的死亡」当成真死亡记一次
///
///   真战斗的伤害结算全在主线程，所以「非主线程 = 模拟」是最省事也最稳的判据，
///   不用去认具体的第三方 mod（认了也不可靠，装的人不一样）。
///
/// 用法：模块初始化时 <see cref="Capture"/> 一次，之后用 <see cref="IsMainThread"/> 守卫。
/// 若从未 Capture（极端情况），一律当作主线程放行，宁可少拦也不要让死归本身失效。
/// </summary>
public static class MainThreadGuard
{
    private static int _mainThreadId;

    /// <summary>记下当前线程为主线程（在 <c>MainFile.Initialize</c> 里调，只生效一次）。</summary>
    public static void Capture()
    {
        if (_mainThreadId == 0)
            _mainThreadId = Environment.CurrentManagedThreadId;
    }

    public static bool IsMainThread =>
        _mainThreadId == 0 || Environment.CurrentManagedThreadId == _mainThreadId;
}
