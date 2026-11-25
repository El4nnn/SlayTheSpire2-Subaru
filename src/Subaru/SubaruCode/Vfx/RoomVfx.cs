using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 房间里的「画布层」工具：给特效找一个**正确的层级**，而不是往屏幕根节点上挂 CanvasLayer。
///
/// <para>2026-11-21 用户要求：击杀 / 终结 / cut-in 这些演出都要**在房间 UI 之下**（手牌也算 UI），
/// 所以不能用 <c>CanvasLayer</c>（Layer &gt; 0 会盖住整个房间 UI，包括手牌）。
/// 房间自己提供了三个容器，顺序就是画序：
/// <c>BgContainer</c> → <c>BackCombatVfxContainer</c> → 角色 → <c>CombatVfxContainer</c> → <c>Ui</c>。</para>
///
/// <para>挂进去的根节点会被 <see cref="AlignToScreen"/> 摆正，所以子节点坐标**就是屏幕坐标**，
/// 和引擎自己那套 <c>GetGlobalTransformWithCanvas</c> 得到的坐标一致。</para>
/// </summary>
public static class RoomVfx
{
    /// <summary>角色之上、UI 之下（射线、剪影、碎裂块用这层）。</summary>
    public static Node2D? Front(string name)
    {
        var room = NCombatRoom.Instance;
        var container = room?.CombatVfxContainer;
        return Attach(container, name);
    }

    /// <summary>背景之上、角色之下（橙色/灰白底色用这层）。</summary>
    public static Node2D? Back(string name)
    {
        var room = NCombatRoom.Instance;
        var container = room?.BackCombatVfxContainer;
        return Attach(container, name);
    }

    /// <summary>
    /// 把 <paramref name="root"/> 摆成「子树坐标 == 屏幕坐标」：**位移和缩放都抵消**。
    ///
    /// <para>2026-11-21 用户发现「同一套底色，上一只怪盖满屏、这一只盖不住」——
    /// 根因就在这：原来只减了容器的 <c>Origin</c>，没管 <c>Scale</c>。
    /// 房间容器的画布变换一旦带缩放（不同房间/不同画布各不一样），
    /// 按「一屏大小」画的矩形就不再等于一屏，边缘就露出来了。</para>
    /// </summary>
    public static void AlignToScreen(Node2D root, Control container)
    {
        try
        {
            var xf = container.GetGlobalTransformWithCanvas();
            var sx = Mathf.Abs(xf.Scale.X) < 0.0001f ? 1f : xf.Scale.X;
            var sy = Mathf.Abs(xf.Scale.Y) < 0.0001f ? 1f : xf.Scale.Y;

            root.Scale = new Vector2(1f / sx, 1f / sy);
            root.Position = new Vector2(-xf.Origin.X / sx, -xf.Origin.Y / sy);
        }
        catch
        {
            // 取不到就按无位移/无缩放处理
        }
    }

    private static Node2D? Attach(Control? container, string name)
    {
        if (container == null || !GodotObject.IsInstanceValid(container))
            return null;

        var root = new Node2D { Name = name };
        container.AddChild(root);

        AlignToScreen(root, container);

        // 一次性诊断：容器带缩放 / 裁剪时，「一屏大小」的矩形就盖不满屏（2026-11-21 排查用）
        try
        {
            var xf = container.GetGlobalTransformWithCanvas();
            GD.Print($"[Subaru] roomvfx '{name}': container origin=({xf.Origin.X:F0},{xf.Origin.Y:F0}) " +
                     $"scale=({xf.Scale.X:F3},{xf.Scale.Y:F3}) " +
                     $"size={container.Size.X:F0}x{container.Size.Y:F0} clip={container.ClipContents}");
        }
        catch
        {
            // 日志失败无所谓
        }

        return root;
    }
}
