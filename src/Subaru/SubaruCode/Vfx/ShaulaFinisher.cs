using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 贤者·夏乌拉的**终结**演出（2026-11-21 用户定）：不再用通用击杀特效，全部自己做。
///
/// <para>节奏：命中瞬间 → 敌人变成**纯黑剪影** → 剪影上出现一个**与底色同色（灰白 #e6e4de）的圆** →
/// 停顿 500ms → 敌人粉碎（黑剪影消失 + 碎片四散）。</para>
///
/// <para>挂载点：<c>EnemyDefeatSfxPatch</c>（<c>Hook.AfterDeath</c>），返回的 Task 会被接到死亡流程上，
/// 所以「停顿 500ms」期间战斗不会推进 —— 和通用击杀特效同一套阻塞方式。</para>
///
/// <para>剪影不用克隆：直接把**活着的敌人本体**的材质换成平涂黑，死亡动画照常播、始终是黑剪影。
/// 粉碎时把本体一藏、原地炸碎片。</para>
/// </summary>
public static class ShaulaFinisher
{
    private static readonly Color HoleColor = new(0.902f, 0.894f, 0.871f);   // #e6e4de，与终结底色同色

    private const int CircleMs = 90;      // 圆出现
    private const int HoldMs = 500;       // 停顿（用户指定 0.5s）
    private const int ShatterMs = 520;    // 粉碎（碎片飞行；2026-11-21 用户：原来 320ms 太短太挤）
    private const int TailMs = 220;       // 收尾（碎片淡出；从飞行 45% 处就开始淡）

    /// <summary>圆半径 = 敌人可视矩形**短边** × 这个比例（直径 ≈ 60% 短边）。</summary>
    private const float CircleRatio = 0.30f;

    /// <summary>圆半径上限（占屏幕高度的比例），防止整屏大的怪掏出个巨洞。</summary>
    private const float MaxRadiusFraction = 0.13f;

    /// <summary>
    /// 终结演出总时长（ms）。<see cref="ShaulaStrike"/> 用它把灰白底色一直留到演出播完
    /// （2026-11-21 用户：终结时底色要盖住整个动画）。
    /// </summary>
    public const int TotalMs = CircleMs + HoldMs + ShatterMs + TailMs;   // 90+500+320+120 = 1030

    /// <summary>
    /// 播一次夏乌拉的击杀演出。
    /// <paramref name="full"/> = 终结（清空非爪牙）：黑剪影 + 底色圆 + 停顿 0.5s + 粉碎；
    /// false = 普通击杀：直接原地炸碎片（按用户 2026-11-21 的分档）。
    /// 返回 null 表示环境不具备（不阻塞死亡）。
    /// </summary>
    public static Task? PlayAsync(Creature? creature, bool full)
    {
        try
        {
            if (creature == null || !GodotObject.IsInstanceValid(creature.GetCreatureNode()))
                return null;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return null;

            var root = RoomVfx.Front("SubaruShaulaFinisher");
            if (root == null)
                return null;

            GD.Print($"[Subaru] shaula finisher: start ({creature.Name}, full={full})");
            return RunAsync(tree, creature, root, full);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula finisher start failed: " + e.Message);
            return null;
        }
    }

    private static async Task RunAsync(SceneTree tree, Creature creature, Node2D root, bool full)
    {
        var view = ViewSize();
        var center = ViewSize() * 0.5f;
        var scale = view.X / 1280f;
        List<LiveVisuals.State>? snapshot = null;

        try
        {
            var node = creature.GetCreatureNode();
            var visuals = node?.Visuals as Node2D;
            if (node == null || visuals == null)
                return;

            // 演出会直接改**活着的敌人本体**（涂黑 / 藏掉），先存一份原状：
            // 转阶段的怪（实验体吃 AdaptablePower，在 Hook.AfterDeath **内部**就原地复活成下一形态）
            // 继承的是同一个 Visuals 节点，不还原的话第二形态一出生就是「黑的 + 不可见」
            // —— 这就是「夏乌拉把实验体第一阶段的模型打没」（2026-11-21 用户报）。
            snapshot = LiveVisuals.Snapshot(visuals);

            // 命中点 = 可视矩形中心（拿不到就用节点位置往上抬一点）
            var rect = KillEffect.VisualRect(node, visuals, out var source);
            var hasRect = source != "none" && rect.Size.X > 4f && rect.Size.Y > 4f;
            center = hasRect
                ? rect.GetCenter()
                : node.GetGlobalTransformWithCanvas().Origin + new Vector2(0f, -view.Y * 0.12f);

            // 圆的半径跟着**敌人自己的身体**走（2026-11-21 用户：小型怪上太大）：
            //  · 用可视矩形的**短边** → 大怪小怪同一个相对比例，宽矮的怪也不会横着戳出身体；
            //  · rect 已经是 view 空间的像素，**不能再乘 scale**（原来乘了，1920 宽下多放大了 1.5 倍）；
            //  · 去掉原来的 46px 绝对下限 —— 正是它把小型怪的圆撑得比身体还大。
            var radius = hasRect
                ? Mathf.Clamp(Mathf.Min(rect.Size.X, rect.Size.Y) * CircleRatio,
                    6f * scale, view.Y * MaxRadiusFraction)
                : view.Y * 0.10f;

            GD.Print($"[Subaru] shaula finisher: circle radius={radius:F1} " +
                     $"rect={(hasRect ? $"{rect.Size.X:F0}x{rect.Size.Y:F0}" : "none")} " +
                     $"src={source} view={view.X:F0}x{view.Y:F0}");

            if (full)
            {
                // ⓪ 底色续期：灰白底一直留到本演出播完（被打断也不会超过兜底上限）
                ShaulaStrike.KeepBackdropForFinisher(TotalMs);

                // ① 敌人 → 纯黑剪影（直接改活体材质，死亡动画继续播也是黑的）
                var black = KillEffect.BuildFlatMaterial(Colors.Black, new Color(0.08f, 0.08f, 0.08f));
                KillEffect.ApplyMaterial(visuals, black);

                // ② 剪影上出现一个与底色同色的圆
                var hole = new Polygon2D
                {
                    Polygon = Circle(center, 1f, 40),
                    Color = HoleColor,
                };
                root.AddChild(hole);

                await GrowCircle(tree, hole, center, radius, CircleMs);

                // ③ 停顿 0.5 秒
                await WaitMs(tree, HoldMs);

                hole.QueueFree();
            }
            else
            {
                // 普通击杀：只停一小下，让碎片和爆开对得上
                await WaitMs(tree, 80);
            }

            // ④ 粉碎：藏掉本体 + 碎片四散
            try
            {
                visuals.Visible = false;
            }
            catch
            {
                // 藏不掉也无所谓
            }

            Shatter(root, center, radius, scale);
            await WaitMs(tree, ShatterMs + TailMs);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula finisher failed: " + e.Message);
        }
        finally
        {
            // A：转阶段的怪在钩子内部就复活了，而我们改过它的材质和可见性 —— 必须还原。
            // 真死的怪 CurrentHp = 0，这里不还原（保持「粉碎后消失」的现状）。
            if (LiveVisuals.RestoreIfAlive(creature, snapshot))
                GD.Print("[Subaru] shaula finisher: restored live visuals (revived)");

            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();

            GD.Print("[Subaru] shaula finisher: done");
        }
    }

    /// <summary>圆从 0 长到目标半径（用逐帧改多边形，简单可靠）。</summary>
    private static async Task GrowCircle(SceneTree tree, Polygon2D hole, Vector2 center, float radius, int ms)
    {
        var end = Time.GetTicksMsec() + (ulong)ms;
        while (Time.GetTicksMsec() < end)
        {
            var t = 1f - (float)((end - Time.GetTicksMsec()) / (double)ms);
            hole.Polygon = Circle(center, Mathf.Max(1f, radius * Mathf.Clamp(t, 0f, 1f)), 40);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        hole.Polygon = Circle(center, radius, 40);
    }

    /// <summary>
    /// 碎片四散（2026-11-21 用户：太短、太挤、散不开）。
    ///
    /// <para>关键改动：**扩散尺度不再跟着圆半径走**。圆改成按身体大小之后半径只有几十像素，
    /// 碎片就在原地挤成一团（0.4~2.0 倍半径 = 100px 级）。现在扩散尺度按**屏幕高度**取，
    /// 方向用金色角均分 + 少量抖动（避免成团），碎片错开 0~60ms 起飞、飞行途中就开始淡出，
    /// 末尾再加一点下坠，读起来是「炸开 → 飘散」而不是「一闪」。</para>
    /// </summary>
    private static void Shatter(Node2D root, Vector2 center, float radius, float scale)
    {
        var view = ViewSize();
        var spread = Mathf.Max(radius * 2.6f, view.Y * 0.22f);   // 扩散尺度：跟屏幕走，不跟圆走
        var rng = new Random(45_821);

        const int count = 40;
        const float golden = 2.39996323f;   // 金色角：方向天然均分
        var flySec = ShatterMs / 1000.0;
        var fadeSec = TailMs / 1000.0;

        for (var i = 0; i < count; i++)
        {
            var angle = i * golden + (float)(rng.NextDouble() * 0.7 - 0.35);
            var dist = spread * (0.35f + (float)rng.NextDouble() * 1.15f);
            var size = (5f + (float)rng.NextDouble() * 17f) * scale;
            var delay = (float)rng.NextDouble() * 0.06;   // 错开起飞，别像一次闪光

            var shard = new Polygon2D
            {
                Polygon =
                [
                    new Vector2(-size, -size * 0.6f),
                    new Vector2(size * 0.9f, -size * 0.2f),
                    new Vector2(-size * 0.2f, size * 0.9f),
                ],
                Position = center,
                Color = Colors.Black,
                Modulate = new Color(1f, 1f, 1f, 1f),
            };
            root.AddChild(shard);

            // 往外飞 + 轻微下坠
            var target = center
                         + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist
                         + new Vector2(0f, dist * 0.22f);

            var tween = shard.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(shard, "position", target, flySec)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(shard, "rotation", (float)(rng.NextDouble() * Math.PI * 4.0 - Math.PI * 2.0),
                    flySec)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            // 飞行到 45% 就开始淡 —— 等飞完再淡会显得「凝住了一团」
            tween.TweenProperty(shard, "modulate:a", 0f, fadeSec)
                .SetDelay(delay + flySec * 0.45);
        }

        // 粉碎瞬间的一下**压暗**（2026-11-21 用户改）：底色整段是灰白 #e6e4de，
        // 原来那一闪也是灰白 → 同色等于看不见，所以换成黑色。
        var flash = new ColorRect
        {
            Position = -view * 0.5f,
            Size = view * 2f,
            Color = new Color(Colors.Black, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(flash);
        var ft = flash.CreateTween();
        ft.TweenProperty(flash, "color:a", 0.25f, 0.05);
        ft.TweenProperty(flash, "color:a", 0f, 0.18);
    }

    private static Vector2[] Circle(Vector2 center, float radius, int segments)
    {
        var pts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return pts;
    }

    private static async Task WaitMs(SceneTree tree, int ms)
    {
        if (ms <= 0)
            return;

        var end = Time.GetTicksMsec() + (ulong)ms;
        while (Time.GetTicksMsec() < end)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static Vector2 ViewSize()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var size = tree?.Root?.GetVisibleRect().Size ?? Vector2.Zero;
            if (size.X > 8f && size.Y > 8f)
                return size;
        }
        catch
        {
            // 落到默认
        }

        return new Vector2(1280f, 720f);
    }
}
