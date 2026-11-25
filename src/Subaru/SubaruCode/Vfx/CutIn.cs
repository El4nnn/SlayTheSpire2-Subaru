using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 立绘切入（cut-in）：剪影 + 双框（梯形 / 平行四边形）+ 角色 + 遮罩，四层各自速度的视差演出。
///
/// <para><b>参数与素材都由 <c>preview.html</c> 离线产出</b>：
/// 演出参数在 <c>res://Subaru/data/cutin.json</c>（按 id 分组），立绘在
/// <c>res://Subaru/images/cutin/&lt;image&gt;.png</c>（带 alpha）。
/// 改演出不需要改代码，改 json 就行 —— 这也是 <c>docs\cutin_工作流.md</c> 的整个前提。</para>
///
/// <para><b>挂载位置</b>：<see cref="Creature.GetBackVfxContainer"/>（= 房间的
/// <c>BackCombatVfxContainer</c>），在背景之上、角色之下，引擎自己的 VFX 也用这个容器。
/// **不要用 <c>TopLevel</c>**：那会让整棵子树跳出房间的绘制顺序，跑到 UI 和手牌上面
/// （2026-11-21 实测）；根节点改成「抵消容器位移」的位置，坐标照样是屏幕坐标。</para>
///
/// <para><b>绘制顺序</b>（与 preview.html 必须一致）：
/// 框A 填色 → 框B 填色 → 剪影 → 角色 → 遮罩（整屏压暗 + 框挖洞）→ 框A/B 描边。</para>
///
/// <para><b>命中点</b>：<see cref="PlayToHitAsync"/> 会在 <c>timing.hitAt</c> 那一帧返回，
/// 伤害由调用方在那之后再结算 —— 演出本身会自己播完并释放。</para>
/// </summary>
public static class CutIn
{
    private const string SpecPath = "res://Subaru/data/cutin.json";
    private const string ImageDir = "res://Subaru/images/cutin/";
    private const float DesignWidth = 1280f;   // preview.html 的设计宽度：px 类参数按它换算

    // 画序靠「树顺序」而不是 ZIndex：填色 -> 剪影 -> 角色 -> 遮罩 -> 描边
    // （ZIndex 是跨整个 canvas 排序的，一设就会把内容顶到 UI/手牌上面）。

    private static Dictionary<string, Spec>? _specs;
    private static bool _loadFailed;

    // ─────────────────────────────────────────────────────────
    // 对外入口
    // ─────────────────────────────────────────────────────────

    /// <summary>这张牌有没有配演出（没配就什么都不播，不要报错）。</summary>
    public static bool Has(string id) => Specs().ContainsKey(id);

    /// <summary>
    /// 角色立绘总开关：设置页「角色 cut-in」打开，并且「每回合最多不同角色」不是 0。
    /// （关掉只影响立绘，切屏 / 射线 / 击杀特效各有自己的开关。）
    /// </summary>
    public static bool Enabled =>
        Settings.SubaruVisualSettings.CutInEnabled
        && Settings.SubaruVisualSettings.CutInMaxDistinctPerTurn > 0;

    /// <summary>
    /// 这次出牌该不该播立绘（2026-11-21 用户定的规则，设置页可调）：
    /// <list type="bullet">
    /// <item>总开关关掉 / 「每回合最多不同角色」= 0 → 不播；</item>
    /// <item>同一角色每回合默认只出一次（「同一角色可重复出场」打开后可多次）；</item>
    /// <item>每回合最多 N 个**不同**角色出场（默认 3）。</item>
    /// </list>
    ///
    /// <para>**会记账**：返回 true 表示这一次已经占用了名额。</para>
    /// </summary>
    public static bool MayShow(string id)
    {
        if (!Enabled)
            return false;

        var repeat = Settings.SubaruVisualSettings.CutInRepeatSameCharacter;
        if (!repeat && Rewind.CombatStats.HasCutInThisTurn(id))
        {
            GD.Print($"[Subaru] cutin '{id}': skipped (already shown this turn)");
            return false;
        }

        var max = Settings.SubaruVisualSettings.CutInMaxDistinctPerTurn;
        if (!Rewind.CombatStats.TryTakeCutInSlot(id, max))
        {
            GD.Print($"[Subaru] cutin '{id}': skipped (per-turn limit {max} reached)");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 招牌演出（终结那一击 / 一局只打一次的契约牌）：**不受「每回合不同角色」名额限制**，
    /// 也不看「同一角色可重复」设置（用户 2026-11-21：「每回合最多三不同角色（终结特效除外）」），
    /// 只记账，仍然受总开关管。
    /// </summary>
    public static bool MayShowSignature(string id)
    {
        if (!Enabled)
            return false;

        Rewind.CombatStats.TryTakeCutInSlot(id, 99);
        return true;
    }

    /// <summary>
    /// 招牌演出（一局只打一次的契约牌 / 只该出一次的过场）：只受总开关管，不占每回合名额。
    /// 返回到命中点，剩下的收尾交给后台。
    /// </summary>
    public static async Task PlaySignatureAsync(Player? player, string id)
    {
        if (!MayShowSignature(id))
            return;

        await PlayToHitAsync(player, id);
    }

    /// <summary>
    /// 播一次 cut-in，**在命中点返回**（剩下的收尾交给后台）。
    /// 任何异常都只写日志、不影响出牌。
    /// </summary>
    public static Task PlayToHitAsync(Player? player, string id)
        => PlayUntilAsync(player, id, "hit", (run, spec) =>
            run.TotalMs * Mathf.Clamp(spec.Timing.HitAt, 0.05f, 1f));

    /// <summary>
    /// 播一次 cut-in 并**定格**住，返回句柄：调用方演完自己的攻击动画再
    /// <see cref="Session.Exit"/> 放它退场（2026-11-21 用户定：「和夏乌拉一样，播放完攻击动画再退场」）。
    ///
    /// <para>返回到 <c>timing.hitAt</c> 那一刻（和 <see cref="PlayToHitAsync"/> 同一个起点），
    /// 区别是立绘**不会自己走**，会一直停在 hold 上等调用方放行。没人 Exit 的时候
    /// 最多定格 <see cref="MaxHoldMs"/>，不会把立绘永久留在屏幕上。</para>
    /// </summary>
    internal static async Task<Session?> PlayAndHoldAsync(Player? player, string id)
    {
        try
        {
            var spec = Lookup(id);
            if (spec == null)
                return null;

            var run = Start(player, id, spec);
            if (run == null)
                return null;

            run.Held = true;
            _ = DriveAsync(run);

            var session = new Session { Run = run };
            var hitMs = run.TotalMs * Mathf.Clamp(spec.Timing.HitAt, 0.05f, 1f);
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return session;

            // 等到 hitAt：攻击演出从这里起步，立绘停在原地不动
            while (!run.Freed && NowMs() - run.StartAt < hitMs)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            GD.Print($"[Subaru] cutin '{id}': held at {NowMs() - run.StartAt:F0}ms (waiting for caller)");
            return session;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] cutin hold failed: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 播一次 cut-in 并定格到 <paramref name="holdMs"/>（从开播算起），然后自动退场；
    /// 返回的 Task 在**退场之后**才完成 —— 死亡演出用（等敌人的死亡动画播完再收）。
    /// </summary>
    public static async Task PlayAndHoldForAsync(Player? player, string id, double holdMs)
    {
        var session = await PlayAndHoldAsync(player, id);
        if (session?.Run == null)
            return;

        var run = session.Run;
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null)
        {
            while (!run.Freed && NowMs() - run.StartAt < holdMs)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        session.Exit();
    }

    /// <summary>播一次 cut-in 并在 <paramref name="untilMs"/> 指定的时刻返回；收尾交给后台。</summary>
    private static async Task PlayUntilAsync(Player? player, string id, string reason,
        Func<Run, Spec, double> untilMs)
    {
        try
        {
            var spec = Lookup(id);
            if (spec == null)
                return;

            var run = Start(player, id, spec);
            if (run == null)
                return;

            _ = DriveAsync(run);

            var target = untilMs(run, spec);
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return;

            // 墙钟计时，和演出驱动同一个时间基准
            while (!run.Freed && NowMs() - run.StartAt < target)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            GD.Print($"[Subaru] cutin '{id}': {reason} at {NowMs() - run.StartAt:F0}ms (target {target:F0}ms)");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] cutin play failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 构建
    // ─────────────────────────────────────────────────────────

    internal sealed class Run
    {
        public required string Id;
        public required Spec Spec;
        public required Node2D Root;
        public required Node2D Band;
        public required Polygon2D FillA;
        public required Polygon2D FillB;
        public required Line2D LineA;
        public required Line2D LineB;
        public Sprite2D? Sil;
        public Sprite2D? Portrait;
        public ColorRect? Veil;
        public ShaderMaterial? VeilMat;
        public required Vector2 View;
        public required float Bw;
        public required float Bh;
        public required float DesignScale;
        public required double StartAt;
        public required double TotalMs;
        public bool Freed;

        /// <summary>true = 定格在 hold 上，等调用方 <see cref="Session.Exit"/> 才退场。</summary>
        public bool Held;

        /// <summary>退场时刻（ms 相对 StartAt）。-1 = 用 spec 的 <c>inMs + holdMs</c>。</summary>
        public double ExitAt = -1;
    }

    /// <summary>
    /// 一次 cut-in 的句柄（<see cref="PlayAndHoldAsync"/> 返回）：调 <see cref="Exit"/> 才真正退场。
    /// </summary>
    internal sealed class Session
    {
        internal Run? Run;
        private bool _exited;

        /// <summary>开始退场（框滑走、遮罩淡出、立绘离场）。重复调用无副作用。</summary>
        public void Exit()
        {
            var run = Run;
            if (_exited || run == null || run.Freed)
                return;

            _exited = true;
            run.Held = false;
            run.ExitAt = NowMs() - run.StartAt;
            GD.Print($"[Subaru] cutin '{run.Id}': exit at {run.ExitAt:F0}ms (released by caller)");
        }
    }

    /// <summary>定格模式的安全上限：调用方忘了 Exit 也不会把立绘永久留在屏幕上。</summary>
    private const double MaxHoldMs = 9000.0;

    /// <summary>这一刻开始退场（ms 相对 StartAt）；定格期间是无穷大（= 永远不退）。</summary>
    private static double ExitMs(Run run)
    {
        if (run.ExitAt >= 0)
            return run.ExitAt;

        if (run.Held)
            return double.PositiveInfinity;

        var t = run.Spec.Timing;
        return t.InMs + t.HoldMs;
    }

    /// <summary>
    /// 立绘出场的那一帧，让昴本人**抬手**（2026-11-22 用户定：所有 cut-in 都这么做）。
    ///
    /// <para>为什么只发一条 <c>Cast</c>：这套骨架的 <c>attack</c> 和 <c>cast</c> 是**同一段 15 帧**
    /// （<c>cast_0000..0013</c>，已核对 spine-json），第 7 帧（0.25s）就是抬手定格
    /// （见 <c>docs\交接_2026-11-20_打击感与音效清单.md</c>）。所以「抬手」就等于 <c>Cast</c>，
    /// 只是把这件事从「出牌动作」挪到了「立绘出场」上。</para>
    ///
    /// <para>挂在这儿而不是逐张牌接线：cut-in 的调用点有七八处（<c>CutInId</c> 自动接线、
    /// 夏乌拉 / 八重 / 文森特手写），逐个加必漏；<see cref="Start"/> 是所有演出**真正开播**的唯一入口
    /// —— 前面任何一步校验失败都会 return null，所以这里抬手就代表「立绘确实会出来」。</para>
    ///
    /// <para>有专属攻击演出的牌（<c>HasCustomAttackVfx</c>）不再播角色默认动作，
    /// 它们的抬手就靠这一条。</para>
    /// </summary>
    private static void Gesture(Creature creature, string id)
    {
        try
        {
            var node = creature.GetCreatureNode();
            if (node == null)
                return;

            node.SetAnimationTrigger("Cast");
            GD.Print($"[Subaru] cutin '{id}': gesture (Cast / raise hand)");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] cutin '{id}': gesture failed: {e.Message}");
        }
    }

    private static Run? Start(Player? player, string id, Spec spec)
    {
        var creature = player?.Creature;
        var tree = Engine.GetMainLoop() as SceneTree;
        if (creature == null || tree?.Root == null)
            return null;

        if (!ResourceLoader.Exists(ImageDir + spec.Image + ".png"))
        {
            GD.PushWarning($"[Subaru] cutin '{id}': image missing ({ImageDir + spec.Image}.png)");
            return null;
        }

        var tex = GD.Load<Texture2D>(ImageDir + spec.Image + ".png");
        if (tex == null)
        {
            GD.PushWarning($"[Subaru] cutin '{id}': image load failed");
            return null;
        }

        var container = creature.GetBackVfxContainer();
        if (container == null)
        {
            GD.PushWarning($"[Subaru] cutin '{id}': no back vfx container");
            return null;
        }

        var view = ViewportSize(tree);
        if (view.X < 64f || view.Y < 64f)
            return null;

        var run = new Run
        {
            Id = id,
            Spec = spec,
            Root = new Node2D { Name = "SubaruCutIn" },
            Band = null!,
            FillA = null!,
            FillB = null!,
            LineA = null!,
            LineB = null!,
            View = view,
            Bw = view.X * spec.Band.Width,
            Bh = view.Y * spec.Band.Height,
            DesignScale = view.X / DesignWidth,
            StartAt = NowMs(),
            TotalMs = spec.Timing.InMs + spec.Timing.HoldMs + spec.Timing.OutMs,
        };

        // 立绘出场的同时昴本人抬手（2026-11-22 用户定：所有 cut-in 都要这样）
        Gesture(creature, id);

        var bw = run.Bw;
        var bh = run.Bh;
        var cy = view.Y * spec.Band.Y;
        var restX = view.X * spec.Band.X + bw * 0.5f;

        // 不再用 TopLevel（那会让整棵子树跳出房间的绘制顺序，跑到 UI 和手牌上面），
        // 改成把根节点放在「抵消容器位移」的位置：子节点坐标仍然等于屏幕坐标，
        // 但绘制顺序回到 BackCombatVfxContainer 里 —— 背景之上、角色之下、UI 之下。
        // 子树坐标 == 屏幕坐标：位移和缩放都抵消（统一走 RoomVfx，别再手写 Origin —— 有缩放就会偏）
        RoomVfx.AlignToScreen(run.Root, container);

        // 两个同位置的 Band：中间夹一层遮罩（填色/剪影/角色 | 遮罩 | 描边），
        // 这样画序就是树顺序，不需要 ZIndex（ZIndex 是跨整个 canvas 排序的，
        // 一设就会把内容顶到 UI 上面去）。
        var bandPos = new Vector2(restX, cy);
        var bandRot = Mathf.DegToRad(spec.Band.Angle);
        var band = new Node2D { Position = bandPos, Rotation = bandRot };
        run.Band = band;
        run.Root.AddChild(band);

        // ① 两个框的填色（在角色后面）
        run.FillA = MakeFill(spec.Layers.FrameA, bw, bh, view.X);
        run.FillB = MakeFill(spec.Layers.FrameB, bw, bh, view.X);
        band.AddChild(run.FillA);
        band.AddChild(run.FillB);

        // ② 剪影
        if (spec.Layers.Silhouette.On)
        {
            var sil = new Sprite2D
            {
                Texture = tex,
                Centered = true,
                Material = BuildFlatMaterial(ParseColor(spec.Layers.Silhouette.Color)),
            };
            run.Sil = sil;
            band.AddChild(sil);
        }

        // ③ 两个框的描边（在立绘**下面**：角色可以压住线，出框感才对）
        run.LineA = MakeLine(spec.Layers.FrameA, bw, bh, view.X, run.DesignScale);
        run.LineB = MakeLine(spec.Layers.FrameB, bw, bh, view.X, run.DesignScale);
        band.AddChild(run.LineA);
        band.AddChild(run.LineB);

        // ④ 角色
        var portrait = new Sprite2D { Texture = tex, Centered = true };
        run.Portrait = portrait;
        band.AddChild(portrait);

        // ④ 遮罩（屏幕空间；洞跟着框走）
        if (spec.Veil.On && spec.Veil.Alpha > 0.001f)
        {
            var mat = BuildVeilMaterial(ParseColor(spec.Veil.Color), spec.Veil.Alpha);
            if (mat != null)
            {
                var rect = new ColorRect
                {
                    Position = Vector2.Zero,
                    Size = view,
                    Color = new Color(1f, 1f, 1f, 1f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Material = mat,
                };
                run.Veil = rect;
                run.VeilMat = mat;
                run.Root.AddChild(rect);
            }
        }

        // 树顺序 = 画序：填色/剪影/描边/角色 -> 遮罩
        container.AddChild(run.Root);

        GD.Print($"[Subaru] cutin start '{id}' view={view.X:F0}x{view.Y:F0} " +
                 $"band={bw:F0}x{bh:F0} total={run.TotalMs:F0}ms");
        return run;
    }

    private static Polygon2D MakeFill(FrameSpec f, float bw, float bh, float screenW)
    {
        var q = Quad(f, bw, bh, screenW);
        var c0 = ParseColor(f.Color);
        var startA = Mathf.Clamp(f.FillAlpha, 0f, 1f);
        var endA = f.FillGradient != null && f.FillGradient.On
            ? Mathf.Clamp(f.FillGradient.To, 0f, 1f)
            : startA;

        var a = new Color(c0, startA);
        var b = new Color(c0, endA);

        // 顶点顺序：左上 / 右上 / 右下 / 左下
        Color[] colors = f.FillGradient != null && f.FillGradient.Dir == "y"
            ? [a, a, b, b]
            : [a, b, b, a];

        return new Polygon2D
        {
            Polygon = q,
            VertexColors = colors,
            Visible = startA > 0.001f || endA > 0.001f,
        };
    }

    private static Line2D MakeLine(FrameSpec f, float bw, float bh, float screenW, float designScale)
    {
        var q = Quad(f, bw, bh, screenW);
        var pts = new Vector2[5];
        Array.Copy(q, pts, 4);
        pts[4] = q[0];                      // 手动闭合（不依赖 Line2D.Closed）

        return new Line2D
        {
            Points = pts,
            Width = Mathf.Max(0.1f, f.Width * designScale),
            DefaultColor = ParseColor(f.Color),
            Antialiased = true,
            JointMode = Line2D.LineJointMode.Round,
            Visible = f.Width > 0.01f,
        };
    }

    private static Vector2[] Quad(FrameSpec f, float bw, float bh, float screenW)
    {
        var hw = bw * 0.5f * f.Scale;
        var hh = bh * 0.5f * f.Scale;
        float Ox(float p) => screenW * p * f.Scale;

        var c = f.Corners ?? new CornersSpec();
        return
        [
            new Vector2(-hw + Ox(c.LeftTop), -hh),
            new Vector2(hw + Ox(c.RightTop), -hh),
            new Vector2(hw + Ox(c.RightBottom), hh),
            new Vector2(-hw + Ox(c.LeftBottom), hh),
        ];
    }

    // ─────────────────────────────────────────────────────────
    // 逐帧驱动
    // ─────────────────────────────────────────────────────────

    private static async Task DriveAsync(Run run)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return;

        var spec = run.Spec;
        var t = spec.Timing;

        try
        {
            while (!run.Freed)
            {
                if (!GodotObject.IsInstanceValid(run.Root))
                {
                    run.Freed = true;
                    break;
                }

                var now = NowMs() - run.StartAt;
                Update(run, now);

                // 生命周期每帧重算：退场时刻可能是被调用方推迟的（PlayAndHoldAsync 的定格）
                var exitMs = ExitMs(run);
                var total = double.IsInfinity(exitMs) ? MaxHoldMs : exitMs + t.OutMs + 200.0;

                if (now >= total)
                {
                    Finish(run);
                    break;
                }

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] cutin drive failed: " + e.Message);
            Finish(run);
        }
    }

    private static void Update(Run run, double now)
    {
        var spec = run.Spec;
        var t = spec.Timing;
        var w = run.View.X;
        var bw = run.Bw;
        float exitAt = (float)ExitMs(run);

        float InDur(float speed) => Mathf.Max(1f, t.InMs / Mathf.Max(0.05f, speed));
        float OutDur(float speed) => Mathf.Max(1f, t.OutMs / Mathf.Max(0.05f, speed));

        float Pin(float speed, float delay, float extra)
        {
            var dur = InDur(speed);
            return Clamp01(((float)now - delay - extra) / dur);
        }

        float Pout(float speed, float outDelay)
        {
            var dur = OutDur(speed);
            return Clamp01(((float)now - exitAt - outDelay) / dur);
        }

        float Offset(float speed, float delay, float extra, float outSpeed, float outDelay)
        {
            return -bw * 1.3f * (1f - EaseOutCubic(Pin(speed, delay, extra)))
                   + (w + bw) * 0.55f * outSpeed * EaseInCubic(Pout(outSpeed, outDelay));
        }

        float Fade(float speed, float outSpeed, float outDelay) => 1f - EaseInCubic(Pout(outSpeed, outDelay));

        // 框 A / B：填色和描边各自延迟（strokeDelayMs 让线晚一点追上来）
        var fa = spec.Layers.FrameA;
        var fb = spec.Layers.FrameB;
        var ds = run.DesignScale;

        run.FillA.Position = new Vector2(Offset(fa.Speed, fa.DelayMs, 0f, fa.OutSpeed, fa.OutDelayMs) + fa.Dx * ds, 0f);
        run.FillA.Modulate = new Color(1f, 1f, 1f, Fade(fa.Speed, fa.OutSpeed, fa.OutDelayMs));

        run.FillB.Position = new Vector2(Offset(fb.Speed, fb.DelayMs, 0f, fb.OutSpeed, fb.OutDelayMs) + fb.Dx * ds, 0f);
        run.FillB.Modulate = new Color(1f, 1f, 1f, Fade(fb.Speed, fb.OutSpeed, fb.OutDelayMs));

        run.LineA.Position = new Vector2(
            Offset(fa.Speed, fa.DelayMs, fa.StrokeDelayMs, fa.OutSpeed, fa.OutDelayMs) + fa.Dx * ds, 0f);
        run.LineA.Modulate = new Color(1f, 1f, 1f, Fade(fa.Speed, fa.OutSpeed, fa.OutDelayMs));

        run.LineB.Position = new Vector2(
            Offset(fb.Speed, fb.DelayMs, fb.StrokeDelayMs, fb.OutSpeed, fb.OutDelayMs) + fb.Dx * ds, 0f);
        run.LineB.Modulate = new Color(1f, 1f, 1f, Fade(fb.Speed, fb.OutSpeed, fb.OutDelayMs));

        // 剪影
        var sil = spec.Layers.Silhouette;
        var po = spec.Layers.Portrait;
        if (run.Sil != null)
        {
            var silScale = (run.Bh / Mathf.Max(1f, run.Sil.Texture!.GetHeight()))
                           * po.Scale * sil.Scale;
            run.Sil.Position = new Vector2(
                Offset(sil.Speed, sil.DelayMs, 0f, sil.OutSpeed, sil.OutDelayMs) + sil.Dx * ds,
                sil.Dy * ds);
            run.Sil.Scale = new Vector2(silScale, silScale);
            var silA = Mathf.Clamp(sil.Alpha, 0f, 1f) * Fade(sil.Speed, sil.OutSpeed, sil.OutDelayMs);
            run.Sil.Modulate = new Color(1f, 1f, 1f, silA);
        }

        // 角色
        if (run.Portrait != null)
        {
            var pScale = (run.Bh / Mathf.Max(1f, run.Portrait.Texture!.GetHeight())) * po.Scale;
            run.Portrait.Position = new Vector2(
                Offset(po.Speed, po.DelayMs, 0f, po.OutSpeed, po.OutDelayMs),
                po.OffsetY * ds);
            run.Portrait.Scale = new Vector2(pScale, pScale);
            run.Portrait.Modulate = new Color(1f, 1f, 1f, Fade(po.Speed, po.OutSpeed, po.OutDelayMs));
        }

        // 遮罩：透明度自己的淡入淡出，洞跟框
        if (run.Veil != null && run.VeilMat != null)
        {
            var v = spec.Veil;
            var hole = v.Hole == "A" ? fa : fb;
            var vIn = Clamp01(((float)now - hole.DelayMs) / Mathf.Max(1f, v.FadeInMs));
            var vOut = Clamp01(((float)now - exitAt) / Mathf.Max(1f, v.FadeOutMs));
            var a = Mathf.Clamp(v.Alpha, 0f, 1f) * EaseOutCubic(vIn) * (1f - EaseInCubic(vOut));
            run.Veil.Modulate = new Color(1f, 1f, 1f, a);

            var q = Quad(hole, run.Bw, run.Bh, w);
            var off = Offset(hole.Speed, hole.DelayMs, 0f, hole.OutSpeed, hole.OutDelayMs) + hole.Dx * ds;
            // 遮罩用 SCREEN_UV 判定，所以要的是「画布坐标」（房间没有相机变换时 = 屏幕坐标）
            var xf = run.Band.GetGlobalTransformWithCanvas();
            for (var i = 0; i < 4; i++)
            {
                var local = new Vector2(q[i].X + off, q[i].Y);
                var screen = xf * local;
                run.VeilMat.SetShaderParameter("p" + i, new Vector2(screen.X / w, screen.Y / run.View.Y));
            }
        }
    }

    private static void Finish(Run run)
    {
        run.Freed = true;
        try
        {
            if (GodotObject.IsInstanceValid(run.Root))
            {
                run.Root.QueueFree();
                GD.Print("[Subaru] cutin freed");
            }
        }
        catch
        {
            // 收尾失败无所谓，节点会被场景一起清掉
        }
    }

    // ─────────────────────────────────────────────────────────
    // 材质
    // ─────────────────────────────────────────────────────────

    /// <summary>剪影：保留 alpha，把所有像素涂成同一个颜色（和 kill effect 的剪影同一招）。</summary>
    private static ShaderMaterial BuildFlatMaterial(Color tint)
    {
        const string code = """
            shader_type canvas_item;
            uniform vec4 tint : source_color = vec4(0.55, 0.23, 0.06, 1.0);

            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                if (tex.a < 0.03) {
                    discard;
                }
                COLOR = vec4(tint.rgb, tint.a * tex.a);
            }
            """;

        var mat = new ShaderMaterial { Shader = new Shader { Code = code } };
        mat.SetShaderParameter("tint", new Color(tint.R, tint.G, tint.B, 1f));
        return mat;
    }

    /// <summary>
    /// 遮罩：整屏压暗，把凸四边形（p0..p3）挖成洞。
    ///
    /// <para>为什么用 shader 而不是多边形：Godot 的 Polygon2D 不能带洞，
    /// 而「屏幕减去一个凸四边形」用四条带拼会在角上叠色/漏缝。
    /// 凸多边形的内外判定对仿射不变（只看叉积符号），所以传 UV 就够了，不用管分辨率/拉伸。</para>
    /// </summary>
    private static ShaderMaterial? BuildVeilMaterial(Color tint, float alpha)
    {
        try
        {
            const string code = """
                shader_type canvas_item;
                uniform vec4 tint : source_color = vec4(0.10, 0.06, 0.08, 1.0);
                uniform vec2 p0 = vec2(0.0);
                uniform vec2 p1 = vec2(0.0);
                uniform vec2 p2 = vec2(0.0);
                uniform vec2 p3 = vec2(0.0);

                float cross2(vec2 a, vec2 b) { return a.x * b.y - a.y * b.x; }

                void fragment() {
                    vec2 p = SCREEN_UV;
                    float d0 = cross2(p1 - p0, p - p0);
                    float d1 = cross2(p2 - p1, p - p1);
                    float d2 = cross2(p3 - p2, p - p2);
                    float d3 = cross2(p0 - p3, p - p3);
                    bool inside = (d0 >= 0.0 && d1 >= 0.0 && d2 >= 0.0 && d3 >= 0.0)
                               || (d0 <= 0.0 && d1 <= 0.0 && d2 <= 0.0 && d3 <= 0.0);
                    COLOR = vec4(tint.rgb, inside ? 0.0 : tint.a);
                }
                """;

            var mat = new ShaderMaterial { Shader = new Shader { Code = code } };
            mat.SetShaderParameter("tint", new Color(tint.R, tint.G, tint.B, Mathf.Clamp(alpha, 0f, 1f)));
            return mat;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] cutin veil material failed: " + e.Message);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────

    private static double NowMs() => Time.GetTicksMsec();

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    private static float EaseInCubic(float x) => x * x * x;

    private static Vector2 ViewportSize(SceneTree tree)
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

        return new Vector2(1280f, 720f);
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Colors.White;

        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3)
            h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        if (h.Length < 6)
            return Colors.White;

        try
        {
            var r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
            var g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
            var b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            return new Color(r, g, b, 1f);
        }
        catch
        {
            return Colors.White;
        }
    }

    private static Spec? Lookup(string id)
    {
        var all = Specs();
        return all.TryGetValue(id, out var spec) ? spec : null;
    }

    private static Dictionary<string, Spec> Specs()
    {
        if (_specs != null)
            return _specs;

        _specs = [];

        if (_loadFailed)
            return _specs;

        try
        {
            // 注意：不能只用 ResourceLoader.Exists —— 它只认「导入过的资源」，
            // 而 .json 是原样打进 pck 的 raw file（kill effect 的玻璃模板踩过同一个坑）。
            if (!Godot.FileAccess.FileExists(SpecPath))
            {
                _loadFailed = true;
                GD.PushWarning($"[Subaru] cutin spec missing: {SpecPath}");
                return _specs;
            }

            var text = Godot.FileAccess.GetFileAsString(SpecPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                _loadFailed = true;
                return _specs;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Spec>>(text, options);
            if (parsed == null)
            {
                _loadFailed = true;
                return _specs;
            }

            _specs = parsed;
            GD.Print($"[Subaru] cutin spec loaded: {_specs.Count} entr(ies)");
        }
        catch (Exception e)
        {
            _loadFailed = true;
            GD.PushWarning("[Subaru] cutin spec parse failed: " + e.Message);
        }

        return _specs;
    }

    // ─────────────────────────────────────────────────────────
    // 参数模型（字段名对应 cutin.json）
    // ─────────────────────────────────────────────────────────

    public sealed class Spec
    {
        public string Image { get; set; } = "shaula";
        public BandSpec Band { get; set; } = new();
        public LayerSet Layers { get; set; } = new();
        public VeilSpec Veil { get; set; } = new();
        public TimingSpec Timing { get; set; } = new();
        public bool ShowText { get; set; }
        public string Layer { get; set; } = "behindCreatures";
        public float ShakePx { get; set; } = 7f;
        public float FlashAlpha { get; set; } = 0.34f;
    }

    public sealed class BandSpec
    {
        public float Width { get; set; } = 0.58f;
        public float Height { get; set; } = 0.98f;
        public float X { get; set; }
        public float Y { get; set; } = 0.5f;
        public float Angle { get; set; }
    }

    public sealed class LayerSet
    {
        public SilSpec Silhouette { get; set; } = new();
        public FrameSpec FrameA { get; set; } = new();
        public FrameSpec FrameB { get; set; } = new();
        public PortraitSpec Portrait { get; set; } = new();
    }

    public sealed class SilSpec
    {
        public bool On { get; set; } = true;
        public string Color { get; set; } = "#8c3b10";
        public float Alpha { get; set; } = 0.85f;
        public float Scale { get; set; } = 1f;
        public float Dx { get; set; }
        public float Dy { get; set; }
        public float Speed { get; set; } = 1f;
        public float DelayMs { get; set; }
        public float OutSpeed { get; set; } = 1f;
        public float OutDelayMs { get; set; }
    }

    public sealed class PortraitSpec
    {
        public float Scale { get; set; } = 1f;
        public float OffsetY { get; set; }
        public bool Clip { get; set; }
        public float Speed { get; set; } = 1f;
        public float DelayMs { get; set; }
        public float OutSpeed { get; set; } = 1f;
        public float OutDelayMs { get; set; }
    }

    public sealed class FrameSpec
    {
        public string Color { get; set; } = "#e07826";
        public float Width { get; set; } = 4f;
        public float FillAlpha { get; set; }
        public GradSpec? FillGradient { get; set; }
        public float Glow { get; set; }
        public float Scale { get; set; } = 1f;
        public float Dx { get; set; }
        public CornersSpec? Corners { get; set; }
        public float Speed { get; set; } = 1f;
        public float DelayMs { get; set; }
        public float OutSpeed { get; set; } = 1f;
        public float OutDelayMs { get; set; }
        public float StrokeDelayMs { get; set; }
    }

    public sealed class CornersSpec
    {
        public float LeftTop { get; set; }
        public float LeftBottom { get; set; }
        public float RightTop { get; set; }
        public float RightBottom { get; set; }
    }

    public sealed class GradSpec
    {
        public bool On { get; set; }
        public float To { get; set; }
        public string Dir { get; set; } = "x";
    }

    public sealed class VeilSpec
    {
        public bool On { get; set; } = true;
        public string Color { get; set; } = "#1a0f14";
        public float Alpha { get; set; } = 0.35f;
        public string Hole { get; set; } = "B";
        public float FadeInMs { get; set; } = 120f;
        public float FadeOutMs { get; set; } = 140f;
    }

    public sealed class TimingSpec
    {
        public float InMs { get; set; } = 180f;
        public float HoldMs { get; set; } = 300f;
        public float OutMs { get; set; } = 140f;
        public float HitAt { get; set; } = 0.59f;
        public string EaseIn { get; set; } = "outCubic";
        public string EaseOut { get; set; } = "inCubic";
    }
}
