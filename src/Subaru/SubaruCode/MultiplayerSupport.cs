using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace Subaru.SubaruCode;

/// <summary>
/// 多人模式的统一判据（2026-11-19 用户定的方案：多人里禁用死归、涅奥三个契约选项固定存在）。
///
/// <para><b>为什么死归在多人里必须关</b>：死归不是一张牌的效果，而是把「整场战斗的玩家状态」回滚到几回合前
/// —— 血量、能量、格挡、能力层数、牌堆顺序、敌方动作状态全要还原。单人这些只在本机内存里，
/// 但多人里它们是**两端必须一致**的同步状态：任何一端单独回滚都会分叉（轻则血量/路线对不上，
/// 重则校验和不匹配断线）。更直接的是致命伤害截断（<c>CapLethalDamage</c>）本身就在改
/// 「这次掉多少血」，等于单方面篡改伤害结算。所以多人里：不截断、不拍快照、不回档、不显示「立即死归」按钮，
/// 昴就是一个「没有死归的普通角色」。</para>
///
/// <para><b>判据</b>：游戏自带 <see cref="RunManager.IsSingleplayerOrFakeMultiplayer"/>。
/// 名字里的 fake multiplayer 指单人局内部也走一遍多人会话框架；那种情况下仍然只有本机一个玩家，
/// 死归完全安全 —— 所以这个属性为 true 就等于「可以死归」。
/// 取不到 <see cref="RunManager.Instance"/>（主菜单等）时按单人处理：宁可多给一次机会，
/// 也不要在单人局里把死归误关。</para>
///
/// <para><b>注意时序</b>（2026-11-20）：这个属性是 <c>IsInProgress &amp;&amp; 会话类型 == Singleplayer</c>，
/// 所以**开局创建期间**（<c>IsInProgress</c> 还没置位）它一律返回 false —— 谁在那一刻用它判断
/// 「是不是多人」，都会得到「是」。血量那条已经因此踩过坑（单人开局拿到多人 +10 → 60 血），
/// 现在血量统一 50，不再依赖这个属性；**其它地方若要在这段时间判断多寡，别用这个属性**。</para>
/// </summary>
public static class MultiplayerSupport
{
    /// <summary>本局是不是真正的多人会话（≥2 名玩家）。</summary>
    public static bool IsMultiplayer
    {
        get
        {
            try
            {
                var manager = RunManager.Instance;
                return manager != null && !manager.IsSingleplayerOrFakeMultiplayer;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>死归能不能用（多人里一律 false）。</summary>
    public static bool RewindEnabled => !IsMultiplayer;

    private static bool _loggedRewindBlocked;

    /// <summary>
    /// 死归相关入口的统一守卫：多人返回 true（调用方直接 return），单人返回 false。
    /// 第一次拦住时打一行日志 —— 既不刷屏，排查时又必须能看见它生效了。
    /// </summary>
    public static bool BlockRewind()
    {
        if (!IsMultiplayer)
            return false;

        if (!_loggedRewindBlocked)
        {
            _loggedRewindBlocked = true;
            GD.Print("[Subaru] rewind disabled: multiplayer run (death return is single-player only)");
        }

        return true;
    }
}
