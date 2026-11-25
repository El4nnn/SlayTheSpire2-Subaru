using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 给事件界面右侧的文字区垫一整块半透明底板（标题 + 正文 + 选项都盖住）。
///
/// <para><b>只给本 mod 的事件加</b>：判定写在 <see cref="EventTextPanelSetEventPatch"/> ——
/// <c>SetEvent</c> 收到的 <see cref="EventModel"/> 只要不是本程序集里的类型（原版事件、
/// 其他 mod 的事件、先古事件如涅奥/建筑师），就撤掉面板。它们各自有底图或别的 mod 自己在管。</para>
///
/// <para><b>插入位置</b>：面板要插在**立绘之上、文字之下**。直接加到根节点最底层会被满屏立绘盖住，
/// 所以插到「文字列在根节点下的直接子节点」之前（标题若更靠前，就插到标题之前）。</para>
///
/// <para><b>创建时机</b>：<c>_Ready</c> 里不建（那时还不知道是哪个事件），
/// 由 <c>SetEvent</c> / <c>AddOptions</c> 触发 Ensure —— 缺了就补建，有就只重新贴合；
/// 找不到原文节点时退化成「屏幕右半屏」固定底，保证一定有底衬。</para>
///
/// <para>面板 <c>MouseFilter = Ignore</c>，不挡按钮点击；名字固定，重复进事件不会叠第二块。</para>
/// </summary>
[HarmonyPatch(typeof(NEventLayout), "_Ready")]
public static class EventTextPanelPatch
{
    internal const string PanelName = "SubaruEventTextBox";
    private const float Pad = 24f;

    /// <summary>本 mod 的程序集：只有这里的 EventModel 才配底板。</summary>
    internal static readonly Assembly ModAssembly = typeof(EventTextPanelPatch).Assembly;

    private static readonly ConditionalWeakTable<NEventLayout, object> EnabledLayouts = new();
    private static bool _dumpedChildren;

    [HarmonyPostfix]
    public static void Postfix(NEventLayout __instance)
    {
        // 这里不建面板：此时还没 SetEvent，判断不了是不是本 mod 的事件。
        // 真正的工作在 SetEvent / AddOptions 的后置里。
    }

    internal static void SetEnabled(NEventLayout root, bool enabled)
    {
        EnabledLayouts.Remove(root);
        if (enabled)
            EnabledLayouts.Add(root, bool.TrueString);
    }

    internal static bool IsEnabled(NEventLayout root) => EnabledLayouts.TryGetValue(root, out _);

    /// <summary>这个事件是不是本 mod 的（按类型所在程序集判断）。</summary>
    internal static bool IsOurEvent(EventModel? model)
    {
        try
        {
            if (model == null)
                return false;

            // 先古事件（涅奥 / 建筑师）是原版类型，天然不在本程序集里；这里再显式挡一次
            if (model is AncientEventModel)
                return false;

            return model.GetType().Assembly == ModAssembly;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>有就贴合，没有就建（仅限本 mod 的事件）。</summary>
    internal static void Ensure(NEventLayout root)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(root))
                return;

            if (!IsEnabled(root))
                return;

            if (!_dumpedChildren)
            {
                _dumpedChildren = true;
                var children = string.Join(", ",
                    root.GetChildren().Select(c => $"{c.Name}({c.GetType().Name})"));
                GD.Print($"[Subaru] event text panel: root children = {children}");
            }

            var panel = root.GetNodeOrNull<Panel>(PanelName);
            if (panel == null)
            {
                var desc = root.GetNodeOrNull<Control>("%EventDescription");
                if (desc == null)
                {
                    GD.PushWarning("[Subaru] event text panel: %EventDescription missing, using right-side fallback");
                    panel = CreateFallback(root);
                }
                else
                {
                    panel = CreateBeforeText(root, desc);
                }
            }

            if (panel != null)
                Refine(root);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] event text panel failed: " + e);
        }
    }

    /// <summary>先古事件用：把已经建好的面板撤掉。</summary>
    internal static void Remove(NEventLayout root)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(root))
                return;

            var panel = root.GetNodeOrNull<Panel>(PanelName);
            if (panel == null)
                return;

            panel.QueueFree();
            GD.Print("[Subaru] event text panel: removed (not a Subaru event)");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] event text panel remove failed: " + e.Message);
        }
    }

    private static Panel CreateBeforeText(NEventLayout root, Control desc)
    {
        var column = AncestorUnderRoot(root, desc);
        if (column == null)
        {
            GD.PushWarning("[Subaru] event text panel: no column under root, using fallback");
            return CreateFallback(root);
        }

        var panel = NewPanel();
        root.AddChild(panel);

        // 插到文字列之前；标题若在更前面，插到标题之前（否则半透明底板会盖在标题上）
        var index = column.GetIndex();
        var title = root.GetNodeOrNull<Control>("%Title");
        var titleChild = title != null ? AncestorUnderRoot(root, title) : null;
        if (titleChild != null && titleChild.GetIndex() < index)
            index = titleChild.GetIndex();

        root.MoveChild(panel, index);

        GD.Print($"[Subaru] event text panel: created before {column.Name}({column.GetType().Name}), index={index}");
        return panel;
    }

    /// <summary>找不到文本列时的兜底：右半屏固定底。</summary>
    private static Panel CreateFallback(NEventLayout root)
    {
        var panel = NewPanel();
        root.AddChild(panel);

        panel.AnchorLeft = 0.45f;
        panel.AnchorTop = 0.05f;
        panel.AnchorRight = 0.995f;
        panel.AnchorBottom = 0.95f;
        panel.OffsetLeft = 0f;
        panel.OffsetTop = 0f;
        panel.OffsetRight = 0f;
        panel.OffsetBottom = 0f;

        GD.Print("[Subaru] event text panel: created right-side fallback");
        return panel;
    }

    private static Panel NewPanel()
    {
        var panel = new Panel
        {
            Name = PanelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", EventTextPanelStyle.Build());
        return panel;
    }

    /// <summary>布局排好之后，用「标题 ∪ 正文 ∪ 选项」的实际矩形贴合一次。</summary>
    internal static void Refine(NEventLayout root)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(root) || !IsEnabled(root))
                return;

            var panel = root.GetNodeOrNull<Panel>(PanelName);
            if (panel == null)
                return;

            var desc = root.GetNodeOrNull<Control>("%EventDescription");
            var options = root.GetNodeOrNull<Control>("%OptionsContainer");
            var title = root.GetNodeOrNull<Control>("%Title");

            if (desc == null || options == null)
                return;

            var rect = desc.GetGlobalRect().Merge(options.GetGlobalRect());
            if (title != null)
                rect = rect.Merge(title.GetGlobalRect());

            if (rect.Size.X < 2f || rect.Size.Y < 2f)
                return;

            var inv = root.GetGlobalTransform().AffineInverse();
            var topLeft = inv * rect.Position;

            panel.AnchorLeft = 0f;
            panel.AnchorTop = 0f;
            panel.AnchorRight = 0f;
            panel.AnchorBottom = 0f;
            panel.Position = topLeft - new Vector2(Pad, Pad);
            panel.Size = rect.Size + new Vector2(Pad * 2f, Pad * 2f);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] event text panel refine failed: " + e.Message);
        }
    }

    /// <summary>node 往上走，返回它在 root 下的直接子节点。</summary>
    private static Control? AncestorUnderRoot(Control root, Node node)
    {
        Node? current = node;

        while (current != null && current.GetParent() != root)
            current = current.GetParent();

        return current as Control;
    }
}

/// <summary>事件每换一页：先判是不是本 mod 的事件，再决定建/撤，并重新贴合。</summary>
[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.SetEvent))]
public static class EventTextPanelSetEventPatch
{
    [HarmonyPostfix]
    public static void Postfix(NEventLayout __instance, EventModel eventModel)
    {
        var ours = EventTextPanelPatch.IsOurEvent(eventModel);
        EventTextPanelPatch.SetEnabled(__instance, ours);

        GD.Print($"[Subaru] event text panel: {eventModel?.Id} ours={ours}");

        Callable.From(() =>
        {
            if (ours)
                EventTextPanelPatch.Ensure(__instance);
            else
                EventTextPanelPatch.Remove(__instance);
        }).CallDeferred();
    }
}

/// <summary>选项加进来之后再贴合一次（选项数量变化会改变文字列高度）。</summary>
[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.AddOptions))]
public static class EventTextPanelAddOptionsPatch
{
    [HarmonyPostfix]
    public static void Postfix(NEventLayout __instance)
    {
        Callable.From(() => EventTextPanelPatch.Ensure(__instance)).CallDeferred();
    }
}

internal static class EventTextPanelStyle
{
    public static StyleBoxFlat Build()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.04f, 0.07f, 0.82f),
            BorderColor = new Color(0.55f, 0.42f, 0.75f, 0.45f),
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
        };

        style.SetCornerRadiusAll(14);
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(0);
        style.ShadowSize = 10;

        return style;
    }
}
