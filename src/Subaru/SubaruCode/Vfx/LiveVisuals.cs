using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 活体视觉的「快照 / 还原」—— 所有**会改活着的怪**的演出都必须经过这里。
///
/// <para><b>铁律（2026-11-21 用户定，踩过两次）</b>：击杀 / 终结演出可以给敌人涂黑、隐藏本体，
/// 但必须先 <see cref="Snapshot"/>，演出收尾时用 <see cref="RestoreIfAlive"/> 还原。
/// 原因：**转阶段的怪是「同一只 Creature 原地复活」**，不是「死掉 + 生成新怪」——
/// 实验体（<c>TestSubject</c>）走 <c>AdaptablePower.AfterDeath → TriggerDeadState → Revive</c>，
/// 而这条链在 <c>Hook.AfterDeath</c> **内部**就执行完了，我们的演出 postfix 是钩子返回**之后**才跑。
/// 第二形态共用同一个 <c>NCreature</c> / `Visuals` 节点，于是「涂黑 + 隐藏」被它继承，
/// 表现就是「第一阶段的模型被打没了」。</para>
///
/// <para>要点：<c>Material</c> / <c>Modulate</c> / <c>Visible</c> 三样都要存；spine 节点还要存
/// <c>get_normal_material</c>（渲染走的是它，只还原 CanvasItem.Material 不够）。</para>
/// </summary>
internal static class LiveVisuals
{
    /// <summary>一个可视节点在演出前的样子。</summary>
    internal sealed class State
    {
        public Node Node = null!;
        public CanvasItem? Item;
        public Material? Material;
        public Color Modulate;
        public bool Visible;
        public Variant Normal;
        public bool HasNormal;
    }

    /// <summary>把 <paramref name="visuals"/> 整棵子树（含自己）记下来。</summary>
    public static List<State> Snapshot(Node visuals)
    {
        var list = new List<State>();
        Collect(visuals, list);
        return list;
    }

    private static void Collect(Node node, List<State> list)
    {
        var st = new State { Node = node };

        if (node is CanvasItem item)
        {
            st.Item = item;
            st.Material = item.Material;
            st.Modulate = item.Modulate;
            st.Visible = item.Visible;
        }

        // spine 节点走的是「normal material」，ApplyMaterial 会把它一起换掉
        if (node.HasMethod("get_normal_material"))
        {
            try
            {
                st.Normal = node.Call("get_normal_material");
                st.HasNormal = true;
            }
            catch
            {
                // 取不出来就只还原 CanvasItem 那层
            }
        }

        list.Add(st);

        foreach (var child in node.GetChildren())
            Collect(child, list);
    }

    public static void Restore(List<State> states)
    {
        foreach (var st in states)
        {
            if (!GodotObject.IsInstanceValid(st.Node))
                continue;

            try
            {
                if (st.Item != null && GodotObject.IsInstanceValid(st.Item))
                {
                    st.Item.Material = st.Material;
                    st.Item.Modulate = st.Modulate;
                    st.Item.Visible = st.Visible;
                }

                if (st.HasNormal)
                    st.Node.Call("set_normal_material", st.Normal);
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] restore live visuals failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 演出收尾：这只怪**还活着**（复活 / 转阶段）就还原，真死（<c>CurrentHp = 0</c>）就保持「被打没」的现状。
    /// 返回 true 表示真的还原了 —— 调用方用它决定要不要打一行日志。
    /// </summary>
    public static bool RestoreIfAlive(Creature? creature, List<State>? snapshot)
    {
        if (snapshot == null || creature == null)
            return false;

        if (creature.CurrentHp <= 0)
            return false;

        Restore(snapshot);
        return true;
    }
}
