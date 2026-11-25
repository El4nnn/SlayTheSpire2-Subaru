using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 打击感（L1 顿帧分级 + L2 命中爆点）。
///
/// **L1 顿帧**：按「这一段伤害 / 目标最大生命」分档，调引擎自己的
/// <c>NGame.Instance.DoHitStop(ShakeStrength, ShakeDuration)</c>
/// （原版只在祭仪兽冲锋 / 机甲骑士重斩 / 幻影肢解三个 Boss 大招用过）。
///   &lt;10%      → **不顿帧、不抖屏**（2026-11-20 用户定：小伤害不要有打击感）
///   10–20%     → Weak / Short
///   20–40%     → Medium / Normal
///   ≥40% / 击杀 → Strong / Normal
/// 两条闸门，防止多段牌把画面顿成幻灯片：
///   ① 节流：800ms 内最多 3 次，超了直接丢；
///   ② 只对本 mod 的「有伤害」的命中生效（纯视觉补出来的子弹不打顿帧）。
/// 2026-11-20 用户定：**取消多段递减** —— 同一串弹幕里每一发都按自己那一段的伤害分档，
/// 不再乘 0.8ⁿ。
///
/// **L2 爆点**：命中点放一个径向光爆（代码生成 64×64 渐变贴图，不占美术资源），
/// 0.15s 内放大 1.6 倍并淡出。挂在弹幕的视觉根节点下，坐标系和子弹一致。
///
/// 出问题一键关：<see cref="Enabled"/> = false（顿帧），<see cref="BurstEnabled"/> = false（爆点）。
/// </summary>
public static class HitFeel
{
    /// <summary>打击特效总开关（设置页）是否打开。</summary>
    public static bool MasterOn => Settings.SubaruVisualSettings.HitFxEnabled;

    private static bool _enabled = true;
    private static bool _burstEnabled = true;
    private static bool _flashEnabled = true;

    /// <summary>顿帧总开关。</summary>
    public static bool Enabled
    {
        get => _enabled && MasterOn;
        set => _enabled = value;
    }

    /// <summary>命中爆点总开关。</summary>
    public static bool BurstEnabled
    {
        get => _burstEnabled && MasterOn;
        set => _burstEnabled = value;
    }

    /// <summary>受击闪白开关（调试用；主开关见设置页）。</summary>
    public static bool FlashEnabled
    {
        get => _flashEnabled && MasterOn;
        set => _flashEnabled = value;
    }

    /// <summary>节流窗口（毫秒）。</summary>
    public static int ThrottleWindowMs = 800;

    /// <summary>窗口内最多几次顿帧。</summary>
    public static int ThrottleMax = 3;

    /// <summary>抖动档位阶梯，索引越小越轻。</summary>
    private static readonly ShakeStrength[] Ladder =
    [
        ShakeStrength.None,        // 0
        ShakeStrength.VeryWeak,    // 1
        ShakeStrength.Weak,        // 2
        ShakeStrength.Medium,      // 3
        ShakeStrength.Strong,      // 4
        ShakeStrength.TooMuch,     // 5
    ];

    private static readonly Queue<long> RecentStops = new();

    private static Texture2D? _burstTexture;

    // ---------------------------------------------------------------- L1

    /// <summary>
    /// 一次命中。
    /// </summary>
    /// <param name="amount">这一段的伤害量（用来分档）。</param>
    /// <param name="maxHp">目标的血量上限；≤0 时不做打击感。</param>
    /// <param name="killed">这一发是否打死目标。</param>
    /// <param name="target">被打的目标（受击闪白用；拿不到就只做顿帧）。</param>
    /// <param name="allowHitStop">
    /// 这一段要不要出打击感。多段攻击只有**第 1 段**给 true（2026-11-21 用户定：12 血史莱姆吃一份
    /// 迷尼亚 = 3 段 × 0.30s 顿帧 = 卡 0.9 秒）。第 2 段起直接返回：不顿帧、不抖屏、不闪白。
    /// </param>
    public static void OnHit(decimal amount, int maxHp, bool killed, Creature? target = null,
        bool allowHitStop = true)
    {
        if (!Enabled)
            return;

        var tier = TierFor(amount, maxHp, killed);
        if (tier <= 0)
            return;

        // 多段攻击的第 2 段起：同一串弹幕里不再重复冻结画面
        if (!allowHitStop)
            return;

        if (!TryPassThrottle())
            return;

        var duration = tier >= 3 ? ShakeDuration.Normal : ShakeDuration.Short;

        try
        {
            var game = NGame.Instance;
            if (game != null && GodotObject.IsInstanceValid(game))
                game.DoHitStop(Ladder[tier], duration);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] hit feel failed: " + e.Message);
        }

        // 新增三层（P2）：抖动量按档位累积 → 轻击轻、重击重
        AddTrauma(0.12f * tier);

        if (tier >= 3)
            PunchCamera(tier);

        if (tier >= 2 && target != null)
            FlashCreature(target, killed);
    }

    /// <summary>按「伤害 / 最大生命」分档（0=不做任何打击感，2=Weak … 4=Strong）。</summary>
    private static int TierFor(decimal amount, int maxHp, bool killed)
    {
        if (killed)
            return 4;

        if (maxHp <= 0)
            return 0;

        var frac = (double)amount / maxHp;
        if (frac >= 0.40) return 4;
        if (frac >= 0.20) return 3;
        if (frac >= 0.10) return 2;

        // 不足 10% 血：不顿帧、不抖屏（用户 2026-11-20 定）
        return 0;
    }

    /// <summary>多段递减已取消（2026-11-20）：每段都用同一套分档，不再乘系数。</summary>

    // ---------------------------------------------------------------- P2：trauma 抖动

    /// <summary>抖动量 0~1，位移按 <c>trauma² × 22px</c> 缩放（Vlambeer 那套，2.2/s 线性衰减）。</summary>
    private static float _trauma;
    private static bool _traumaLoopRunning;
    private static ulong _joltUntilMsec;

    /// <summary>累积抖动量。轻击 +0.12、重击 +0.36、击杀 +0.48（按档位）。</summary>
    public static void AddTrauma(float amount)
    {
        if (amount <= 0)
            return;

        _trauma = Math.Clamp(_trauma + amount, 0f, 1f);
        if (!_traumaLoopRunning)
            _ = TraumaLoopAsync();
    }

    /// <summary>
    /// 把抖动量**托到**至少这个值（不是叠加）。给「蓄力」这种连续抖动用：
    /// 每帧托一次就能做出「越蓄越强」的曲线，而靠 <see cref="AddTrauma"/> 打脉冲是做不到的
    /// —— 2.2/s 的衰减每 120ms 就吃掉 0.26，脉冲加 0.07~0.14 完全不累积。
    /// </summary>
    public static void HoldTrauma(float floor)
    {
        if (floor <= 0f)
            return;

        var v = Math.Clamp(floor, 0f, 1f);
        if (v <= _trauma)
            return;

        _trauma = v;
        if (!_traumaLoopRunning)
            _ = TraumaLoopAsync();
    }

    private static async Task TraumaLoopAsync()
    {
        _traumaLoopRunning = true;

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var game = NGame.Instance;
            if (tree == null || tree.Root == null || game == null || !GodotObject.IsInstanceValid(game))
            {
                _trauma = 0f;
                return;
            }

            var target = game.ScreenshakeTarget;
            if (target == null || !GodotObject.IsInstanceValid(target))
            {
                _trauma = 0f;
                return;
            }

            var basePos = target.Position;
            var rng = new Random();
            var last = Time.GetTicksMsec();

            while (_trauma > 0.01f)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                var now = Time.GetTicksMsec();
                var dt = (now - last) / 1000.0;
                last = now;
                _trauma = Math.Max(0f, _trauma - 2.2f * (float)dt);

                // JoltDown 期间让位（它自己会压位置）
                if (now < _joltUntilMsec)
                    continue;

                var mag = _trauma * _trauma * 22f;
                target.Position = basePos + new Vector2(
                    (float)(rng.NextDouble() * 2.0 - 1.0) * mag,
                    (float)(rng.NextDouble() * 2.0 - 1.0) * mag);
            }

            if (GodotObject.IsInstanceValid(target))
                target.Position = basePos;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] trauma shake failed: " + e.Message);
        }
        finally
        {
            _trauma = 0f;
            _traumaLoopRunning = false;
        }
    }

    // ---------------------------------------------------------------- P2：镜头 push

    /// <summary>重击的镜头推近：缩到 1.015~1.02 再弹回（0.15s）。</summary>
    public static void PunchCamera(int tier)
    {
        try
        {
            var game = NGame.Instance;
            if (game == null || !GodotObject.IsInstanceValid(game))
                return;

            var target = game.ScreenshakeTarget;
            if (target == null || !GodotObject.IsInstanceValid(target))
                return;

            target.PivotOffset = target.Size / 2f;
            var baseScale = target.Scale;
            var amount = 1.01f + 0.005f * (tier - 3);   // tier3 → 1.01，tier4 → 1.015

            var tween = target.CreateTween();
            tween.TweenProperty(target, "scale", baseScale * amount, 0.05)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(target, "scale", baseScale, 0.10)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] camera punch failed: " + e.Message);
        }
    }

    // ---------------------------------------------------------------- P2：受击闪白

    /// <summary>
    /// 目标身上叠一份白色平涂剪影，2 帧后消失。
    /// 复用击杀特效那套：<c>NCreature.Visuals</c> 整棵子树 Duplicate → 白材质 → 对齐视口坐标 → 冻结姿势。
    /// </summary>
    public static void FlashCreature(Creature creature, bool killed)
    {
        if (!FlashEnabled || creature == null)
            return;

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null || tree.Root == null)
                return;

            var cnode = creature.GetCreatureNode();
            if (cnode == null || !GodotObject.IsInstanceValid(cnode))
                return;

            var visuals = cnode.Visuals as Node2D;
            if (visuals == null || !GodotObject.IsInstanceValid(visuals))
                return;

            string? animName = null;
            var animTime = 0f;
            try
            {
                if (cnode.HasSpineAnimation)
                {
                    var anim = cnode.SpineAnimation;
                    animName = anim.GetCurrentAnimationName(0);
                    animTime = anim.GetCurrentTrack(0)?.GetTrackTime() ?? 0f;
                }
            }
            catch
            {
                animName = null;
            }

            if (visuals.Duplicate() is not Node2D copy)
                return;

            var layer = new CanvasLayer { Layer = 129, Name = "SubaruHitFlash" };
            var root = new Node2D();
            layer.AddChild(root);
            tree.Root.AddChild(layer);

            root.AddChild(copy);
            copy.GlobalTransform = visuals.GetGlobalTransformWithCanvas();

            var material = BuildWhiteMaterial();
            ApplyFlatMaterial(copy, material);

            if (!string.IsNullOrEmpty(animName))
            {
                try
                {
                    var state = new MegaSprite(FindSpineSprite(copy)!).GetAnimationState();
                    state?.SetAnimation(animName, false, 0);
                    state?.Update(0f);
                    state?.GetCurrent(0)?.SetTrackTime(animTime);
                    state?.Update(0f);
                }
                catch
                {
                    // 姿势同步失败就用默认姿势
                }
            }

            _ = FreeFlashLaterAsync(tree, layer, killed ? 60 : 34);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] hit flash failed: " + e.Message);
        }
    }

    private static async Task FreeFlashLaterAsync(SceneTree tree, CanvasLayer layer, int ms)
    {
        try
        {
            var end = Time.GetTicksMsec() + (ulong)ms;
            while (Time.GetTicksMsec() < end && tree.Root != null)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        catch
        {
            // 树没了就直接释放
        }
        finally
        {
            if (layer != null && GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
        }
    }

    private static void ApplyFlatMaterial(Node node, Material material)
    {
        if (node is CanvasItem item)
        {
            item.Material = material;
            item.Modulate = new Color(1f, 1f, 1f, 1f);
        }

        if (node.HasMethod("set_normal_material"))
        {
            try
            {
                node.Call("set_normal_material", material);
            }
            catch
            {
                // 没有这个入口就只靠 Material
            }
        }

        foreach (var child in node.GetChildren())
            ApplyFlatMaterial(child, material);
    }

    private static Node? FindSpineSprite(Node node)
    {
        if (node.HasMethod("get_animation_state"))
            return node;

        foreach (var child in node.GetChildren())
        {
            var found = FindSpineSprite(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static ShaderMaterial BuildWhiteMaterial()
    {
        const string code = """
            shader_type canvas_item;
            uniform vec4 tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
            uniform float alpha_cut = 0.06;

            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                if (tex.a < alpha_cut) {
                    discard;
                }
                COLOR = vec4(tint.rgb, tex.a);
            }
            """;

        return new ShaderMaterial { Shader = new Shader { Code = code } };
    }

    // ---------------------------------------------------------------- 单次群攻

    /// <summary>
    /// 单次群攻牌的命中：顿一下 + 屏幕向下“踩”一脚（用户 2026-11-20 定）。
    /// 群攻牌拿不到单个目标的最大生命，所以不走分档，固定 Medium/Short。
    /// </summary>
    public static void AoeImpact()
    {
        if (!MasterOn)
            return;

        try
        {
            var game = NGame.Instance;
            if (game != null && GodotObject.IsInstanceValid(game))
                game.DoHitStop(ShakeStrength.Medium, ShakeDuration.Short);
        }
        catch
        {
            // 顿帧失败不影响抖屏
        }

        JoltDown();
    }

    /// <summary>
    /// 向下的屏幕顿挫：把引擎的抖屏容器瞬间压下去，再缓回来。
    /// 只做竖直方向 —— 和引擎自带的随机 2D 抖动区分开，观感是“被砸了一下”。
    /// </summary>
    public static void JoltDown(float pixels = 14f, int ms = 260)
    {
        if (!MasterOn)
            return;

        try
        {
            var game = NGame.Instance;
            if (game == null || !GodotObject.IsInstanceValid(game))
                return;

            var target = game.ScreenshakeTarget;
            if (target == null || !GodotObject.IsInstanceValid(target))
                return;

            var basePos = target.Position;
            target.Position = basePos + new Vector2(0f, pixels);
            _joltUntilMsec = Time.GetTicksMsec() + (ulong)ms;

            var tween = target.CreateTween();
            tween.TweenProperty(target, "position", basePos, ms / 1000.0)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] jolt down failed: " + e.Message);
        }
    }

    private static bool TryPassThrottle()
    {
        var now = (long)(Time.GetTicksMsec());

        while (RecentStops.Count > 0 && now - RecentStops.Peek() > ThrottleWindowMs)
            RecentStops.Dequeue();

        if (RecentStops.Count >= ThrottleMax)
            return false;

        RecentStops.Enqueue(now);
        return true;
    }

    // ---------------------------------------------------------------- L2

    /// <summary>在命中点放一个径向光爆。size ≈ 屏幕单位，建议 60~110。</summary>
    public static void SpawnBurst(Node2D? parent, Vector2 pos, float size = 78f, float tintShift = 0f)
    {
        if (!BurstEnabled || parent == null || !GodotObject.IsInstanceValid(parent))
            return;

        try
        {
            _burstTexture ??= MakeBurstTexture();

            var sprite = new Sprite2D
            {
                Texture = _burstTexture,
                Position = pos,
                Scale = Vector2.One * (size / 64f * 0.55f),
                Modulate = new Color(0.92f, 0.84f, 1f, 0.95f),
                ZIndex = 60,
            };
            sprite.Rotation = (float)(GD.Randf() * Math.PI * 2.0);

            parent.AddChild(sprite);

            var tween = sprite.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(sprite, "scale", Vector2.One * (size / 64f * 1.6f), 0.15);
            tween.TweenProperty(sprite, "modulate:a", 0f, 0.15);
            tween.Chain().TweenCallback(Callable.From(sprite.QueueFree));
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] hit burst failed: " + e.Message);
        }
    }

    /// <summary>生成一次径向渐变贴图（白心 → 透明边），全局复用。</summary>
    private static Texture2D MakeBurstTexture()
    {
        var gradient = new Gradient();
        gradient.SetOffset(0, 0f);
        gradient.SetColor(0, new Color(1f, 1f, 1f, 1f));
        gradient.SetOffset(1, 1f);
        gradient.SetColor(1, new Color(0.85f, 0.75f, 1f, 0f));

        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }
}
