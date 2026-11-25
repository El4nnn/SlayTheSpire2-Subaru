using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 把血条伪装成「已经归零」，用来盖住死归的锁血。
///
/// 逻辑上人还是 1 血（引擎不能真的判死，否则整场战斗会被结束掉），
/// 但视觉上要像死了：血条前景条隐藏、血量数字显示 0，倒放结束后再恢复。
///
/// 归宿：
///   - 前景条的显示/隐藏由 NHealthBar.RefreshForeground 按 CurrentHp 决定，
///     CurrentHp &lt;= 0 时它会把 _hpForeground 整个 Visible=false —— 这里直接复刻那个状态。
///   - 恢复时调 RefreshValues()，让它按真实血量重算一次，不手工还原数字。
/// </summary>
public static class RewindHealthMask
{
    /// <summary>
    /// 遮罩是否正在接管血条。RewindVisuals.TintHealthBar 靠它避让：
    /// 紫闪和死归演出几乎同一帧发生，两边都改 Modulate 会互相把对方当原色。
    /// </summary>
    public static bool IsActive { get; private set; }

    /// <summary>当前被遮罩的生物。RefreshBar 靠它判断「这次刷新会不会把 0 又写回 1」。</summary>
    private static Creature _masked;

    /// <summary>把血条伪装成 0 血。返回恢复动作（可能为 null，表示没找到血条）。</summary>
    public static Action Apply(Creature creature)
    {
        try
        {
            if (creature == null)
                return null;

            var node = creature.GetCreatureNode();
            if (node == null)
                return null;

            var bar = FindHealthBar(node) as NHealthBar;
            if (bar == null)
            {
                GD.Print("[Subaru] health mask skipped: NHealthBar not found");
                return null;
            }

            var fg = Traverse.Create(bar).Field("_hpForeground").GetValue<Control>();
            var label = FindHpLabel(bar);
            var oldText = label?.Text;
            var oldModulate = bar.Modulate;

            // 命中的那一刻 RewindVisuals 正在把血条染紫（持续 0.6 s），
            // 那个紫不是原色，绝不能记下来当「还原目标」，否则演出结束后血条会永久变紫。
            var normalized = oldModulate.IsEqualApprox(RewindVisuals.BarTint);
            if (normalized)
                oldModulate = Colors.White;

            // 整条血条（含数字）染成紫色，和屏幕闪光同一个色系
            bar.Modulate = RewindVisuals.BarTint;

            if (fg != null && GodotObject.IsInstanceValid(fg))
                fg.Visible = false;

            if (label != null && GodotObject.IsInstanceValid(label))
                label.Text = "0";

            IsActive = true;
            _masked = creature;

            GD.Print($"[Subaru] health masked to 0 (visual only, restore to {oldModulate}"
                     + (normalized ? ", tint colour ignored)" : ")"));

            return () =>
            {
                try
                {
                    if (GodotObject.IsInstanceValid(bar))
                    {
                        // 先让引擎按真实血量重算，再压颜色，
                        // 免得 RefreshValues 又把 Modulate 覆盖回去
                        bar.RefreshValues();
                        bar.Modulate = oldModulate;
                    }

                    // RefreshValues 万一没重写文本，就手动还原
                    if (label != null && GodotObject.IsInstanceValid(label) && !string.IsNullOrEmpty(oldText))
                        label.Text = oldText;

                    if (fg != null && GodotObject.IsInstanceValid(fg))
                        fg.Visible = true;

                    GD.Print("[Subaru] health mask restored");
                }
                catch (Exception e)
                {
                    GD.PushWarning("[Subaru] health mask restore failed: " + e.Message);
                }
                finally
                {
                    IsActive = false;
                    _masked = null;
                }
            };
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] health mask apply failed: " + e.Message);
            IsActive = false;
            return null;
        }
    }

    /// <summary>
    /// 让血条按真实血量重算一次。
    /// 瀑布巨兽蓄力时血条会显示成 ∞（不可击杀的表现），死归把血量和能力都写回去之后
    /// 这条 UI 不会自己重算，就会一直停在 ∞——模型其实是好的，只是显示没刷新。
    /// </summary>
    public static void RefreshBar(Creature creature)
    {
        try
        {
            // 遮罩期间绝不能刷新：RefreshValues 会按真实血量（1）把数字和血条写回去，
            // 观感就变成「被击杀那一刻明明该是 0，却显示 1 血」。
            if (IsActive && ReferenceEquals(_masked, creature))
            {
                GD.Print("[Subaru] health bar refresh suppressed (mask active)");
                return;
            }

            var node = creature?.GetCreatureNode();
            if (node == null)
                return;

            if (FindHealthBar(node) is NHealthBar bar && GodotObject.IsInstanceValid(bar))
            {
                bar.RefreshValues();
                GD.Print("[Subaru] health bar refreshed: " + creature.Name);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] refresh health bar failed: " + e.Message);
        }
    }

    private static Control FindHealthBar(Node root)
    {
        if (root == null)
            return null;

        if (root is Control control && root.GetType().Name.Contains("HealthBar"))
            return control;

        foreach (var child in root.GetChildren())
        {
            var found = FindHealthBar(child);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>血条上的血量数字（第一个 Label）。</summary>
    private static Label FindHpLabel(Node root)
    {
        if (root == null)
            return null;

        if (root is Label label)
            return label;

        foreach (var child in root.GetChildren())
        {
            var found = FindHpLabel(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
