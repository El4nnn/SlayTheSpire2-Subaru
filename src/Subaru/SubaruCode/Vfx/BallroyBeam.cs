using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 魔弹射手·巴尔罗伊的专属演出：**天降光柱**（Holy Smite / pillar of light）。
///
/// <para>2026-11-22 用户定：光从敌人正上方垂直砸下来；终结时整屏刷白，光柱与目标一起变成黑剪影。</para>
///
/// <para>结构（2026-11-22 升级）：柱体 = <see cref="BeamQuad"/>（一个 quad + shader：高斯衰减 + 纵向流动噪声 + 溶解）；
/// 粒子 = <see cref="BallroyParticles"/>（引擎 <c>GpuParticles2D</c>）；配色 = <see cref="BeamPalette"/>。
/// shader 编译失败时自动退回三层 Line2D（首版方案），效果降级但不空。</para>
///
/// <para>三档跟着三段伤害：一档青蓝（10）、二档紫（15，尸兵版固定）、三档金红（20）。</para>
/// </summary>
public static class BallroyBeam
{
    private const float DesignWidth = 1280f;

    private static readonly Color FlashCol = new(0.90f, 0.89f, 0.85f);   // #E6E3D9 暖中性白（护眼）
    private static readonly Color InkCol = new(0.04f, 0.04f, 0.05f);     // 负片剪影

    private sealed class Timing
    {
        public float TeleMs;
        public float DropMs;
        public float HoldMs;
        public float Trauma;
        public float Flash;
    }

    private static Timing TimingOf(int tier) => tier switch
    {
        <= 1 => new Timing { TeleMs = 200f, DropMs = 80f, HoldMs = 150f, Trauma = 0.24f, Flash = 0f },
        2 => new Timing { TeleMs = 220f, DropMs = 85f, HoldMs = 175f, Trauma = 0.40f, Flash = 0.10f },
        _ => new Timing { TeleMs = 240f, DropMs = 90f, HoldMs = 195f, Trauma = 0.55f, Flash = 0.16f },
    };

    // 「最近一次巴尔罗伊伤害」时间戳：延迟段没有 card 来源，靠它让击杀钩子认出是自己的伤害
    private static double _lastDamageMs;

    /// <summary>伤害打出前打点（卡牌本体 + 两段延迟都调）。</summary>
    public static void NoteDamage() => _lastDamageMs = Time.GetTicksMsec();

    /// <summary>这一下击杀是不是巴尔罗伊造成的（默认 1.5s 窗口）。</summary>
    public static bool IsRecentDamage(double windowMs = 1500.0)
    {
        var last = _lastDamageMs;
        return last > 0.0 && Time.GetTicksMsec() - last <= windowMs;
    }

    /// <summary>
    /// 这一击是不是「终结必杀」：是的话**不放普通光柱**，直接把这一击的攻击动画换成终结动画
    /// （2026-11-22 用户定：「把终结特效改为终结攻击动画」）。
    ///
    /// <para>判据两条：① 打完就清场（<see cref="ShaulaStrike.WillBeLastKill"/> —— 必须排除目标自己，
    /// 出牌那一刻它还活着）；② 这一击的实际伤害扣掉格挡后 ≥ 目标当前血量。</para>
    /// </summary>
    public static bool WillBeFinisherKill(Player? player, Creature? target, decimal amount, ValueProp props,
                                          CardModel? cardSource = null, CardPlay? cardPlay = null)
    {
        try
        {
            if (player == null || target == null || target.IsDead)
                return false;

            if (!ShaulaStrike.WillBeLastKill(player, target))
                return false;

            var hpLoss = PredictHpLoss(player, target, amount, props, cardSource, cardPlay);
            var lethal = hpLoss >= target.CurrentHp;

            GD.Print($"[Subaru] ballroy finisher predict: {target.Name} hp={target.CurrentHp} "
                     + $"block={target.Block} raw={amount} actual={hpLoss} "
                     + $"=> {(lethal ? "LETHAL (attack anim = 终结)" : "survives (normal beam)")}");

            return lethal;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy finisher predict failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 这一击大概掉多少血：走引擎自己的 <c>Hook.ModifyDamage</c>（和真正结算时同一条链，
    /// 含力量 / 易伤 / 衰弱 / 无实体上限 / 附魔），再扣掉目标格挡。
    /// 拿不到（异常、没有 CombatState）就退回「原始伤害 - 格挡」。
    /// </summary>
    private static decimal PredictHpLoss(Player player, Creature target, decimal amount, ValueProp props,
                                         CardModel? cardSource, CardPlay? cardPlay)
    {
        var damage = amount;
        try
        {
            var state = target.CombatState;
            var dealer = player.Creature;
            if (state?.RunState != null && dealer != null)
            {
                damage = Hook.ModifyDamage(state.RunState, state, target, dealer, amount, props,
                                           cardSource, cardPlay, ModifyDamageHookType.All,
                                           CardPreviewMode.None, out _);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy finisher predict: ModifyDamage failed, use raw: " + e.Message);
            damage = amount;
        }

        return Math.Max(0m, damage - (decimal)Math.Max(0, target.Block));
    }

    private static Curve? _taper;
    private static Curve Taper => _taper ??= BuildTaper();

    // ── 攻击 ────────────────────────────────────────────────────────────────

    /// <summary>砸下一道光柱；返回时**正好是命中那一刻**，调用方在这一行之后结算伤害。</summary>
    public static async Task PlayAsync(Creature? target, int tier)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = RoomVfx.Front("SubaruBallroyBeam");
            if (tree == null || root == null || target == null)
                return;

            var view = ViewSize(tree);
            var s = view.X / DesignWidth;
            var tm = TimingOf(tier);
            var pal = BeamPalette.ForTier(tier);
            var rect = RectOf(target, view);
            var ground = new Vector2(rect.GetCenter().X, rect.End.Y - 8f * s);
            var top = new Vector2(ground.X, -30f * s);
            var rng = new Random(System.Environment.TickCount ^ target.GetHashCode());

            // 柱体：一个 quad；拿不到 shader 就退回三层线
            var beam = BeamQuad.Create(root, top, top.Lerp(ground, 0.08f), pal, s);
            Line2D? coreLine = null;
            Line2D? volLineA = null;
            Line2D? volLineB = null;
            if (beam == null)
            {
                coreLine = MakeLine(root, pal.CoreWidth * s, new Color(pal.Core, 0f), s);
                volLineA = MakeLine(root, pal.VolumeWidth * s, new Color(pal.Volume, 0f), s);
                volLineB = MakeLine(root, pal.VolumeWidth * 0.52f * s, new Color(pal.Volume, 0f), s);
            }

            var rays = new List<Line2D>();
            var rayCols = new List<Color>();
            for (var i = 0; i < pal.Rays; i++)
            {
                var k = pal.Rays <= 1 ? 0f : i / (float)(pal.Rays - 1);
                var col = pal.RayA.Lerp(pal.RayB, k);
                rays.Add(MakeLine(root, (2.2f + (float)rng.NextDouble() * 2.6f) * s, new Color(col, 0f), s));
                rayCols.Add(col);
            }

            var ring = MakeRing(root, ground, Mathf.Max(70f * s, rect.Size.X * 0.85f), s, new Color(pal.Ring, 0f));

            var head = SoftDot.Make(new Vector2(ground.X, 0f), 120f * s, new Color(1f, 1f, 1f, 0f), true, core: true);
            root.AddChild(head);

            // ① 预警：地面环收束 + 顶端聚光 + 柱体只露一小截
            await Anim.Ms(tree, tm.TeleMs, el =>
            {
                var k = Mathf.Clamp(el / tm.TeleMs, 0f, 1f);
                var e = Anim.OutCubic(k);

                ring.Scale = Vector2.One * Mathf.Lerp(1.75f, 1.0f, e);
                ring.DefaultColor = new Color(pal.Ring, 0.85f * k);

                SoftDot.SetDiameter(head, Mathf.Lerp(210f, 78f, e) * s);
                head.Modulate = new Color(1f, 1f, 1f, 0.75f * k);

                var tip = top.Lerp(ground, 0.08f * k);
                if (beam != null)
                {
                    beam.SetSpan(top, tip);
                    beam.SetFade(0.25f * k);
                }
                else if (coreLine != null)
                {
                    coreLine.Points = [top, tip];
                    coreLine.DefaultColor = new Color(pal.Core, 0.22f * k);
                }
            });

            // ② 落下：柱体从屏幕顶压到地面。音效在**起手**这一帧播：
            //    素材峰值在 ~0.10s，正好落在命中帧（TM.DropMs + 100 ≈ hit）
            Audio.BallroyBeamSfx.PlayTier(tier);

            await Anim.Ms(tree, tm.DropMs, el =>
            {
                var p = Anim.OutQuint(Mathf.Clamp(el / tm.DropMs, 0f, 1f));
                var end = top.Lerp(ground, 0.08f + 0.92f * p);

                if (beam != null)
                {
                    beam.SetSpan(top, end);
                    beam.SetFade(Mathf.Lerp(0.4f, 1f, p));
                }
                else if (coreLine != null && volLineA != null && volLineB != null)
                {
                    coreLine.Points = [top, end];
                    coreLine.DefaultColor = new Color(pal.Core, Mathf.Lerp(0.4f, 1f, p));
                    volLineA.Points = [top, top.Lerp(ground, p)];
                    volLineB.Points = [top, top.Lerp(ground, p)];
                    volLineA.DefaultColor = new Color(pal.Volume, pal.Volume.A * 0.55f * p);
                    volLineB.DefaultColor = new Color(pal.Volume, pal.Volume.A * p);
                }

                for (var i = 0; i < rays.Count; i++)
                {
                    var off = (i - (rays.Count - 1) * 0.5f) * 14f * s;
                    var spread = 1f + i % 3 * 0.35f;
                    rays[i].Points = [new Vector2(top.X + off * 0.4f, top.Y), new Vector2(ground.X + off * spread, ground.Y)];
                    rays[i].DefaultColor = new Color(rayCols[i], rayCols[i].A * 0.8f * p);
                }

                head.Modulate = new Color(1f, 1f, 1f, 0.75f * (1f - p));
            });

            // ③ 命中：抖动 + 白闪 + 地面环炸开 + 粒子（音效已在落下起手播过）
            HitFeel.AddTrauma(tm.Trauma);

            if (tm.Flash > 0f)
                SpawnFlash(root, view, tm.Flash);

            var ringTw = ring.CreateTween().SetParallel(true);
            ringTw.TweenProperty(ring, "scale", Vector2.One * 2.6f, 0.16);
            ringTw.TweenProperty(ring, "default_color:a", 0f, 0.16);

            BallroyParticles.Rise(root, ground, pal.VolumeWidth * s, pal.Core, 16 + pal.Rays * 2, s);
            if (tier >= 2)
                BallroyParticles.Burst(root, ground, pal.RayA, 9 + tier * 2, s);
            SpawnImpactFrames(tree, root, ground, pal.Core, rect.Size.X, s, additive: true, frames: 4, frameMs: 34f);
            SpawnGroundPool(root, ground, rect.Size.X, pal, s);

            GD.Print($"[Subaru] ballroy beam: tier={tier} at ({ground.X:F0},{ground.Y:F0}) " +
                     $"core={pal.CoreWidth:F0}px vol={pal.VolumeWidth:F0}px rays={pal.Rays} " +
                     $"shader={(beam != null ? "ok" : "fallback-lines")} hit={tm.TeleMs + tm.DropMs:F0}ms");

            _ = TailAsync(tree, root, beam, coreLine, volLineA, volLineB, rays, rayCols, ring, pal, tm, s, top, ground);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy beam failed: " + e.Message);
        }
    }

    /// <summary>命中之后：停留（8Hz 脉动）→ 收窄溶解 → 清理。纯视觉，不阻塞。</summary>
    private static async Task TailAsync(SceneTree tree, Node2D root, BeamQuad? beam,
        Line2D? coreLine, Line2D? volLineA, Line2D? volLineB, List<Line2D> rays, List<Color> rayCols,
        Line2D ring, BeamPalette pal, Timing tm, float s, Vector2 top, Vector2 ground)
    {
        try
        {
            var fadeMs = 260f;

            await Anim.Ms(tree, tm.HoldMs + fadeMs, el =>
            {
                var inHold = el <= tm.HoldMs;
                var pulse = inHold ? 1f + 0.06f * Mathf.Sin(el / 1000f * Mathf.Tau * 8f) : 1f;
                var fade = inHold ? 1f : Mathf.Max(0f, 1f - (el - tm.HoldMs) / fadeMs);

                if (beam != null)
                {
                    beam.SetFade(fade * (inHold ? pulse : 1f));
                    beam.SetDissolve(1f - fade);
                }

                if (coreLine != null && GodotObject.IsInstanceValid(coreLine))
                {
                    coreLine.Width = pal.CoreWidth * s * pulse * Mathf.Lerp(0.15f, 1f, fade);
                    coreLine.DefaultColor = new Color(pal.Core, pal.Core.A * fade);
                }

                if (volLineA != null && GodotObject.IsInstanceValid(volLineA))
                {
                    volLineA.Width = pal.VolumeWidth * s * Mathf.Lerp(0f, 1f, fade);
                    volLineA.DefaultColor = new Color(pal.Volume, pal.Volume.A * 0.55f * fade);
                }

                if (volLineB != null && GodotObject.IsInstanceValid(volLineB))
                {
                    volLineB.Width = pal.VolumeWidth * 0.52f * s * Mathf.Lerp(0f, 1f, fade);
                    volLineB.DefaultColor = new Color(pal.Volume, pal.Volume.A * fade);
                }

                for (var i = 0; i < rays.Count; i++)
                {
                    if (GodotObject.IsInstanceValid(rays[i]))
                        rays[i].DefaultColor = new Color(rayCols[i], rayCols[i].A * 0.8f * fade);
                }

                _ = ring;
            });

            // 余丝：主柱散尽后仍有一根细线挂着（参考 ParticleFX 的尾帧）
            try
            {
                var thread = MakeLine(root, 4f * s, new Color(pal.Core, 0f), s);
                thread.Points = [top, ground];

                await Anim.Ms(tree, 320f, el =>
                {
                    var k = Mathf.Clamp(el / 320f, 0f, 1f);
                    var a = k < 0.25f ? k / 0.25f * 0.45f : 0.45f * (1f - (k - 0.25f) / 0.75f);
                    thread.DefaultColor = new Color(pal.Core, Mathf.Max(0f, a));
                    thread.Width = 4f * s * Mathf.Lerp(1f, 0.5f, k);
                });
            }
            catch
            {
                // 余丝只是加分项
            }

            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
        catch
        {
            // 尾段失败不影响战斗
        }
    }

    // ── 终结：终结一击的攻击动画（蓄力 → 白屏 → 停抖 → 黑柱砸成剪影 → 再抖）──

    /// <summary>上一条终结动画的白屏收尾；死亡钩子只等它，不再播任何东西。</summary>
    private static Task? _pendingTail;

    /// <summary>还在播的终结收尾（没有就 null）。</summary>
    public static Task? PendingTail => _pendingTail is { IsCompleted: false } running ? running : null;

    /// <summary>
    /// 终结一击的攻击动画：**替换**这一击原本的光柱（2026-11-22 用户定）。
    ///
    /// <para>返回时正好是「黑柱砸到地面」那一帧 —— 调用方在这之后结算伤害；
    /// 之后的定格 / 白屏退场由 <see cref="RunFinisherTailAsync"/> 自己跑完。
    /// 所以整场只有这一套演出：不会再出现「先播攻击光柱、死了再播终结」。</para>
    /// </summary>
    public static async Task PlayFinisherAttackAsync(Creature? creature)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null || creature == null)
                return;

            var root = RoomVfx.Front("SubaruBallroyFinisher");
            if (root == null)
                return;

            GD.Print($"[Subaru] ballroy finisher: attack anim start ({creature.Name})");
            await RunFinisherAsync(tree, creature, root);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy finisher attack failed: " + e.Message);
        }
    }

    private static async Task RunFinisherAsync(SceneTree tree, Creature creature, Node2D root)
    {
        var view = ViewSize(tree);
        var s = view.X / DesignWidth;
        var rect = RectOf(creature, view);
        var ground = new Vector2(rect.GetCenter().X, rect.End.Y - 8f * s);
        var top = new Vector2(ground.X, -30f * s);
        var cnode = creature.GetCreatureNode();
        var visuals = cnode?.Visuals as Node2D;
        var snap = visuals != null ? LiveVisuals.Snapshot(visuals) : null;
        var pal = BeamPalette.BallroyTier3();

        // 拍子按「剪好的 终结.mp3」来：蓄力 0~1.15s、砸落 1.30s（素材峰值）
        const float chargeMs = 1150f;
        const float whiteMs = 100f;
        const float stillMs = 50f;
        const float slamMs = 110f;

        try
        {
            // ① 蓄力：压暗 + 预警环收缩 + 顶端聚光；音效先起，抖动一级一级加
            var veil = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(0.03f, 0.04f, 0.06f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(veil);
            veil.CreateTween().TweenProperty(veil, "color:a", 0.58f, 0.12);

            Audio.BallroyBeamSfx.PlayCharge();

            var ring = MakeRing(root, ground, Mathf.Max(80f * s, rect.Size.X * 0.9f), s, new Color(pal.Ring, 0f));
            var head = SoftDot.Make(new Vector2(ground.X, 0f), 90f * s, new Color(1f, 1f, 1f, 0f), true, core: true);
            root.AddChild(head);

            await Anim.Ms(tree, chargeMs, el =>
            {
                var k = Mathf.Clamp(el / chargeMs, 0f, 1f);
                ring.Scale = Vector2.One * Mathf.Lerp(1.7f, 0.92f, Anim.OutCubic(k));
                ring.DefaultColor = new Color(pal.Ring, 0.85f * k);
                SoftDot.SetDiameter(head, Mathf.Lerp(60f, 26f, k) * s);
                head.Modulate = new Color(1f, 1f, 1f, 0.85f * k);

                // 蓄力抖动：托底 → 0.10 一路升到 0.52（打脉冲会被 2.2/s 的衰减吃掉，见 HitFeel.HoldTrauma）
                HitFeel.HoldTrauma(0.10f + 0.42f * k);
            });

            // ② 变白（在砸落前的静默里完成）→ 停抖
            var white = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(FlashCol, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(white);
            white.CreateTween().TweenProperty(white, "color:a", 0.90f, whiteMs / 1000f);

            await Anim.Ms(tree, whiteMs + stillMs, _ => { });
            GD.Print("[Subaru] ballroy finisher: charge -> white-out (shake stopped)");

            // ③ 光柱砸进白屏：黑柱压下来 + 抓剪影 + 重抖（音效的砸落就在这一帧）
            var sil = visuals != null
                ? await SilhouetteCapture.CaptureAsync(tree, visuals, rect, root, InkCol, InkCol)
                : null;

            Sprite2D? silSprite = null;
            if (sil?.Tex != null)
            {
                silSprite = new Sprite2D
                {
                    Texture = sil.Tex,
                    Centered = false,
                    Position = sil.DrawOrigin,
                };
                root.AddChild(silSprite);
            }

            if (visuals != null)
                visuals.Visible = false;

            var pillar = MakeLine(root, pal.VolumeWidth * 0.62f * s, new Color(InkCol, 0f), s);
            var pillarSoft = MakeLine(root, pal.VolumeWidth * 0.95f * s, new Color(InkCol, 0f), s);
            foreach (var line in new[] { pillar, pillarSoft })
            {
                line.Material = null;   // 剪影不是光，取消叠加混合
                line.Points = [top, top];
            }

            await Anim.Ms(tree, slamMs, el =>
            {
                var p = Anim.OutQuint(Mathf.Clamp(el / slamMs, 0f, 1f));
                var end = top.Lerp(ground, p);
                pillar.Points = [top, end];
                pillarSoft.Points = [top, end];
                pillar.DefaultColor = new Color(InkCol, 0.94f * p);
                pillarSoft.DefaultColor = new Color(InkCol, 0.30f * p);
            });

            HitFeel.AddTrauma(0.85f);
            BallroyParticles.Burst(root, ground, InkCol, 26, s);
            SpawnImpactFrames(tree, root, ground, InkCol, rect.Size.X, s, additive: false, frames: 4, frameMs: 40f);

            GD.Print($"[Subaru] ballroy finisher: slam -> silhouette={(sil?.Tex != null ? "ok" : "FALLBACK")} "
                     + "pillar=black (damage resolves now)");

            // 命中帧到了：交回调用方结算伤害；后面的定格 / 白屏退场自己跑（死亡钩子只等它）
            _pendingTail = RunFinisherTailAsync(tree, root, creature, visuals, snap, silSprite,
                                                pillar, pillarSoft, white, ground, pal, s);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy finisher failed: " + e.Message);

            // 别把怪留在「涂黑 + 隐藏」的状态里
            if (visuals != null && snap != null && LiveVisuals.RestoreIfAlive(creature, snap))
                GD.Print("[Subaru] ballroy finisher: restored live visuals (aborted)");

            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
    }

    /// <summary>砸落之后：余震 → 负片定格 → 白屏退去 → 清理。纯视觉，不阻塞出牌。</summary>
    private static async Task RunFinisherTailAsync(SceneTree tree, Node2D root, Creature creature,
        Node2D? visuals, List<LiveVisuals.State>? snap, Sprite2D? silSprite,
        Line2D pillar, Line2D pillarSoft, ColorRect white, Vector2 ground, BeamPalette pal, float s)
    {
        const float freezeMs = 520f;
        const float fadeMs = 220f;

        try
        {
            // ① 两记余震
            await Anim.Ms(tree, 220f, el =>
            {
                if (el > 70f && el < 82f) HitFeel.AddTrauma(0.40f);
                if (el > 150f && el < 162f) HitFeel.AddTrauma(0.24f);
            });

            // ② 万一没死（预测偏乐观 / 转阶段复活）：把本体还回来，白屏快速退掉，别留一只隐形怪
            if (visuals != null && snap != null && LiveVisuals.RestoreIfAlive(creature, snap))
            {
                GD.Print("[Subaru] ballroy finisher: restored live visuals (survived, prediction missed)");
                if (silSprite != null && GodotObject.IsInstanceValid(silSprite))
                    silSprite.QueueFree();

                await Anim.Ms(tree, 160f, el =>
                {
                    var a = 1f - Mathf.Clamp(el / 160f, 0f, 1f);
                    white.Color = new Color(FlashCol, 0.90f * a);
                    pillar.DefaultColor = new Color(InkCol, 0.94f * a);
                    pillarSoft.DefaultColor = new Color(InkCol, 0.30f * a);
                });

                if (GodotObject.IsInstanceValid(root))
                    root.QueueFree();

                return;
            }

            // ③ 负片定格
            await Anim.Ms(tree, freezeMs, _ => { });

            // ④ 白屏退去 + 剪影 / 黑柱化尘
            await Anim.Ms(tree, fadeMs, el =>
            {
                var a = 1f - Mathf.Clamp(el / fadeMs, 0f, 1f);
                white.Color = new Color(FlashCol, 0.90f * a);
                pillar.DefaultColor = new Color(InkCol, 0.94f * a);
                pillarSoft.DefaultColor = new Color(InkCol, 0.30f * a);

                if (silSprite != null && GodotObject.IsInstanceValid(silSprite))
                    silSprite.Modulate = new Color(1f, 1f, 1f, a);
            });

            BallroyParticles.Rise(root, ground, pal.VolumeWidth * 0.6f * s, pal.Core, 40, s);

            await Anim.Ms(tree, 260f, _ => { });

            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy finisher tail failed: " + e.Message);
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
        }
        finally
        {
            _pendingTail = null;
        }
    }

    // ── 零件 ────────────────────────────────────────────────────────────────

    /// <summary>地面亮光池：命中后“踩”在地上的一滩光，60ms 亮起、460ms 退干净。</summary>
    private static void SpawnGroundPool(Node2D root, Vector2 ground, float targetWidth, BeamPalette pal, float s)
    {
        try
        {
            var tex = VfxTextures.BeamPool;
            if (tex == null)
                return;

            var size = Mathf.Clamp(targetWidth * 0.90f, 200f * s, 780f * s);
            var sprite = new Sprite2D
            {
                Texture = tex,
                Position = ground,
                Centered = true,
                Scale = Vector2.One * (size / tex.GetWidth()),
                Modulate = new Color(pal.Ring, 0f),
                Material = SoftDot.Additive,
            };
            root.AddChild(sprite);

            var tw = sprite.CreateTween().SetParallel(true);
            tw.TweenProperty(sprite, "modulate:a", 0.26f, 0.06);
            tw.TweenProperty(sprite, "scale", sprite.Scale * 1.12f, 0.06);
            tw.Chain().TweenProperty(sprite, "modulate:a", 0f, 0.38);

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return;

            var timer = tree.CreateTimer(0.75f);
            tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout)
                .OnCompleted(() =>
                {
                    if (GodotObject.IsInstanceValid(sprite))
                        sprite.QueueFree();
                });
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] ground pool failed: " + e.Message);
        }
    }

    /// <summary>命中冲击帧：4 张 `impact_*.png` 依次换贴图，同时放大、淡出（约 140ms）。</summary>
    private static void SpawnImpactFrames(SceneTree tree, Node2D root, Vector2 ground, Color tint,
        float targetWidth, float s, bool additive, int frames, float frameMs)
    {
        try
        {
            var tex = VfxTextures.Impact(1);
            if (tex == null)
                return;

            var size = Mathf.Clamp(targetWidth * 0.85f, 160f * s, 760f * s);
            var sprite = new Sprite2D
            {
                Texture = tex,
                Position = ground,
                Centered = true,
                Scale = Vector2.One * (size / tex.GetWidth()),
                Modulate = new Color(tint, 0f),
            };
            if (additive)
                sprite.Material = SoftDot.Additive;
            else
                sprite.Material = null;

            root.AddChild(sprite);

            _ = Anim.Ms(tree, frames * frameMs + 90f, el =>
            {
                if (!GodotObject.IsInstanceValid(sprite))
                    return;

                var idx = Mathf.Clamp((int)(el / frameMs) + 1, 1, frames);
                var t = VfxTextures.Impact(idx);
                if (t != null && sprite.Texture != t)
                    sprite.Texture = t;

                var k = Mathf.Clamp(el / (frames * frameMs), 0f, 1f);
                var grow = Mathf.Lerp(1f, 1.55f, Anim.OutCubic(k));
                var alpha = Mathf.Max(0f, 1f - k) * 0.72f;

                sprite.Scale = Vector2.One * (size / 512f * grow);
                sprite.Modulate = new Color(tint, alpha);
            });

            // 生命周期结束后自己清掉（不依赖调用方）
            var timer = tree.CreateTimer((frames * frameMs + 200f) / 1000f);
            tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout)
                .OnCompleted(() =>
                {
                    if (GodotObject.IsInstanceValid(sprite))
                        sprite.QueueFree();
                });
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] impact frames failed: " + e.Message);
        }
    }

    private static Line2D MakeLine(Node2D root, float width, Color color, float s)
    {
        var node = new Line2D
        {
            Points = [Vector2.Zero, Vector2.Zero],
            Width = width,
            WidthCurve = Taper,
            DefaultColor = color,
            Antialiased = true,
            JointMode = Line2D.LineJointMode.Round,
            Material = SoftDot.Additive,
        };
        root.AddChild(node);
        return node;
    }

    private static Line2D MakeRing(Node2D root, Vector2 pos, float radiusX, float s, Color color)
    {
        const int segments = 36;
        var pts = new List<Vector2>();
        for (var i = 0; i <= segments; i++)
        {
            var a = i * Mathf.Tau / segments;
            pts.Add(new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusX * 0.34f));
        }

        var node = new Line2D
        {
            Points = pts.ToArray(),
            Width = 4f * s,
            DefaultColor = color,
            Antialiased = true,
            Position = pos,
            Material = SoftDot.Additive,
        };
        root.AddChild(node);
        return node;
    }

    private static void SpawnFlash(Node2D root, Vector2 view, float peak)
    {
        var flash = new ColorRect
        {
            Position = Vector2.Zero,
            Size = view,
            Color = new Color(FlashCol, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(flash);

        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "color:a", peak, 0.04);
        tw.TweenProperty(flash, "color:a", 0f, 0.16);
        tw.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(flash))
                flash.QueueFree();
        }));
    }

    private static Curve BuildTaper()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.12f));
        curve.AddPoint(new Vector2(0.28f, 1f));
        curve.AddPoint(new Vector2(0.72f, 1f));
        curve.AddPoint(new Vector2(1f, 0.12f));
        return curve;
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
            // 退到默认
        }

        return new Vector2(1920f, 1080f);
    }

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
            // 退到默认
        }

        return new Rect2(new Vector2(view.X * 0.70f, view.Y * 0.35f), new Vector2(view.X * 0.12f, view.Y * 0.30f));
    }
}
