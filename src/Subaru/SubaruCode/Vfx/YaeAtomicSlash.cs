using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 八重·天膳的击杀演出「原子斩」（2026-11-21 用户定）。
///
/// <para>三拍（和原作一致）：**刷线 → 静止 → 才散**。</para>
/// <list type="bullet">
/// <item>刷线：一次性放 32 条细白线（两组倾角），每条 45~85ms 从一端扫到另一端，起手错开 0~90ms
/// —— 读作「一瞬间上百刀」，不是一条条画出来。</item>
/// <item>静止（只终结版有，200ms）：敌人本体在切线结束那一刻**被换成碎块**（原地不动）。
/// **两个版本都会翻底色**：终结 → 灰白 `#e6e4de`（α0.94），普通击杀 → 浅粉红 `#f3cfd6`（α0.88）
/// —— 深酒红底上黑色碎块根本看不见；白线同时退干净（浅底上是噪点）。</item>
/// <item>散开：碎块沿中心向外滑 300ms，同时淡出。</item>
/// </list>
///
/// <para><b>为什么是「不规则的矩形网格」</b>（用户 2026-11-21 指定）：她的能力是操丝，
/// 切面应该是**绷直的钢丝**拉出来的横平竖直的格子，但格子大小参差（每道界线随机挪 ±30% 格宽）。
/// 每块向内缩 <see cref="SeamPx"/> 像素 → 缝里露出底色，才是「被切开」而不是「被打碎」。</para>
///
/// <para><b>挂载点</b>：<c>EnemyDefeatSfxPatch</c> 的 <c>Hook.AfterDeath</c>，返回的 Task 会接到死亡流程上
/// （和夏乌拉同一套阻塞方式），所以静止那一拍战斗不会推进。
/// 底色走 <see cref="RoomVfx.Back"/>（角色之下）、切线与碎块走 <see cref="RoomVfx.Front"/>（角色之上、UI 之下）。</para>
/// </summary>
public static class YaeAtomicSlash
{
    // ---- 配色（和她的立绘/框同一套：深酒红 + 近白）----
    private static readonly Color ShadeColor = new(0.102f, 0.039f, 0.071f);   // #1a0a12
    private static readonly Color LineColor = new(1f, 0.976f, 0.961f);        // #fff9f5 近白
    private const float ShadeAlphaFull = 0.46f;
    private const float ShadeAlphaShort = 0.34f;

    /// <summary>
    /// 终结版在「切」的那一帧把整屏翻成灰白 —— 黑色碎块在深酒红底上根本看不见
    /// （2026-11-21 用户：全屏变灰白，不然看不清碎片）。颜色和夏乌拉终结同色。
    /// </summary>
    private static readonly Color WhiteShade = new(0.902f, 0.894f, 0.871f);   // #e6e4de
    private const float WhiteAlphaFull = 0.94f;
    private const float WhiteFlipMs = 90f;    // 翻白的过渡

    /// <summary>普通击杀版同样翻色，但用她的浅粉红（2026-11-21 用户定），比终结版轻一档。</summary>
    private static readonly Color PinkShade = new(0.953f, 0.812f, 0.839f);    // #f3cfd6
    private const float PinkAlphaShort = 0.88f;
    private const float PinkFlipMs = 80f;

    // ---- 时间轴（ms，墙钟）----
    private const float SlashMs = 120f;        // 切线扫过
    private const float LineStaggerMs = 90f;   // 最后一条线的起手延迟
    private const float LineMinMs = 45f;
    private const float LineMaxMs = 85f;
    private const float TailMs = 160f;         // 收尾淡出
    private const float ShadeInMs = 100f;

    // ---- 崩解成粉（2026-11-21 用户：切碎之后粉末状消失，不再滑开）----
    private const float CrumbleMsFull = 620f;    // 粉末从出现到消失
    private const float CrumbleMsShort = 460f;
    private const float PieceFadeMs = 130f;      // 碎块本体退干净的时间（粉末接上）
    private const float PieceDriftPx = 16f;      // 碎块崩解时外扩的位移（设计像素）
    private const int PowderFull = 170;
    private const int PowderShort = 110;
    private static readonly Color DustColor = new(0.165f, 0.051f, 0.078f);   // #2a0d14 暗红粉

    // ---- 构图 ----
    private const int LineCount = 32;
    private const float LineWidthPx = 3.4f;    // 设计像素（按 1280 宽换算）
    private const int Cols = 4;
    private const int Rows = 5;
    private const float SeamPx = 1.8f;

    /// <summary>一条切线的运行时数据。</summary>
    private sealed class Slash
    {
        public Line2D Node = null!;
        public Vector2 From;
        public Vector2 To;
        public float Delay;
        public float Dur;
    }

    /// <summary>一块碎块（剪影的一格）。</summary>
    private sealed class Piece
    {
        public Node2D Node = null!;
        public Vector2 Origin;
        public Vector2 Dir;
    }

    /// <summary>粉末里的一粒。</summary>
    private sealed class Dust
    {
        public Polygon2D Node = null!;
        public Vector2 Origin;
        public Vector2 Dir;
        public float Dist;
        public float Delay;
        public float Life;
    }

    /// <summary>抓帧结果：贴图（切块用）+ 图像（抽粉末位置用）。</summary>
    private sealed class Silhouette
    {
        public Texture2D? Tex;
        public Image? Img;
    }

    /// <summary>
    /// 播一次原子斩。<paramref name="full"/> = 终结（清场那一击）：有长静止；
    /// false = 普通击杀：刷线 + 碎裂，去掉静止。
    ///
    /// <para><paramref name="deathAnimSeconds"/> 是这只怪的死亡动画时长（<c>Hook.AfterDeath</c> 的最后那个 float）。
    /// **整段演出的长度按它来算**（2026-11-21 用户：底色要一直留到动画结束）——
    /// 终结版跟着走（1.4~3.2s），普通击杀版压到 0.8~1.3s，免得打小怪每只都停 2 秒。</para>
    ///
    /// 返回 null 表示环境不具备 / 击杀特效被关掉（不阻塞死亡）。
    /// </summary>
    public static Task? PlayAsync(Creature? creature, bool full, float deathAnimSeconds = 1f)
    {
        try
        {
            if (creature == null || !GodotObject.IsInstanceValid(creature.GetCreatureNode()))
                return null;

            if (!Settings.SubaruVisualSettings.KillEffectEnabled)
            {
                GD.Print("[Subaru] atomic slash: skipped (kill effect disabled)");
                return null;
            }

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return null;

            var front = RoomVfx.Front("SubaruYaeSlashFront");
            if (front == null)
            {
                GD.Print("[Subaru] atomic slash: skipped (no front container)");
                return null;
            }

            var back = RoomVfx.Back("SubaruYaeSlashBack");
            if (back == null)
                GD.Print("[Subaru] atomic slash: WARNING no back container (底色不会出现)");

            GD.Print($"[Subaru] atomic slash: start ({creature.Name}, full={full}, anim={deathAnimSeconds:F2}s)");
            return RunAsync(tree, creature, back, front, full, deathAnimSeconds);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] atomic slash start failed: " + e.Message);
            return null;
        }
    }

    private static async Task RunAsync(SceneTree tree, Creature creature, Node2D? back, Node2D front,
        bool full, float deathAnimSeconds)
    {
        var view = ViewSize();
        var scale = view.X / 1280f;
        var rng = new Random(System.Environment.TickCount ^ creature.GetHashCode());

        var cutAt = SlashMs + LineStaggerMs;

        // 整段长度跟死亡动画走；终结长、普通击杀短（都要盖住「动画结束」那一刻）
        var anim = deathAnimSeconds > 0.05f ? deathAnimSeconds : 1f;
        var totalMs = full
            ? Mathf.Clamp(anim * 1000f, 1400f, 3200f)
            : Mathf.Clamp(anim * 1000f, 800f, 1300f);

        // 崩解（碎块 → 粉末）在结尾前的 CrumbleMs 开始，粉末正好淡到动画结束
        var crumbleMs = full ? CrumbleMsFull : CrumbleMsShort;
        var crumbleAt = Mathf.Max(cutAt + 140f, totalMs - crumbleMs - 40f);
        var fadeStart = totalMs - TailMs;
        var endMs = totalMs;

        var powderCount = full ? PowderFull : PowderShort;
        List<LiveVisuals.State>? snapshot = null;

        try
        {
            var node = creature.GetCreatureNode();
            var visuals = node?.Visuals as Node2D;

            // 演出会藏掉活着的本体（切线合上前一刻），先存一份原状：
            // 转阶段的怪（实验体这类）在 Hook.AfterDeath 内部就原地复活，第二形态继承同一个 Visuals 节点，
            // 不还原就是「模型被打没」（2026-11-21 用户报，夏乌拉同款问题）。
            if (visuals != null)
                snapshot = LiveVisuals.Snapshot(visuals);

            var rect = node != null && visuals != null
                ? KillEffect.VisualRect(node, visuals, out _)
                : new Rect2();

            var hasRect = visuals != null && rect.Size.X > 4f && rect.Size.Y > 4f;
            if (!hasRect)
            {
                var origin = node?.GetGlobalTransformWithCanvas().Origin ?? view * 0.5f;
                rect = new Rect2(origin - new Vector2(70f, 90f) * scale, new Vector2(140f, 180f) * scale);
            }

            var center = rect.GetCenter();

            // ① 底色：背景之上、角色之下（2 倍铺满，见 RoomVfx 的缩放补偿）
            ColorRect? shade = null;
            if (back != null)
            {
                shade = new ColorRect
                {
                    Position = -view * 0.5f,
                    Size = view * 2f,
                    Color = new Color(ShadeColor, 0f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                back.AddChild(shade);
            }

            // ② 切线：角色之上（整屏交叉，但发源点对着这只怪）
            var lines = BuildLines(front, center, view, scale, rng);

            // ③ 爆闪（切线合上那一刻）
            var flash = new ColorRect
            {
                Position = -view * 0.5f,
                Size = view * 2f,
                Color = new Color(LineColor, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            front.AddChild(flash);

            var t0 = Time.GetTicksMsec();
            var cut = false;
            var crumbled = false;
            var pieces = new List<Piece>();
            var dust = new List<Dust>();
            var silImage = default(Image);

            while (true)
            {
                var now = (float)(Time.GetTicksMsec() - t0);

                // 刷线：每条从起点扫到终点
                foreach (var s in lines)
                {
                    var p = Mathf.Clamp((now - s.Delay) / s.Dur, 0f, 1f);
                    var tip = s.From.Lerp(s.To, EaseOutCubic(p));
                    s.Node.Points = [s.From, tip];
                }

                // 收场淡出：只在最后 TailMs 淡，所以底色会一直盖到动画结束
                var fade = Mathf.Clamp(1f - (now - fadeStart) / TailMs, 0f, 1f);

                if (shade != null)
                {
                    var inA = Mathf.Clamp(now / ShadeInMs, 0f, 1f);

                    // 切完就翻色：终结 → 灰白，普通击杀 → 浅粉红（黑碎块要能看清）
                    var target = full ? WhiteShade : PinkShade;
                    var targetA = full ? WhiteAlphaFull : PinkAlphaShort;
                    var flipMs = full ? WhiteFlipMs : PinkFlipMs;
                    var k = Mathf.Clamp((now - cutAt) / flipMs, 0f, 1f);

                    var col = ShadeColor.Lerp(target, EaseOutCubic(k));
                    var a = Mathf.Lerp(full ? ShadeAlphaFull : ShadeAlphaShort, targetA, k);
                    shade.Color = new Color(col, a * inA * fade);
                }

                // 白线在浅色底上看不见（等于噪点），所以两个版本都在翻色的同时退干净
                var lineA = fade * (1f - Mathf.Clamp((now - cutAt) / 80f, 0f, 1f));

                foreach (var s in lines)
                    s.Node.DefaultColor = new Color(LineColor, lineA);

                // 切：先把敌人这一刻的样子抓成剪影贴图，再按不规则网格切成块
                if (!cut && now >= cutAt)
                {
                    cut = true;
                    FlashNow(flash);
                    Audio.YaeSlashSfx.Play();

                    var sil = visuals != null
                        ? await CaptureSilhouette(tree, visuals, rect, front)
                        : null;

                    silImage = sil?.Img;
                    var drawOrigin = rect.Position - new Vector2(SilhouettePad, SilhouettePad);
                    pieces = BuildPieces(front, rect, sil?.Tex, rng, scale, drawOrigin);

                    if (visuals != null)
                    {
                        try
                        {
                            visuals.Visible = false;
                        }
                        catch
                        {
                            // 藏不掉也无所谓
                        }
                    }

                    GD.Print($"[Subaru] atomic slash: cut ({pieces.Count} pieces, " +
                             $"lines={lines.Count}, rect={rect.Size.X:F0}x{rect.Size.Y:F0}, " +
                             $"silhouette={(sil?.Tex != null ? "ok" : "FALLBACK boxes")}, " +
                             $"shade={(shade != null ? "on" : "OFF")}, " +
                             $"crumbleAt={crumbleAt:F0}ms, endAt={endMs:F0}ms)");
                }

                // 崩：碎块原地化掉 + 一把粉末向外飘散（2026-11-21 用户：切碎之后粉末状消失）
                if (!crumbled && now >= crumbleAt)
                {
                    crumbled = true;
                    dust = SpawnDust(front, silImage, rect, scale, rng, powderCount, crumbleMs);

                    GD.Print($"[Subaru] atomic slash: crumble ({dust.Count} dust, {crumbleMs:F0}ms)");
                }

                if (crumbled)
                {
                    UpdatePieces(pieces, (now - crumbleAt) / PieceFadeMs, scale);
                    UpdateDust(dust, now - crumbleAt);
                }

                if (now >= endMs)
                    break;

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] atomic slash failed: " + e.Message);
        }
        finally
        {
            // 转阶段怪（复活了）要把藏掉的本体还回去；真死的保持「切碎消失」
            if (LiveVisuals.RestoreIfAlive(creature, snapshot))
                GD.Print("[Subaru] atomic slash: restored live visuals (revived)");

            if (back != null && GodotObject.IsInstanceValid(back))
                back.QueueFree();

            if (GodotObject.IsInstanceValid(front))
                front.QueueFree();

            GD.Print("[Subaru] atomic slash: done");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 切线
    // ─────────────────────────────────────────────────────────

    /// <summary>两组倾角（近水平 / 近竖直）各一半，交错排布 —— 才像「无数刀」而不是一排平行线。</summary>
    private static List<Slash> BuildLines(Node2D parent, Vector2 center, Vector2 view, float scale, Random rng)
    {
        var diag = view.Length();
        var half = diag * 0.9f;
        var lines = new List<Slash>(LineCount);

        for (var i = 0; i < LineCount; i++)
        {
            var baseDeg = i % 2 == 0 ? 16f : 74f;
            var deg = baseDeg + (float)(rng.NextDouble() * 22.0 - 11.0);
            var th = Mathf.DegToRad(deg);

            var u = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            var n = new Vector2(-u.Y, u.X);

            var off = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.55f * diag;
            var c = center + n * off;

            var reverse = rng.Next(2) == 0;
            var a = c - u * half;
            var b = c + u * half;

            var line = new Line2D
            {
                Points = [a, a],
                Width = LineWidthPx * scale,
                DefaultColor = LineColor,
                Antialiased = true,
                JointMode = Line2D.LineJointMode.Round,
            };
            parent.AddChild(line);

            lines.Add(new Slash
            {
                Node = line,
                From = reverse ? b : a,
                To = reverse ? a : b,
                Delay = (float)rng.NextDouble() * LineStaggerMs,
                Dur = LineMinMs + (float)rng.NextDouble() * (LineMaxMs - LineMinMs),
            });
        }

        return lines;
    }

    private static void FlashNow(ColorRect flash)
    {
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "color:a", 0.30f, 0.04);
        tw.TweenProperty(flash, "color:a", 0f, 0.14);
    }

    // ─────────────────────────────────────────────────────────
    // 剪影 → 碎块（不规则的矩形网格）
    // ─────────────────────────────────────────────────────────

    /// <summary>抓帧用的视口边距（像素）。贴图原点 = <c>rect.Position - (pad, pad)</c>。</summary>
    private const int SilhouettePad = 6;

    /// <summary>
    /// 把敌人**这一刻的样子**渲染成一张黑色剪影贴图（透明底），之后每一块都用这张图切
    /// —— 这样切的是**剪影**，不是「一个方块」（2026-11-21 用户指出）。
    ///
    /// <para>做法：把 <c>visuals</c> 复制一份（只复制一次），套上平涂黑材质，放进一个和可视矩形等大的
    /// <see cref="SubViewport"/> 里（透明背景），抓一帧。整段演出期间这个视口要留着 ——
    /// 块用的是它的 <see cref="ViewportTexture"/>，视口一删贴图就失效。</para>
    /// </summary>
    private static async Task<Silhouette?> CaptureSilhouette(SceneTree tree, Node2D visuals, Rect2 rect,
        Node2D parent)
    {
        SubViewport? vp = null;

        try
        {
            var w = Mathf.Max(16, Mathf.RoundToInt(rect.Size.X) + SilhouettePad * 2);
            var h = Mathf.Max(16, Mathf.RoundToInt(rect.Size.Y) + SilhouettePad * 2);

            vp = new SubViewport
            {
                Size = new Vector2I(w, h),
                TransparentBg = true,
                Disable3D = true,
                HandleInputLocally = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            parent.AddChild(vp);

            if (visuals.Duplicate() is not Node2D copy)
            {
                vp.QueueFree();
                return null;
            }

            vp.AddChild(copy);

            // 复制品在视口里的位置：屏幕坐标 − 视口原点（视口原点 = rect.Position − pad）
            var xf = visuals.GetGlobalTransformWithCanvas();
            copy.GlobalTransform = new Transform2D(xf.X, xf.Y,
                xf.Origin - rect.Position + new Vector2(SilhouettePad, SilhouettePad));

            KillEffect.ApplyMaterial(copy, KillEffect.BuildFlatMaterial(
                Colors.Black, new Color(0.04f, 0.03f, 0.04f)));

            vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var tex = vp.GetTexture();

            // 粉末的位置从这里抽（要的是「剪影上」的点，不是方框里的点）
            Image? img = null;
            try
            {
                img = tex?.GetImage();
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] atomic slash: silhouette image failed: " + e.Message);
            }

            GD.Print($"[Subaru] atomic slash: silhouette {w}x{h} " +
                     $"tex={(tex != null ? "ok" : "null")} img={(img != null ? $"{img.GetWidth()}x{img.GetHeight()}" : "null")}");

            return new Silhouette { Tex = tex, Img = img };
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] atomic slash: silhouette capture failed: " + e.Message);

            if (vp != null && GodotObject.IsInstanceValid(vp))
                vp.QueueFree();

            return null;
        }
    }

    /// <summary>
    /// 按不规则网格切剪影：每块是一个 <see cref="Sprite2D"/>，区域裁到这一格。
    /// 抓帧失败（<paramref name="silhouette"/> 为 null）时退回纯黑方块，至少不会什么都不显示。
    /// </summary>
    private static List<Piece> BuildPieces(Node2D parent, Rect2 rect, Texture2D? silhouette,
        Random rng, float scale, Vector2 drawOrigin)
    {
        var xs = Boundaries(rect.Position.X, rect.Size.X, Cols, rng);
        var ys = Boundaries(rect.Position.Y, rect.Size.Y, Rows, rng);
        var gap = SeamPx * scale;
        var rectCenter = rect.GetCenter();
        var pieces = new List<Piece>(Cols * Rows);

        for (var cx = 0; cx < Cols; cx++)
        {
            for (var cy = 0; cy < Rows; cy++)
            {
                var x0 = xs[cx] + gap;
                var x1 = xs[cx + 1] - gap;
                var y0 = ys[cy] + gap;
                var y1 = ys[cy + 1] - gap;
                var w = x1 - x0;
                var h = y1 - y0;
                if (w < 2f || h < 2f)
                    continue;

                var center = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);

                if (silhouette != null)
                {
                    var sprite = new Sprite2D
                    {
                        Texture = silhouette,
                        RegionEnabled = true,
                        RegionRect = new Rect2(new Vector2(x0, y0) - drawOrigin, new Vector2(w, h)),
                        Centered = true,
                        Position = center,
                    };
                    parent.AddChild(sprite);
                    pieces.Add(new Piece { Node = sprite, Origin = center, Dir = Outward(center, rectCenter, rng) });
                    continue;
                }

                var poly = new Polygon2D
                {
                    Position = center,
                    Polygon =
                    [
                        new Vector2(x0, y0) - center,
                        new Vector2(x1, y0) - center,
                        new Vector2(x1, y1) - center,
                        new Vector2(x0, y1) - center,
                    ],
                    Color = Colors.Black,
                };
                parent.AddChild(poly);
                pieces.Add(new Piece { Node = poly, Origin = center, Dir = Outward(center, rectCenter, rng) });
            }
        }

        return pieces;
    }

    /// <summary>碎块崩解时往外挪的方向（离中心越远越明显，带一点随机偏角）。</summary>
    private static Vector2 Outward(Vector2 point, Vector2 center, Random rng)
    {
        var dir = point - center;
        if (dir.LengthSquared() < 1f)
            dir = new Vector2(0f, -1f);

        return dir.Normalized().Rotated((float)(rng.NextDouble() * 0.6 - 0.3));
    }

    /// <summary>碎块本体：外扩一点点 + 130ms 内淡成透明（粉末同时在原位铺开，接得上）。</summary>
    private static void UpdatePieces(List<Piece> pieces, float progress, float scale)
    {
        var p = Mathf.Clamp(progress, 0f, 1f);
        var e = EaseOutCubic(p);

        foreach (var piece in pieces)
        {
            if (!GodotObject.IsInstanceValid(piece.Node))
                continue;

            piece.Node.Position = piece.Origin + piece.Dir * (PieceDriftPx * scale * e);
            piece.Node.Modulate = new Color(1f, 1f, 1f, 1f - p);
        }
    }

    /// <summary>
    /// 一把粉末：位置从剪影贴图里抽（alpha &gt; 0.25 的像素），所以粉正好从轮廓上飘起来，
    /// 而不是从方框里飘。每粒带随机延迟/寿命/方向，往外飘 + 略微上浮，边飘边淡。
    /// </summary>
    private static List<Dust> SpawnDust(Node2D parent, Image? img, Rect2 rect, float scale,
        Random rng, int count, float lifeMs)
    {
        var w = img?.GetWidth() ?? 0;
        var h = img?.GetHeight() ?? 0;
        var drawOrigin = rect.Position - new Vector2(SilhouettePad, SilhouettePad);
        var rectCenter = rect.GetCenter();
        var dust = new List<Dust>(count);

        for (var i = 0; i < count; i++)
        {
            var point = default(Vector2);
            var tries = 0;

            while (true)
            {
                tries++;

                if (img != null && w > 2 && h > 2)
                {
                    var x = rng.Next(w);
                    var y = rng.Next(h);
                    if (img.GetPixel(x, y).A < 0.25f && tries < 24)
                        continue;

                    point = drawOrigin + new Vector2(x + 0.5f, y + 0.5f);
                }
                else
                {
                    point = new Vector2(rect.Position.X + (float)rng.NextDouble() * rect.Size.X,
                                        rect.Position.Y + (float)rng.NextDouble() * rect.Size.Y);
                }

                break;
            }

            var size = (1.1f + (float)rng.NextDouble() * 2.1f) * scale;

            var node = new Polygon2D
            {
                Position = point,
                Polygon =
                [
                    new Vector2(-size, -size),
                    new Vector2(size, -size),
                    new Vector2(size, size),
                    new Vector2(-size, size),
                ],
                Color = new Color(DustColor, 0f),
                Visible = false,
            };
            parent.AddChild(node);

            var dir = point - rectCenter;
            if (dir.LengthSquared() < 1f)
                dir = new Vector2(0f, -1f);

            dir = dir.Normalized().Rotated((float)(rng.NextDouble() * 1.1 - 0.55));
            dir += new Vector2(0f, -0.3f);          // 略微上浮
            dir = dir.Normalized();

            dust.Add(new Dust
            {
                Node = node,
                Origin = point,
                Dir = dir,
                Dist = (10f + (float)rng.NextDouble() * 46f) * scale,
                Delay = (float)rng.NextDouble() * 90f,
                // 最晚的一粒也要在 crumbleMs 之内消失（Delay ≤ 90，所以寿命上限 = lifeMs − 110）
                Life = Mathf.Max(120f, (lifeMs - 110f) * (0.6f + (float)rng.NextDouble() * 0.4f)),
            });
        }

        return dust;
    }

    private static void UpdateDust(List<Dust> dust, float elapsedMs)
    {
        foreach (var d in dust)
        {
            if (!GodotObject.IsInstanceValid(d.Node))
                continue;

            var k = Mathf.Clamp((elapsedMs - d.Delay) / Mathf.Max(1f, d.Life), 0f, 1f);
            if (k <= 0f || k >= 1f)
            {
                d.Node.Visible = false;
                continue;
            }

            d.Node.Visible = true;
            d.Node.Position = d.Origin + d.Dir * (d.Dist * EaseOutCubic(k));
            d.Node.Color = new Color(DustColor, 0.85f * Mathf.Pow(1f - k, 1.3f));
        }
    }

    /// <summary>
    /// 网格界线：等分之后每道线随机挪 ±30% 格宽。
    /// 不超过 ±40% 就不会交叉（相邻两道之间本来隔着一整格）。
    /// </summary>
    private static float[] Boundaries(float start, float size, int count, Random rng)
    {
        var arr = new float[count + 1];
        var cell = size / count;

        for (var i = 0; i <= count; i++)
        {
            var jitter = i == 0 || i == count
                ? 0f
                : (float)(rng.NextDouble() * 0.6 - 0.3);

            arr[i] = start + i * cell + jitter * cell;
        }

        return arr;
    }

    // ─────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

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

        return new Vector2(1920f, 1080f);
    }
}
