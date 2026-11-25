using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 遗物栏图标刷新：给「在事件里直接发放、界面还没准备好」的遗物补一次 Reload。
///
/// <para><b>问题</b>：<c>NRelic.set_Model</c> 里是 <c>ModelChanged?.Invoke(); Reload();</c>，
/// 而 <c>Reload()</c> 第一行就是 <c>if (!IsNodeReady()) return;</c>。也就是说，
/// 如果遗物节点是「先设 Model、再挂进场景树」，那一次 Reload 会直接被跳过，
/// 而且 <c>NRelic._Ready</c> 不会再调一次 —— 结果是遗物栏里有一个**有框没有图**的空位
/// （2026-11-19 玩家报「贝蒂的守护在遗物栏是空白的」）。</para>
///
/// <para><b>做法</b>：发完遗物后等一小段时间（节点已经进树），
/// 再按 Model 找到对应的 NRelic 节点，用反射调一次 <c>Reload()</c>。
/// 和 <see cref="YangSword"/> 里"战斗结束把图标恢复原色"用的是同一套走树 + 反射。</para>
/// </summary>
public static class RelicIconRefresh
{
    /// <summary>延迟刷新（不阻塞调用方）。节点还没进树时先返回，由定时器兜底。</summary>
    public static void Schedule(RelicModel model)
    {
        try
        {
            if (model == null)
                return;

            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            _ = RefreshLater(tree, model);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] relic icon refresh schedule failed: " + e.Message);
        }
    }

    private static async Task RefreshLater(SceneTree tree, RelicModel model)
    {
        try
        {
            await tree.ToSignal(tree.CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);

            var nodes = Reload(tree.Root, model);
            GD.Print($"[Subaru] relic icon refresh: {model.Id} nodes={nodes}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] relic icon refresh failed: " + e.Message);
        }
    }

    /// <summary>立刻按 Model 刷新（调用方自己保证节点已经进树）。</summary>
    public static int Reload(Node root, RelicModel model)
    {
        var count = 0;
        Walk(root);
        return count;

        void Walk(Node node)
        {
            if (node == null)
                return;

            if (node.GetType().Name == "NRelic")
            {
                var type = node.GetType();
                var current = type
                    .GetProperty("Model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    ?.GetValue(node);

                if (ReferenceEquals(current, model))
                {
                    type.GetMethod("Reload",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic)
                        ?.Invoke(node, null);
                    count++;
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }
    }
}
