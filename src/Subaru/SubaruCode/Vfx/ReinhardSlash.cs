using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 剑圣·莱茵哈鲁特（龙剑）的演出 —— 方案 A「一刀两断」的 **T2 版**（2026-11-22 用户定）。
///
/// <para><b>和上一版的区别</b>（上一版只有一条等宽白线，用户评价「太廉价」）：
/// ① 刀光是**四层 + 宽度曲线 + 弧形**（两端收尖，不再是直尺）；
/// ② 有 **3 帧蓄势**（命中点一个收束的光点，先给方向）；
/// ③ 扫过时留 **2 张残影**（smear）；
/// ④ 命中瞬间**画面沿切线撕裂 + 红蓝分离**（<see cref="ScreenSlash"/>，最贵的那一层）；
/// ⑤ 光尘从方块点换成**软圆**（<see cref="SoftDot"/>）；
/// ⑥ 击杀时把敌人**沿切线切成两半**（冷白剪影），静止一拍再滑开。</para>
///
/// <para><b>时间轴</b>：−50~0ms 蓄势 → 0~70ms 扫过 → **70ms 命中（这一刻交回伤害结算）**
/// → 70~180ms 画面撕裂 → 190ms 伤口 → 200~280ms 刀光回缩淡出。整段 280ms，牌只演一次。</para>
///
/// <para><b>铁律</b>：会改敌人本体的可见性，动手前 <see cref="LiveVisuals.Snapshot"/>，
/// 收尾 <see cref="LiveVisuals.RestoreIfAlive"/>（转阶段怪会原地复活，同一只 <c>Visuals</c> 节点）。</para>
/// </summary>
public static class ReinhardSlash
{
    private const float DesignWidth = 1280f;

    /// <summary>斩线角度（度，正 = 从左上到右下）。</summary>
    private const float SlashDeg = 18f;

    /// <summary>伤口比命中晚多少毫秒出现（「斩完才出血」的手感）。</summary>
    private const int WoundDelayMs = 120;

    // ---- 时间轴（毫秒，从起手开始算）----
    private const float AntMs = 50f;         // 蓄势
    private const float SweepMs = 70f;       // 刀光扫过
    private const float RedDelayMs = 8f;     // 红边滞后（露在刃后面）
    private const float RetractFromMs = 45f; // 命中后多久开始回缩
    private const float TailMs = 230f;       // 命中后整段残留 + 清理

    /// <summary>弧的弯度（占刀光长度的比例）。0 = 直尺，这是廉价感的来源之一。</summary>
    private const float ArcSagRatio = 0.055f;

    private const int ArcSegments = 12;

    // ---- 配色 ----
    private static readonly Color EdgeRed = new(0.69f, 0.10f, 0.16f);
    private static readonly Color CoolBlue = new(0.50f, 0.72f, 1f);
    private static readonly Color WarmWhite = new(1f, 0.95f, 0.88f);
    private static readonly Color Silver = new(0.94f, 0.96f, 1f);
    private static readonly Color SilverDark = new(0.72f, 0.78f, 0.90f);

    /// <summary>四层刀光：宽度（设计像素）/ 颜色（含 α）/ 起手延迟 / 沿法线偏移。</summary>
    private static readonly (float W, Color C, float Delay, float Off)[] LayerSpecs =
    [
        (28f, new Color(CoolBlue, 0.30f), 0f, 0f),        // 外层冷光
        (11f, new Color(1f, 1f, 1f, 0.90f), 0f, 0f),      // 中层白
        (3.5f, new Color(WarmWhite, 1f), 0f, 0f),         // 芯（暖白）
        (1.4f, new Color(EdgeRed, 0.75f), RedDelayMs, 2.5f), // 红边（刃的背面）
    ];

    /// <summary>刀光两端收尖的宽度曲线（0.30 → 1.0 → 0.90 → 0.06，末端不为 0 免得几何退化）。</summary>
    private static readonly Curve TaperCurve = BuildTaper();

    private sealed class Layer
    {
        public Line2D Node = null!;
        public Color Color;
        public float BaseWidth;
        public float Delay;
        public float Off;
    }

    private sealed class Half
    {
        public Polygon2D Node = null!;
        public Vector2 Dir;

        /// <summary>位移（已按界面缩放换算成像素）。</summary>
        public float Dist;

        /// <summary>这一半走完自己的位移要多久（终结版两半速度不同）。</summary>
        public float Dur;

        /// <summary>true = 加速下滑（重力），false = 减速上飘。</summary>
        public bool Fall;
    }

    // ─────────────────────────────────────────────────────────
    // 攻击
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 切一刀：返回时**正好是命中那一帧**（伤害由调用方紧接着结算），
    /// 后面的收尾（回缩、伤口、画面恢复）在后台自己走完，不阻塞牌。
    /// </summary>
    public static async Task PlayAsync(Player? player, Creature? target)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = RoomVfx.Front("SubaruReinhardSlash");
            if (tree == null || root == null || target == null)
                return;

            var view = ViewSize(tree);
            var s = view.X / DesignWidth;
            var center = ImpactPoint(target, view);
            var (a, b) = LineEnds(center, view, s);
            var normal = NormalOf(a, b);
            var sag = (b - a).Length() * ArcSagRatio;

            var layers = BuildLayers(root, a, s);
            var dot = SoftDot.Make(center, 40f * s, new Color(1f, 1f, 1f, 0f), true, core: true);
            root.AddChild(dot);

            // ① 蓄势 50ms：光点从大到小收束 + 芯层先露一小截（预告方向）
            await Anim.Ms(tree, AntMs, el =>
            {
                var k = Mathf.Clamp(el / AntMs, 0f, 1f);
                var e = Anim.OutCubic(k);

                SoftDot.SetDiameter(dot, Mathf.Lerp(2.6f, 0.7f, e) * 150f * s);
                dot.Modulate = new Color(1f, 1f, 1f, 0.92f * k);

                foreach (var l in layers)
                {
                    var hint = Mathf.Abs(l.Node.Width) <= 4f * s;
                    l.Node.Points = hint ? Arc(a, b, sag, 0f, 0.05f) : new Vector2[] { a, a };
                    l.Node.DefaultColor = new Color(l.Color, l.Color.A * (hint ? 0.55f * k : 0f));
                }
            });

            Audio.ReinhardSlashSfx.Play();

            // ② 扫过 70ms（红边晚 8ms 起手）+ 第 16/33ms 各拍一张残影
            var smears = 0;
            var sweepTotal = SweepMs + RedDelayMs;

            await Anim.Ms(tree, sweepTotal, el =>
            {
                foreach (var l in layers)
                {
                    var k = Mathf.Clamp((el - l.Delay) / SweepMs, 0f, 1f);
                    var prog = Mathf.Lerp(0.05f, 1f, Anim.OutQuint(k));
                    var pts = Arc(a, b, sag, 0f, prog);

                    if (l.Off > 0f)
                        pts = OffsetNormal(pts, normal, l.Off * s);

                    l.Node.Points = pts;
                    l.Node.DefaultColor = l.Color;
                }

                while (smears < 2 && el >= (smears + 1) * 16f)
                {
                    SpawnSmear(root, layers[1].Node, smears == 0 ? 0.45f : 0.20f);
                    smears++;
                }
            });

            // ③ 命中：画面撕裂 + 抖动 + 光点炸开 + 溅射
            HitFeel.AddTrauma(0.30f);
            ScreenSlash.Play(view, center, SlashDeg, s, heavy: false);

            SoftDot.SetDiameter(dot, 165f * s);
            dot.Modulate = new Color(1f, 1f, 1f, 0.90f);
            var dt = dot.CreateTween().SetParallel(true);
            dt.TweenProperty(dot, "modulate:a", 0f, 0.09);
            dt.TweenProperty(dot, "scale", dot.Scale * 1.8f, 0.09);

            SpawnSparks(root, center, normal, s);

            GD.Print($"[Subaru] reinhard slash: cut at ({center.X:F0},{center.Y:F0}) deg={SlashDeg} " +
                     $"layers={layers.Count} smear={smears} sweep={SweepMs:F0}ms");

            _ = TailAsync(tree, root, layers, a, b, sag, normal, center, s);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard slash failed: " + e.Message);
        }
    }

    /// <summary>命中之后：刀光回缩淡出、延迟出伤口、到点清理。纯视觉，不阻塞。</summary>
    private static async Task TailAsync(SceneTree tree, Node2D root, List<Layer> layers,
        Vector2 a, Vector2 b, float sag, Vector2 normal, Vector2 center, float s)
    {
        var wound = false;

        try
        {
            await Anim.Ms(tree, TailMs, el =>
            {
                if (!wound && el >= WoundDelayMs)
                {
                    wound = true;
                    SpawnWound(root, a, b, sag, normal, s);
                }

                // 前半段保持，后半段从尾巴开始回缩 + 淡出（读作「刀光散掉」而不是「线消失」）
                var k = Mathf.Clamp((el - RetractFromMs) / Mathf.Max(1f, TailMs - RetractFromMs - 20f), 0f, 1f);
                var e = Anim.OutCubic(k);
                var tail = 1f - 0.6f * e;

                foreach (var l in layers)
                {
                    if (!GodotObject.IsInstanceValid(l.Node))
                        continue;

                    var pts = Arc(a, b, sag, 0f, tail);
                    if (l.Off > 0f)
                        pts = OffsetNormal(pts, normal, l.Off * s);

                    l.Node.Points = pts;
                    l.Node.DefaultColor = new Color(l.Color, l.Color.A * (1f - e));

                    // 叠加混合下 alpha 未必吃掉亮度，所以同时把宽度收到 0（两者一起 = 一定看得见在消失）
                    l.Node.Width = l.BaseWidth * Mathf.Max(0.02f, 1f - e);
                }
            });
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard slash tail failed: " + e.Message);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 击杀 / 终结
    // ─────────────────────────────────────────────────────────

    /// <summary>击杀 / 终结：压暗 → 斩线 → 切成两半 → 滑开 → 光尘。返回 Task 由死亡钩子挂住。</summary>
    public static Task? PlayFinisherAsync(Creature? creature, bool finisher, float deathAnimSeconds)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null || creature == null)
                return null;

            var root = RoomVfx.Front("SubaruReinhardFinisher");
            if (root == null)
                return null;

            GD.Print($"[Subaru] reinhard finisher: start ({creature.Name}, finisher={finisher}, anim={deathAnimSeconds:F2}s)");
            return RunFinisherAsync(tree, creature, root, finisher, deathAnimSeconds);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard finisher start failed: " + e.Message);
            return null;
        }
    }

    private static async Task RunFinisherAsync(SceneTree tree, Creature creature, Node2D root,
        bool finisher, float deathAnimSeconds)
    {
        var view = ViewSize(tree);
        var s = view.X / DesignWidth;
        var cnode = creature.GetCreatureNode();
        var visuals = cnode?.Visuals as Node2D;
        var snap = visuals != null ? LiveVisuals.Snapshot(visuals) : null;
        var rect = RectOf(creature, view);
        var center = rect.GetCenter();
        // 终结版要撑得住：下限从 0.9s 提到 1.1s，才放得下「静一拍 + 长静止 + 慢分离」
        var holdMs = Mathf.Clamp((float)deathAnimSeconds * 1000f, finisher ? 1100f : 550f, finisher ? 2400f : 1200f);
        var rng = new Random(System.Environment.TickCount ^ creature.GetHashCode());
        var startAt = Anim.NowMs();

        // 终结 / 普通击杀的节奏差（同一套动作，两种呼吸）
        var silenceMs = finisher ? 120f : 0f;    // 终结：先给一拍什么都不画
        var sweepMs = finisher ? 140f : 100f;    // 终结：刀光扫得慢一档
        var freezeMs = finisher ? 260f : 100f;   // 切开后停多久（「已经断了」）
        var sepMs = finisher ? 420f : 190f;      // 两半分开的窗口

        try
        {
            var (a, b) = LineEnds(center, view, s);
            var sag = (b - a).Length() * ArcSagRatio;
            var normal = NormalOf(a, b);

            // 专属击杀音（通用碎裂音在 EnemyDefeatSfxPatch 里已经被这一支拦掉）。
            // 终结先响 —— 下面那 120ms 的空画面由声音撑着；普通击杀放到切开那一帧再响。
            if (finisher)
                Audio.ReinhardSlashSfx.PlayKill();

            if (silenceMs > 0f)
            {
                GD.Print($"[Subaru] reinhard finisher: silence {silenceMs:F0}ms (intro)");
                await Anim.Ms(tree, silenceMs, _ => { });
            }

            // ① 压暗（整屏压一层冷黑，只有斩线和剪影是亮的）
            var veilAlpha = finisher ? 0.55f : 0.40f;
            var veil = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(0.04f, 0.05f, 0.08f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(veil);
            veil.CreateTween().TweenProperty(veil, "color:a", veilAlpha, 0.09);

            // ② 斩线：三层（冷光晕 / 白 / 红），带弧度和收尖
            var halo = MakeLine(root, 20f, new Color(CoolBlue, 0.25f), s);
            var white = MakeLine(root, 9f, new Color(1f, 1f, 1f, 0.85f), s);
            var red = MakeLine(root, 2.4f, new Color(EdgeRed, 0.95f), s);

            await Anim.Ms(tree, sweepMs, el =>
            {
                var prog = Mathf.Lerp(0.03f, 1f, Anim.OutQuint(el / sweepMs));
                var pts = Arc(a, b, sag, 0f, prog);
                halo.Points = pts;
                white.Points = pts;
                red.Points = OffsetNormal(pts, normal, 1.5f * s);
            });

            // ③ 切：抓这一刻的剪影（本体还可见）→ 沿切线裁成两半 → 换掉本体
            var sil = visuals != null
                ? await SilhouetteCapture.CaptureAsync(tree, visuals, rect, root, Silver, SilverDark)
                : null;

            var halves = BuildHalves(root, rect, center, sil, normal, s, finisher);

            if (visuals != null)
                visuals.Visible = false;

            // 白闪盖住「换身体」那一帧
            var flash = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(1f, 1f, 1f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(flash);
            var ft = flash.CreateTween();
            ft.TweenProperty(flash, "color:a", 0.20f, 0.03);
            ft.TweenProperty(flash, "color:a", 0f, finisher ? 0.22 : 0.16);

            // 普通击杀的专属击杀音就压在这一帧；终结的音已经在开头响过了
            if (!finisher)
                Audio.ReinhardSlashSfx.PlayKill();

            ScreenSlash.Play(view, center, SlashDeg, s, heavy: true);
            SpawnDust(root, sil, rect, normal, s, rng, finisher ? 64 : 30, finisher);

            GD.Print($"[Subaru] reinhard finisher: split (2 halves, " +
                     $"silhouette={(sil?.Tex != null ? "ok" : "FALLBACK")}, " +
                     $"rect={rect.Size.X:F0}x{rect.Size.Y:F0}, veil={veilAlpha:F2}, " +
                     $"finisher={finisher}, sweep={sweepMs:F0}ms, freeze={freezeMs:F0}ms, " +
                     $"sep={sepMs:F0}ms, dust={(finisher ? 64 : 30)})");

            // 压暗层在整个 hold 期间慢慢退掉
            veil.CreateTween().TweenProperty(veil, "color:a", 0f, Mathf.Max(0.35f, holdMs / 1000f));

            // 斩线也退掉：不然一条满屏的亮线会横在画面上直到演出结束（焦点该交给「切开的那两半」）
            var lineFade = root.CreateTween().SetParallel(true);
            foreach (var line in new[] { halo, white, red })
            {
                if (!GodotObject.IsInstanceValid(line))
                    continue;

                lineFade.TweenProperty(line, "width", 0f, 0.30);
                lineFade.TweenProperty(line, "default_color:a", 0f, 0.30);
            }

            // ④ 静止一拍（「已切开」）→ 两半分开（终结：上半上飘得慢、下半下落得快）
            await Anim.Ms(tree, freezeMs, _ => { });
            await Anim.Ms(tree, sepMs, el => SlideHalves(halves, el));

            // ⑤ 终结收尾：极短的冷白闪，收掉余像（普通击杀没有这一下）
            if (finisher)
                FlashCold(root, view);

            // ⑥ 剩下时间跟着死亡动画走（按墙钟算，别和上面几拍累加错）
            var spent = (float)(Anim.NowMs() - startAt);
            var rest = Mathf.Max(60f, holdMs - spent);
            await Anim.Ms(tree, rest, _ => { });

            GD.Print("[Subaru] reinhard finisher: done");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard finisher failed: " + e.Message);
        }
        finally
        {
            if (LiveVisuals.RestoreIfAlive(creature, snap))
                GD.Print("[Subaru] reinhard finisher: restored live visuals (revived)");

            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 切两半
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 沿切线把可视矩形裁成两个半平面，各做成一块带贴图的 <see cref="Polygon2D"/>。
    ///
    /// <para>为什么不用 Sprite2D 的 region：region 是**轴对齐**的，18° 的切线切不出斜口；
    /// <see cref="Polygon2D"/> 可以给每个顶点写 UV，切多少度都精确，而且贴图能跟着走。</para>
    ///
    /// <para>抓帧失败（<paramref name="sil"/> 为 null）时退化成两块浅银灰的纯色，至少形状对。</para>
    /// </summary>
    private static List<Half> BuildHalves(Node2D root, Rect2 rect, Vector2 center,
        SilhouetteCapture.Shot? sil, Vector2 normal, float s, bool finisher)
    {
        var quad = new[]
        {
            rect.Position,
            new Vector2(rect.End.X, rect.Position.Y),
            rect.End,
            new Vector2(rect.Position.X, rect.End.Y),
        };

        var halves = new List<Half>(2);

        for (var side = -1; side <= 1; side += 2)
        {
            var poly = ClipHalfPlane(quad, center, normal * side);
            if (poly.Count < 3)
                continue;

            var node = new Polygon2D
            {
                Position = Vector2.Zero,
                Polygon = poly.Select(p => p - center).ToArray(),
            };

            if (sil?.Tex != null)
            {
                node.Texture = sil.Tex;
                node.UV = poly.Select(p => p - sil.DrawOrigin).ToArray();
                node.Color = Colors.White;
            }
            else
            {
                node.Color = new Color(0.90f, 0.93f, 0.98f, 0.95f);
            }

            var dir = normal * side;

            // 终结：上半（dir.Y < 0）向上飘 40px / 420ms（减速），下半下落 26px / 240ms（加速）
            //       —— 读作「这一刀之后重力回来了」，和普通击杀的对称滑开明显不同。
            // 普通击杀：对称滑开 18px / 190ms。
            var up = dir.Y < 0f;
            var half = new Half { Node = node, Dir = dir };

            if (finisher)
            {
                half.Dist = (up ? 40f : 26f) * s;
                half.Dur = up ? 420f : 240f;
                half.Fall = !up;
            }
            else
            {
                half.Dist = 18f * s;
                half.Dur = 190f;
            }

            root.AddChild(node);
            halves.Add(half);
        }

        return halves;
    }

    /// <summary>Sutherland–Hodgman，只裁一个半平面：保留 <c>dot(p - c, n) &gt;= 0</c> 的一侧。</summary>
    private static List<Vector2> ClipHalfPlane(Vector2[] poly, Vector2 c, Vector2 n)
    {
        var outp = new List<Vector2>(poly.Length + 1);

        for (var i = 0; i < poly.Length; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Length];
            var dp = (p - c).Dot(n);
            var dq = (q - c).Dot(n);

            if (dp >= 0f)
                outp.Add(p);

            if (dp >= 0f != dq >= 0f)
                outp.Add(p.Lerp(q, dp / (dp - dq)));
        }

        return outp;
    }

    /// <summary>两半各走自己的位移（<paramref name="elapsedMs"/> = 从「分开」开始过了多久），同时淡掉。</summary>
    private static void SlideHalves(List<Half> halves, float elapsedMs)
    {
        foreach (var h in halves)
        {
            if (!GodotObject.IsInstanceValid(h.Node))
                continue;

            var k = Mathf.Clamp(elapsedMs / Mathf.Max(1f, h.Dur), 0f, 1f);
            var e = h.Fall ? k * k : Anim.OutCubic(k);

            h.Node.Position = h.Dir * (h.Dist * e);
            h.Node.Modulate = new Color(1f, 1f, 1f, 1f - Mathf.Pow(k, 1.8f));
        }
    }

    /// <summary>终结收尾的冷白闪（极短，只负责把余像收干净）。</summary>
    private static void FlashCold(Node2D root, Vector2 view)
    {
        try
        {
            var flash = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(0.86f, 0.91f, 1f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(flash);

            var tw = flash.CreateTween();
            tw.TweenProperty(flash, "color:a", 0.14f, 0.03);
            tw.TweenProperty(flash, "color:a", 0f, 0.10);
        }
        catch
        {
            // 收尾闪只是锦上添花
        }
    }

    // ─────────────────────────────────────────────────────────
    // 刀光 / 光点 / 光尘
    // ─────────────────────────────────────────────────────────

    private static List<Layer> BuildLayers(Node2D root, Vector2 a, float s)
    {
        var list = new List<Layer>(LayerSpecs.Length);

        foreach (var spec in LayerSpecs)
        {
            var node = new Line2D
            {
                Points = [a, a],
                Width = spec.W * s,
                WidthCurve = TaperCurve,
                DefaultColor = spec.C,
                Antialiased = true,
                JointMode = Line2D.LineJointMode.Round,
                Material = SoftDot.Additive,
            };
            root.AddChild(node);

            list.Add(new Layer
            {
                Node = node,
                Color = spec.C,
                BaseWidth = spec.W * s,
                Delay = spec.Delay,
                Off = spec.Off,
            });
        }

        return list;
    }

    private static Line2D MakeLine(Node2D root, float width, Color color, float s)
    {
        var node = new Line2D
        {
            Points = [Vector2.Zero, Vector2.Zero],
            Width = width * s,
            WidthCurve = TaperCurve,
            DefaultColor = color,
            Antialiased = true,
            JointMode = Line2D.LineJointMode.Round,
            Material = SoftDot.Additive,
        };
        root.AddChild(node);
        return node;
    }

    /// <summary>残影：把此刻的刀光几何**拷一份静止**下来，60ms 内淡掉（= smear 帧）。</summary>
    private static void SpawnSmear(Node2D root, Line2D source, float alpha)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(source) || source.Points.Length < 2)
                return;

            var ghost = new Line2D
            {
                Points = source.Points,
                Width = source.Width,
                WidthCurve = TaperCurve,
                DefaultColor = new Color(1f, 1f, 1f, alpha),
                Antialiased = true,
                JointMode = Line2D.LineJointMode.Round,
                Material = SoftDot.Additive,
            };
            root.AddChild(ghost);

            var tw = ghost.CreateTween().SetParallel(true);
            tw.TweenProperty(ghost, "modulate:a", 0f, 0.06);
            tw.TweenProperty(ghost, "width", 0f, 0.06);
        }
        catch
        {
            // 残影只是加分项
        }
    }

    /// <summary>伤口：命中后 120ms 才出现的一条红线，闪 3 帧就淡掉。</summary>
    private static void SpawnWound(Node2D root, Vector2 a, Vector2 b, float sag, Vector2 normal, float s)
    {
        try
        {
            var wound = new Line2D
            {
                Points = OffsetNormal(Arc(a, b, sag, 0f, 1f), normal, 0.8f * s),
                Width = 3f * s,
                WidthCurve = TaperCurve,
                DefaultColor = new Color(1f, 0.20f, 0.20f, 0.95f),
                Antialiased = true,
                Material = SoftDot.Additive,
            };
            root.AddChild(wound);
            GD.Print("[Subaru] reinhard slash: wound");

            var tw = wound.CreateTween();
            tw.TweenInterval(0.05);
            tw.TweenProperty(wound, "default_color:a", 0f, 0.12);
        }
        catch
        {
            // 伤口只是装饰
        }
    }

    /// <summary>命中点的溅射：沿切线法线两侧飞出去的小光点（软圆，叠加）。</summary>
    private static void SpawnSparks(Node2D root, Vector2 center, Vector2 normal, float s)
    {
        try
        {
            var rng = new Random(9317);

            for (var i = 0; i < 14; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;
                var p = center + normal * ((float)(rng.NextDouble() * 90.0 - 45.0) * s) * side;
                var dir = (normal * side).Rotated((float)(rng.NextDouble() * 0.9 - 0.45));
                var life = 0.14 + rng.NextDouble() * 0.12;
                var size = (2.0f + (float)rng.NextDouble() * 3.2f) * s;

                var dot = SoftDot.Make(p, size, new Color(1f, 0.97f, 0.92f, 0.95f), true);
                root.AddChild(dot);

                var tw = dot.CreateTween().SetParallel(true);
                tw.TweenProperty(dot, "position", p + dir * ((70f + (float)rng.NextDouble() * 110f) * s), life)
                  .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                tw.TweenProperty(dot, "modulate:a", 0f, life);
            }
        }
        catch
        {
            // 溅射只是加分项
        }
    }

    /// <summary>
    /// 光尘：位置从剪影贴图里抽（所以是从轮廓上飘起来，不是方框里的随机点），
    /// 方向跟着自己那一半走（切面朝外喷）。白为主，四分之一是红。
    /// </summary>
    private static void SpawnDust(Node2D root, SilhouetteCapture.Shot? sil, Rect2 rect, Vector2 normal,
        float s, Random rng, int count, bool cold)
    {
        var img = sil?.Img;
        var w = img?.GetWidth() ?? 0;
        var h = img?.GetHeight() ?? 0;
        var drawOrigin = sil?.DrawOrigin ?? rect.Position;
        var rectCenter = rect.GetCenter();

        for (var i = 0; i < count; i++)
        {
            Vector2 p;

            if (img != null && w > 2 && h > 2)
            {
                var x = rng.Next(w);
                var y = rng.Next(h);
                p = drawOrigin + new Vector2(x + 0.5f, y + 0.5f);
            }
            else
            {
                p = new Vector2(rect.Position.X + (float)rng.NextDouble() * rect.Size.X,
                                rect.Position.Y + (float)rng.NextDouble() * rect.Size.Y);
            }

            var side = (p - rectCenter).Dot(normal) >= 0f ? 1f : -1f;
            var dir = (normal * side + new Vector2(0f, -0.28f)).Normalized()
                .Rotated((float)(rng.NextDouble() * 0.7 - 0.35));

            var life = 0.35 + rng.NextDouble() * 0.5;
            var size = (2.2f + (float)rng.NextDouble() * 3.6f) * s;
            var red = rng.Next(cold ? 5 : 4) == 0;

            // 终结版整体偏冷蓝（同一套暗底上更「剑圣」，也和普通击杀的暖白分开）
            var white = cold ? new Color(0.88f, 0.93f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);

            var dot = SoftDot.Make(p, size,
                red ? new Color(1f, 0.36f, 0.32f, 0.95f) : white, true);
            root.AddChild(dot);

            var travel = (12f + (float)rng.NextDouble() * 52f) * s;
            var tw = dot.CreateTween().SetParallel(true);
            tw.TweenProperty(dot, "position", p + dir * (float)travel, (float)life)
              .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tw.TweenProperty(dot, "modulate:a", 0f, (float)life);
            tw.TweenProperty(dot, "scale", dot.Scale * 0.35f, (float)life);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 几何 / 工具
    // ─────────────────────────────────────────────────────────

    /// <summary>刀光的一段弧：<c>t ∈ [t0, t1]</c> 上均分，法线方向按 <see cref="ArcSagRatio"/> 弯一点。</summary>
    private static Vector2[] Arc(Vector2 a, Vector2 b, float sag, float t0, float t1)
    {
        var d = b - a;
        var len = d.Length();
        var dir = len > 0.001f ? d / len : Vector2.Right;
        var nrm = new Vector2(-dir.Y, dir.X);

        var pts = new Vector2[ArcSegments + 1];

        for (var i = 0; i <= ArcSegments; i++)
        {
            var t = Mathf.Lerp(t0, t1, i / (float)ArcSegments);
            pts[i] = a.Lerp(b, t) + nrm * (sag * Mathf.Sin(Mathf.Pi * t));
        }

        return pts;
    }

    private static Vector2[] OffsetNormal(Vector2[] pts, Vector2 normal, float d)
    {
        var outp = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
            outp[i] = pts[i] + normal * d;

        return outp;
    }

    private static Vector2 NormalOf(Vector2 a, Vector2 b)
    {
        var d = b - a;
        var len = d.Length();
        var u = len > 0.001f ? d / len : Vector2.Right;
        return new Vector2(-u.Y, u.X);
    }

    /// <summary>刀光两端收尖（0.30 → 1.0 → 0.90 → 0.06，末端不给 0，免得几何退化出现毛刺）。</summary>
    private static Curve BuildTaper()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.30f));
        curve.AddPoint(new Vector2(0.18f, 1f));
        curve.AddPoint(new Vector2(0.62f, 0.90f));
        curve.AddPoint(new Vector2(1f, 0.06f));
        return curve;
    }

    private static (Vector2 a, Vector2 b) LineEnds(Vector2 center, Vector2 view, float s)
    {
        var th = Mathf.DegToRad(SlashDeg);
        var u = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
        var half = view.Length() * 0.75f;
        return (center - u * half, center + u * half);
    }

    private static Vector2 ImpactPoint(Creature? creature, Vector2 view)
        => RectOf(creature, view).GetCenter();

    private static Rect2 RectOf(Creature? creature, Vector2 view)
    {
        try
        {
            var cnode = creature?.GetCreatureNode();
            if (cnode != null && GodotObject.IsInstanceValid(cnode))
            {
                var r = KillEffect.VisualRect(cnode, cnode.Visuals as Node2D, out var src);
                if (src != "none" && r.Size.X > 4f && r.Size.Y > 4f)
                    return r;
            }
        }
        catch
        {
            // 退到下面
        }

        return new Rect2(new Vector2(view.X * 0.70f, view.Y * 0.35f), new Vector2(view.X * 0.12f, view.Y * 0.30f));
    }

    private static Vector2 ViewSize(SceneTree tree)
    {
        try
        {
            var size = tree.Root?.GetVisibleRect().Size ?? Vector2.Zero;
            if (size.X > 8f && size.Y > 8f)
                return size;
        }
        catch
        {
            // 退到下面
        }

        return new Vector2(1920f, 1080f);
    }
}
