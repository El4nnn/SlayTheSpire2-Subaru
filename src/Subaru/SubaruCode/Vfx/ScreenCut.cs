using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 八重·天膳（原「操丝术」）专属演出：**四根略微倾斜的线依次切开屏幕**，每切一刀，屏幕就沿着那条线错开一点，
/// 缝里露出背后的虚空；播完自己合拢。
///
/// <para><b>做法</b>：一块盖住全屏的 <see cref="ColorRect"/> + 一个 canvas_item shader。
/// shader 用 <c>hint_screen_texture</c> 读「已经画好的画面」，再把屏幕按 4 条线切成 5 块，
/// 每块整体平移（这就是「分开」），块与块之间没有像素被认领 → 缝 = 虚空。
/// 因为是读实时的屏幕，画面<b>不会定格</b>：伤害数字、受击、卡牌飞走都照常播。</para>
///
/// <para><b>为什么不用「截屏 + 多边形贴图」那套</b>（<see cref="KillEffect"/> 的玻璃碎裂）：
/// 截屏会把画面冻住 0.5 秒，而这是一张 1 费普通攻击、每回合都可能打 —— 看不到伤害数字。
/// 读屏幕纹理没有这个问题，代价是要在 shader 里做「反向查表」（见下）。</para>
///
/// <para><b>几何模型</b>（和 <c>_anim/review/screen_cut.html</c> 一字不差）：
/// 每刀 = 一条无限长直线（单位法线 n、偏移 d，屏幕像素坐标）。
/// 某点 p 属于第 k 块 ⟺ 它在恰好 k 条线的「正侧」—— 所以 k = 数一下几根 line 的 dot(n,p)-d &gt; 0。
/// 第 k 块整体位移 = Σ_j (j &lt; k ? +A_j·n_j : −A_j·n_j)：相邻两块沿 n_j 各让 A_j，
/// 缝宽 = 2A_j；两端的块位移是累加的（所以缝从中间往两边逐级变宽）。</para>
///
/// <para><b>反向查表</b>：目标像素 p 不知道自己原来属于哪块，于是对 k=0..4 试：
/// q = p − disp(k)，若 q 属于第 k 块 → 采样 q。5 块都没认领 = 缝。</para>
///
/// <para><b>时序</b>：每一刀由 <see cref="Cut"/> 触发（所以节奏跟着牌的多段伤害走）：
/// 出线 <see cref="LineMs"/> → 分开 <see cref="OpenMs"/>（带回弹过冲）→ 常亮。
/// <see cref="Finish"/> 之后整体合拢 <see cref="ReleaseMs"/>。
/// 全部按墙钟算（<see cref="Time.GetTicksMsec"/>），不依赖 SceneTreeTimer。</para>
/// </summary>
public static class ScreenCut
{
    /// <summary>出问题一键关：关掉后 <see cref="Start"/> 直接返回 null，牌照常结算。</summary>
    private static bool _enabled = true;

    /// <summary>切屏演出是否启用（设置页的「打击特效」总开关也会读成 false）。</summary>
    public static bool Enabled
    {
        get => _enabled && Settings.SubaruVisualSettings.HitFxEnabled;
        set => _enabled = value;
    }

    /// <summary>比击杀特效再高一层，盖住战斗层和 HUD。</summary>
    private const int Layer = 133;

    /// <summary>最多几刀（跨屏线条 + 1 = 碎片块数）。</summary>
    public const int MaxCuts = 4;

    // ---- 线条构图（和 screen_cut.html 的默认值一致；角度单位：度，线条相对竖直的倾角）----
    private const float BaseAngleDeg = 90f;
    private const float Spread = 0.77f;
    private const float AngleJitterDeg = 5.5f;
    private const float PosJitter = 0.1f;

    // ---- 时序（毫秒）----
    private const int LineMs = 70;
    private const int OpenMs = 85;
    private const int HoldMs = 420;
    private const int ReleaseMs = 160;

    // ---- 外观 ----
    /// <summary>每刀分开多少像素（按 900p 设计，实际按屏幕高等比放大）。</summary>
    private const float SepPxAt900 = 8.5f;

    private const float BackOvershoot = 1.25f;
    private const float LineWidthPxAt900 = 1.3f;
    private const float GlowWidthPxAt900 = 8f;
    private const float GlowIntensity = 0.25f;
    private const float SeamGlow = 0.92f;
    private const float FlashAmount = 0.30f;

    private static Run? _current;
    private static Shader? _shader;
    private static bool _shaderChecked;
    private static bool _shaderOk = true;

    /// <summary>一次切割的运行实例（牌的 OnPlay 拿着它逐刀触发）。</summary>
    public sealed class Run
    {
        internal CanvasLayer? LayerNode;
        internal ColorRect? Rect;
        internal ShaderMaterial? Material;
        internal Vector2 Size;
        internal readonly double[] CutAt = new double[MaxCuts];
        internal readonly Vector2[] Normals = new Vector2[MaxCuts];
        internal readonly float[] Dists = new float[MaxCuts];
        internal bool LinesReady;
        internal int CutCount;
        internal double StartAt;
        internal double FinishAt = -1;
        internal bool Freed;

        /// <summary>下一刀（在每次伤害结算之前调用）。超过 <see cref="MaxCuts"/> 次就忽略。</summary>
        public void Cut()
        {
            if (Freed || CutCount >= MaxCuts)
                return;

            CutAt[CutCount] = NowMs();
            CutCount++;
            GD.Print($"[Subaru] screen cut: cut #{CutCount}/{MaxCuts}");
        }

        /// <summary>打完收工：合拢 + 释放（没打满的刀就不再出现了）。</summary>
        public void Finish()
        {
            if (Freed || FinishAt >= 0)
                return;

            FinishAt = NowMs();
        }
    }

    /// <summary>开始一次演出。返回 null = 关闭 / 拿不到场景树（牌照常结算）。</summary>
    public static Run? Start(int totalCuts = MaxCuts)
    {
        if (!Enabled)
            return null;

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null)
                return null;

            // 上一发还没播完（同一回合连打两张）：直接收掉旧的，别叠层
            Cancel();

            var size = ViewportSize(tree);
            if (size.X < 32f || size.Y < 32f)
                return null;

            var run = new Run { Size = size, StartAt = NowMs() };

            var shader = GetShader();
            if (shader == null || !ShaderLooksValid(shader))
                return null;

            var mat = BuildMaterial(size);
            if (mat == null)
                return null;

            var layer = new CanvasLayer { Layer = Layer, Name = "SubaruScreenCut" };
            var rect = new ColorRect
            {
                Color = new Color(1f, 1f, 1f, 1f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = mat,
            };
            rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(rect);
            tree.Root.AddChild(layer);

            run.LayerNode = layer;
            run.Rect = rect;
            run.Material = (ShaderMaterial)rect.Material;

            _current = run;
            _ = DriveAsync(run, tree);

            GD.Print($"[Subaru] screen cut: start (plan={totalCuts} cuts, screen={size.X:F0}x{size.Y:F0})");
            return run;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] screen cut start failed: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 等这次演出完全收干净（合拢 <see cref="ReleaseMs"/> + 一点余量）。
    /// 在 <see cref="Finish"/> 之后紧接着调，用来把「立绘退场」排到攻击动画播完之后
    /// （2026-11-21 用户定：播放完攻击动画再退场）。
    /// </summary>
    public static async Task WaitReleasedAsync()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return;

        var end = NowMs() + ReleaseMs + 80;
        while (NowMs() < end)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>丢掉当前这次（不播完）。</summary>
    public static void Cancel()
    {
        var run = _current;
        _current = null;
        if (run == null)
            return;

        run.Freed = true;
        if (run.LayerNode != null && GodotObject.IsInstanceValid(run.LayerNode))
            run.LayerNode.QueueFree();
    }

    // ---------------------------------------------------------------- 驱动

    private static async Task DriveAsync(Run run, SceneTree tree)
    {
        try
        {
            var generated = false;

            while (!run.Freed)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                if (run.Freed || tree.Root == null)
                    return;

                if (run.Rect == null || !GodotObject.IsInstanceValid(run.Rect) || run.Material == null)
                    return;

                // 第一刀落地那一刻才真正把线画出来（之前 uniform 全是 0，等于看不见）
                if (!generated && run.CutCount > 0)
                {
                    GenerateLines(run);
                    generated = true;
                }

                var ms = NowMs();

                // 兜底：万一没人调 Finish（出牌被打断/报错），也别把全屏覆盖层留在场上
                if (ms > run.StartAt + 4000)
                    return;

                Update(run, ms);

                if (run.FinishAt >= 0 && ms >= run.FinishAt + ReleaseMs + 80)
                    return;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] screen cut loop failed: " + e.Message);
        }
        finally
        {
            run.Freed = true;
            if (run.LayerNode != null && GodotObject.IsInstanceValid(run.LayerNode))
                run.LayerNode.QueueFree();
            if (_current == run)
                _current = null;
        }
    }

    /// <summary>
    /// 生成 4 条线的几何：基准角度 + 抖动，沿法线均匀铺开（和 screen_cut.html 的
    /// <c>rebuildLines()</c> 同一条公式）。每次打牌略随机 → 每刀位置都不一样。
    /// </summary>
    private static void GenerateLines(Run run)
    {
        if (run.Material == null)
            return;

        var rng = new Random(System.Environment.TickCount ^ run.GetHashCode());
        var w = run.Size.X;
        var h = run.Size.Y;
        var scale = h / 900f;                      // 视觉量按屏幕高缩放

        var nb = BaseAngleDeg * Mathf.Pi / 180f;   // 基准法线角
        var nx0 = Mathf.Cos(nb);
        var ny0 = Mathf.Sin(nb);
        var halfSpan = Mathf.Abs(nx0) * w * 0.5f + Mathf.Abs(ny0) * h * 0.5f;

        var sep = SepPxAt900 * scale;

        var lines = new List<(Vector2 N, float D)>();
        for (var j = 0; j < MaxCuts; j++)
        {
            var f = (j - (MaxCuts - 1) / 2f) / Mathf.Max(1f, (MaxCuts - 1) / 2f);   // -1..1
            var off = f * Spread * halfSpan;
            off += ((float)rng.NextDouble() * 2f - 1f) * PosJitter * halfSpan * 0.35f;

            var th = nb + ((float)rng.NextDouble() * 2f - 1f) * AngleJitterDeg * Mathf.Pi / 180f;
            var px = w * 0.5f + nx0 * off;
            var py = h * 0.5f + ny0 * off;

            var n = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            lines.Add((n, n.X * px + n.Y * py));
        }

        lines.Sort((a, b) => a.D.CompareTo(b.D));

        for (var j = 0; j < MaxCuts; j++)
        {
            var (n, d) = lines[j];
            run.Normals[j] = n;
            run.Dists[j] = d;
            run.Material.SetShaderParameter($"cut{j}", new Vector4(n.X, n.Y, d, 0f));
        }

        run.LinesReady = true;

        run.Material.SetShaderParameter("line_width", LineWidthPxAt900 * scale);
        run.Material.SetShaderParameter("glow_width", GlowWidthPxAt900 * scale);
        run.Material.SetShaderParameter("seam_glow", SeamGlow);
        run.Material.SetShaderParameter("glow_intensity", GlowIntensity);
        run.Material.SetShaderParameter("screen_size", run.Size);

        GD.Print($"[Subaru] screen cut: lines " + string.Join(" | ",
            lines.Select((l, i) => $"#{i} n=({l.N.X:F2},{l.N.Y:F2}) d={l.D:F0}")) + $" sep={sep:F1}px");
    }

    /// <summary>逐帧把「每一刀现在的进度」写进 uniform。</summary>
    private static void Update(Run run, double ms)
    {
        var mat = run.Material;
        if (mat == null)
            return;

        var rel = 1f;
        if (run.FinishAt >= 0)
        {
            var k = Mathf.Clamp(1f - (float)((ms - run.FinishAt) / ReleaseMs), 0f, 1f);
            rel = k * k * (3f - 2f * k);
        }

        var flash = 0f;

        for (var j = 0; j < MaxCuts; j++)
        {
            float rev = 0f;
            float amp = 0f;
            float glow = 0f;

            if (j < run.CutCount)
            {
                var dt = ms - run.CutAt[j];
                rev = Mathf.Clamp((float)dt / LineMs, 0f, 1f);

                var open0 = LineMs * 0.35f;
                var op = Mathf.Clamp(((float)dt - open0) / OpenMs, 0f, 1f);
                var eased = EaseOutBack(op, BackOvershoot * 2.2f);
                amp = SepPxAt900 * (run.Size.Y / 900f) * eased * rel;

                var burst = Mathf.Exp(-Mathf.Max(0f, (float)dt) / 150f) * 0.9f + 0.45f;
                glow = rev * burst;    // 合拢靠 fade uniform 整体退掉

                var f = Mathf.Exp(-Mathf.Pow(((float)dt - 6f) / 45f, 2f));
                flash = Mathf.Max(flash, FlashAmount * f);
            }

            var n = run.Normals[j];
            mat.SetShaderParameter($"cut{j}", new Vector4(n.X, n.Y, run.Dists[j], amp));
            mat.SetShaderParameter($"rev{j}", rev);
            mat.SetShaderParameter($"glow{j}", glow);
        }

        mat.SetShaderParameter("cut_count", run.CutCount);
        mat.SetShaderParameter("flash", flash);
        mat.SetShaderParameter("fade", rel);   // 合拢时线光/切口光靠它整体退掉
    }

    private static float EaseOutBack(float t, float s)
    {
        var u = t - 1f;
        return 1f + (s + 1f) * u * u * u + s * u * u;
    }

    private static double NowMs() => Time.GetTicksMsec();

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
            // 读不到就用默认
        }

        return new Vector2(1920f, 1080f);
    }

    // ---------------------------------------------------------------- shader

    private static ShaderMaterial? BuildMaterial(Vector2 size)
    {
        var shader = GetShader();
        if (shader == null)
            return null;

        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("screen_size", size);
        mat.SetShaderParameter("cut_count", 0);
        return mat;
    }

    /// <summary>
    /// shader 只建一次（编译一次就够）。第一次使用时顺手验一下 uniform 表：
    /// 编译失败时 Godot 拿不到 uniform 列表 → 直接不播，避免整屏变白。
    /// </summary>
    private static Shader? GetShader()
    {
        if (_shader != null)
            return _shader;

        try
        {
            _shader = new Shader { Code = ShaderCode };
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] screen cut: shader create failed: " + e.Message);
            _shaderOk = false;
            return null;
        }

        return _shader;
    }

    /// <summary>
    /// 探针 uniform：**不能用 <c>screen_tex</c>**。
    /// 带 <c>hint_screen_texture</c> 的 uniform 引擎不允许从外部设置，
    /// 所以压根不会出现在 <c>GetShaderUniformList()</c> 里 ——
    /// 2026-11-20 就是拿它当探针，导致每次都被判「编译失败」而静默跳过（日志：
    /// <c>shader uniform screen_tex missing，本次不播</c>）。改用一个普通 uniform。
    /// </summary>
    private const string ProbeUniform = "screen_size";

    /// <summary>uniform 表里必须能看到探针；看不到就认为这个 shader 没编过。</summary>
    private static bool ShaderLooksValid(Shader shader)
    {
        if (_shaderChecked)
            return _shaderOk;

        _shaderChecked = true;

        try
        {
            var list = shader.GetShaderUniformList();
            var names = new List<string>();
            var hasProbe = false;

            foreach (var item in list)
            {
                var dict = item.AsGodotDictionary();
                if (dict == null || !dict.TryGetValue("name", out var name))
                    continue;

                var n = name.AsString();
                names.Add(n);
                if (n == ProbeUniform)
                    hasProbe = true;
            }

            GD.Print($"[Subaru] screen cut: shader uniforms=[{string.Join(",", names)}]");

            if (list.Count == 0 || !hasProbe)
            {
                _shaderOk = false;
                GD.PushWarning($"[Subaru] screen cut: shader 没有 {ProbeUniform} uniform "
                               + $"(count={list.Count})，本次不播");
                return false;
            }

            return true;
        }
        catch
        {
            // 查不出来就当它是好的，别把功能堵死
            return true;
        }
    }

    /// <summary>
    /// 全屏切割 shader。读出「已经画好的画面」，按 4 条线切成 5 块，每块整体平移；
    /// 没被任何一块认领的像素 = 缝 → 虚空色 + 切口发光。
    /// </summary>
    private const string ShaderCode = """
        shader_type canvas_item;

        uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
        uniform vec2 screen_size = vec2(1920.0, 1080.0);
        uniform int cut_count = 0;

        // x,y = 单位法线；z = 直线偏移（像素）；w = 这一刀的分开量（像素）
        uniform vec4 cut0 = vec4(1.0, 0.0, 0.0, 0.0);
        uniform vec4 cut1 = vec4(1.0, 0.0, 0.0, 0.0);
        uniform vec4 cut2 = vec4(1.0, 0.0, 0.0, 0.0);
        uniform vec4 cut3 = vec4(1.0, 0.0, 0.0, 0.0);

        uniform float rev0 = 0.0;
        uniform float rev1 = 0.0;
        uniform float rev2 = 0.0;
        uniform float rev3 = 0.0;

        uniform float glow0 = 0.0;
        uniform float glow1 = 0.0;
        uniform float glow2 = 0.0;
        uniform float glow3 = 0.0;

        uniform vec4 seam_color : source_color = vec4(1.0, 0.95, 1.0, 1.0);
        uniform vec4 void_color : source_color = vec4(0.035, 0.012, 0.06, 1.0);
        uniform float line_width = 1.2;
        uniform float glow_width = 9.0;
        uniform float glow_intensity = 0.6;
        uniform float seam_glow = 0.85;
        uniform float flash = 0.0;
        uniform float fade = 1.0;

        float side_of(vec4 c, vec2 p) {
            return dot(c.xy, p) - c.z;
        }

        // p 属于第几块 = 它在几条线的正侧
        int piece_index(vec2 q) {
            int c = 0;
            if (cut_count > 0 && side_of(cut0, q) > 0.0) { c += 1; }
            if (cut_count > 1 && side_of(cut1, q) > 0.0) { c += 1; }
            if (cut_count > 2 && side_of(cut2, q) > 0.0) { c += 1; }
            if (cut_count > 3 && side_of(cut3, q) > 0.0) { c += 1; }
            return c;
        }

        // 第 k 块的位移：j<k 沿 +n_j，j>=k 沿 -n_j
        vec2 piece_disp(int k) {
            vec2 d = vec2(0.0);
            d += (k > 0 ? 1.0 : -1.0) * cut0.w * cut0.xy;
            d += (k > 1 ? 1.0 : -1.0) * cut1.w * cut1.xy;
            d += (k > 2 ? 1.0 : -1.0) * cut2.w * cut2.xy;
            d += (k > 3 ? 1.0 : -1.0) * cut3.w * cut3.xy;
            return d;
        }

        // 出线进度：沿线条方向从一端拉到另一端
        float reveal_mask(vec4 c, float rev, vec2 p) {
            if (rev <= 0.0) {
                return 0.0;
            }
            vec2 t = vec2(-c.y, c.x);
            float half_span = abs(t.x) * screen_size.x * 0.5 + abs(t.y) * screen_size.y * 0.5;
            float u = dot(t, p - screen_size * 0.5);
            float edge = -half_span + 2.0 * half_span * rev;
            return 1.0 - smoothstep(edge - 24.0, edge, u);
        }

        void fragment() {
            if (cut_count <= 0) {
                COLOR = vec4(0.0);
            } else {
                vec2 p = SCREEN_UV * screen_size;

                vec4 col = void_color;
                bool found = false;

                // 反向查表：试每一块，看 q 是不是落在它自己的地盘里
                for (int k = 0; k <= 4; k++) {
                    vec2 q = p - piece_disp(k);
                    if (piece_index(q) == k) {
                        vec2 uv = clamp(q / screen_size, vec2(0.0005), vec2(0.9995));
                        col = texture(screen_tex, uv);
                        found = true;
                        break;
                    }
                }

                // 离最近的一条切割线多远（缝的发光 + 线条光晕都用它）
                float best = 1e9;
                if (cut_count > 0) { best = min(best, abs(side_of(cut0, p))); }
                if (cut_count > 1) { best = min(best, abs(side_of(cut1, p))); }
                if (cut_count > 2) { best = min(best, abs(side_of(cut2, p))); }
                if (cut_count > 3) { best = min(best, abs(side_of(cut3, p))); }

                vec3 rgb = col.rgb;

                // 缝：露出虚空，靠线越近越亮（= 被切开的断面在发光）
                if (!found) {
                    float g = clamp(exp(-best / max(0.8, line_width * 1.2)) * seam_glow, 0.0, 1.0);
                    rgb = mix(void_color.rgb, seam_color.rgb, g);
                }

                // 线条光晕（叠在所有像素上，所以线没分开时也看得见）
                float halo = 0.0;
                if (cut_count > 0) {
                    halo += glow0 * exp(-abs(side_of(cut0, p)) / max(0.6, glow_width)) * reveal_mask(cut0, rev0, p);
                }
                if (cut_count > 1) {
                    halo += glow1 * exp(-abs(side_of(cut1, p)) / max(0.6, glow_width)) * reveal_mask(cut1, rev1, p);
                }
                if (cut_count > 2) {
                    halo += glow2 * exp(-abs(side_of(cut2, p)) / max(0.6, glow_width)) * reveal_mask(cut2, rev2, p);
                }
                if (cut_count > 3) {
                    halo += glow3 * exp(-abs(side_of(cut3, p)) / max(0.6, glow_width)) * reveal_mask(cut3, rev3, p);
                }

                // 硬芯：线本身就是一道细亮线
                float core = 0.0;
                if (cut_count > 0) {
                    core += rev0 * exp(-abs(side_of(cut0, p)) / max(0.7, line_width)) * reveal_mask(cut0, rev0, p);
                }
                if (cut_count > 1) {
                    core += rev1 * exp(-abs(side_of(cut1, p)) / max(0.7, line_width)) * reveal_mask(cut1, rev1, p);
                }
                if (cut_count > 2) {
                    core += rev2 * exp(-abs(side_of(cut2, p)) / max(0.7, line_width)) * reveal_mask(cut2, rev2, p);
                }
                if (cut_count > 3) {
                    core += rev3 * exp(-abs(side_of(cut3, p)) / max(0.7, line_width)) * reveal_mask(cut3, rev3, p);
                }

                rgb = mix(rgb, seam_color.rgb, clamp(core, 0.0, 1.0) * 0.9 * fade);
                rgb += seam_color.rgb * clamp(halo * glow_intensity, 0.0, 4.0) * fade;
                rgb += vec3(flash);

                COLOR = vec4(rgb, 1.0);
            }
        }
        """;
}
