using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 贤者·夏乌拉的专属打击演出（2026-11-21 用户定）。
///
/// <para>流程：橘色接管整个战斗场景（敌人贴图不变）→ 屏幕左侧上 1/4 位置打出一根白色射线 →
/// 命中目标 → 射线爆开、**结算伤害** → 收场。立绘 cut-in 是可选前置（由调用方决定播不播）。</para>
///
/// <para>终结分支（这一击清空场上所有非爪牙，和通用击杀特效同一个判据）：
/// 结算瞬间底色转**灰白**（#e6e4de，护眼）、射线变**黑**、敌人变**黑剪影**，然后照常爆开。
/// 非终结击杀沿用通用击杀特效（紫白），只是不播音效。</para>
///
/// <para>层级（2026-11-21 用户要求全部在房间 UI 之下）：
/// 橘/灰白底 → <see cref="RoomVfx.Back"/>（背景之上、角色之下，所以敌人贴图不受影响）；
/// 射线 + 爆开 → <see cref="RoomVfx.Front"/>（角色之上、UI 之下）。</para>
/// </summary>
public static class ShaulaStrike
{
    /// <summary>当前正在播的那一次打击（<see cref="ShaulaFinisher"/> 靠它接管底色时长）。</summary>
    private static Strike? Current;

    // ---- 配色（用户 2026-11-21 给定）----
    private static readonly Color Orange = new(0.878f, 0.471f, 0.149f);      // #e07826
    private static readonly Color GrayWhite = new(0.902f, 0.894f, 0.871f);   // #e6e4de

    // ---- 射线参数 ----
    private const float RayCoreWidth = 5f;    // 细白线（狙击感）
    private const float RayGlowWidth = 16f;
    private const float RayGlowAlpha = 0.22f;
    private const float RayStartYFraction = 0.25f;   // 屏幕左侧、上 1/4
    private const float RayOffscreen = 40f;          // 起点在屏幕外多少（设计像素）

    // ---- 时间轴（ms）----
    private const float InMs = 60f;       // 橘色淡入
    private const float RayMs = 80f;      // 射线伸展到命中
    private const float BurstMs = 100f;   // 爆开
    private const float RetractMs = 90f;  // 尾线回收
    private const float OutMs = 100f;     // 收场淡出
    private const float KillHoldMs = 900f;      // 普通击杀：底色留 900ms 给通用击杀特效
    private const float NormalHoldMs = 100f;    // 普通攻击：爆开之后再补 100ms（2026-11-21 用户：底色太短）
    private const float FinisherFallbackMs = 1100f; // 终结没接管时的兜底停留（正常由 ShaulaFinisher 续期）
    private const float MaxLifeMs = 8000f;      // 硬上限，防止底色卡在屏幕上

    /// <summary>一次打击的「计划」：伤害还没结算前就能算出来的信息。</summary>
    public sealed class Plan
    {
        public Player? Player;
        public Creature? Target;
        public decimal TotalDamage;
        public bool WillKill;
        public bool Finisher;
        public Vector2 From;
        public Vector2 To;
    }

    /// <summary>一次打击的运行时句柄。</summary>
    public sealed class Strike
    {
        internal Plan Plan = null!;
        internal double StartAt;
        internal readonly TaskCompletionSource<bool> ImpactTcs = new();
        internal Node2D? Root;         // 底色根（Back 容器里）
        internal Node2D? Front;        // 射线/爆开根（Front 容器里）
        internal ColorRect? Back;      // 橘 / 灰白底
        internal ColorRect? Flash;     // 爆开白闪
        internal Line2D? RayCore;
        internal Line2D? RayGlow;
        internal Line2D? Ring;         // 冲击环
        internal Vector2 From;
        internal Vector2 To;
        internal float S = 1f;         // 设计像素 → 实际像素
        internal bool Freed;
        internal bool Killed;
        internal bool Impacted;
        internal bool Finisher;
        internal bool ForceFinisherBackdrop;  // 终结演出要求底色转灰白
        internal float BackFadeStart = -1f;   // 底色开始淡出的时刻（ms 相对 StartAt，-1 = 还没定）

        /// <summary>射线打到目标那一刻（调用方在这里结算伤害）。</summary>
        public Task Impact => ImpactTcs.Task;

        /// <summary>
        /// 伤害结算完调用。**终结与否以结算后的场上状态为准**（结算前的估算会漏掉力量/易伤加伤，
        /// 2026-11-21 实测「终结了却还是紫白配色」就是估算偏保守造成的）。
        /// </summary>
        public void NotifyDamageDealt(bool killed, bool finisher)
        {
            Killed = killed;
            Finisher = killed && finisher;
            GD.Print($"[Subaru] shaula strike: killed={killed} finisher={Finisher}");
        }
    }

    /// <summary>
    /// 算一遍「会不会打死 / 是不是终结」。**不产生任何节点**，所以可以在立绘出场前调用。
    /// </summary>
    public static Plan Prepare(Player? player, Creature? target, decimal totalDamage)
    {
        var plan = new Plan
        {
            Player = player,
            Target = target,
            TotalDamage = totalDamage,
        };

        try
        {
            var state = player?.Creature?.CombatState;
            var view = ViewSize();

            plan.From = new Vector2(-RayOffscreen * (view.X / 1280f), view.Y * RayStartYFraction);
            plan.To = ImpactPoint(target, view);

            if (target == null || state == null)
                return plan;

            plan.WillKill = target.CurrentHp <= totalDamage;

            if (!plan.WillKill)
                return plan;

            // 终结判据和通用击杀特效一致：这一击之后场上不再有活着的「非爪牙」敌人
            var left = state.HittableEnemies;
            var othersAlive = left == null
                ? false
                : left.Any(e => e != null
                                && !ReferenceEquals(e, target)
                                && !e.IsSecondaryEnemy
                                && !e.IsDead
                                && e.CurrentHp > 0);

            plan.Finisher = !target.IsSecondaryEnemy && !othersAlive && !FinisherExempt.Contains(target);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula strike prepare failed: " + e.Message);
        }

        return plan;
    }

    /// <summary>
    /// 结算之后再判一次「是不是终结」：场上不再有活着的非爪牙敌人。
    /// （和 <see cref="KillEffect"/> 的 full tier 判据一致；目标此刻已经死了，所以直接数活的。）
    /// </summary>
    public static bool IsFinisherNow(Player? player, Creature? target)
    {
        try
        {
            // 死后还会生成敌人的怪（地精佣兵 / 异蛙寄生虫）：一律不算终结，
            // 否则那一刻场上的活怪被判成爪牙 → 误判成终结（2026-11-21 用户定）。
            if (FinisherExempt.Contains(target))
                return false;

            var left = player?.Creature?.CombatState?.HittableEnemies;
            if (left == null)
                return target != null && !target.IsSecondaryEnemy;

            return !left.Any(e => e != null && !e.IsDead && e.CurrentHp > 0 && !e.IsSecondaryEnemy);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 命中**之前**判「这一击打下去是不是就清场了」。
    ///
    /// <para><b>和 <see cref="IsFinisherNow"/> 的唯一区别</b>：这里要排除目标自己 ——
    /// 出牌那一刻目标还活着，会出现在 <c>HittableEnemies</c> 里。
    /// 2026-11-22 根因：<c>BallroyBeam.WillBeFinisherKill</c> 拿死后的判据（<see cref="IsFinisherNow"/>）
    /// 做命中前的判断，目标自己永远算“还有一个活的” → 恒为 false →
    /// 「先播二档光柱、死了再播终结」那个重复演出的直接原因。</para>
    /// </summary>
    public static bool WillBeLastKill(Player? player, Creature? target)
    {
        try
        {
            if (target == null || target.IsSecondaryEnemy || FinisherExempt.Contains(target))
                return false;

            var left = player?.Creature?.CombatState?.HittableEnemies;
            if (left == null)
                return true;

            return !left.Any(e => e != null
                                  && !ReferenceEquals(e, target)
                                  && !e.IsDead
                                  && e.CurrentHp > 0
                                  && !e.IsSecondaryEnemy);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按计划启动演出（橘色 + 射线）。返回 null 表示环境不具备（不影响出牌）。</summary>
    public static Strike? Start(Plan plan)
    {
        try
        {
            var view = ViewSize();
            var s = view.X / 1280f;

            var strike = new Strike
            {
                Plan = plan,
                StartAt = Time.GetTicksMsec(),
                From = plan.From,
                To = plan.To,
                S = s,
            };

            // 底色：背景之上、角色之下 → 敌人贴图不变
            var backRoot = RoomVfx.Back("SubaruShaulaStrikeBack");
            if (backRoot != null)
            {
                var rect = new ColorRect
                {
                    // 按 2 倍铺、以屏幕中心对齐（2026-11-21 用户：出现过盖不满屏的情况，保险起见翻倍）
                    Position = -view * 0.5f,
                    Size = view * 2f,
                    Color = new Color(Orange, 0f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                backRoot.AddChild(rect);
                strike.Root = backRoot;
                strike.Back = rect;
            }

            // 射线 + 爆开：角色之上、UI 之下
            var frontRoot = RoomVfx.Front("SubaruShaulaStrikeFront");
            if (frontRoot != null)
            {
                var glow = new Line2D
                {
                    Points = [strike.From, strike.From],
                    Width = RayGlowWidth * s,
                    DefaultColor = new Color(Colors.White, RayGlowAlpha),
                    Antialiased = true,
                    JointMode = Line2D.LineJointMode.Round,
                };
                frontRoot.AddChild(glow);

                var core = new Line2D
                {
                    Points = [strike.From, strike.From],
                    Width = RayCoreWidth * s,
                    DefaultColor = Colors.White,
                    Antialiased = true,
                    JointMode = Line2D.LineJointMode.Round,
                };
                frontRoot.AddChild(core);

                var flash = new ColorRect
                {
                    Position = -view * 0.5f,
                    Size = view * 2f,
                    Color = new Color(Colors.White, 0f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                frontRoot.AddChild(flash);

                var ring = new Line2D
                {
                    Points = Circle(strike.To, 8f * s, 32),
                    Width = 4f * s,
                    DefaultColor = new Color(Colors.White, 0f),
                    Antialiased = true,
                    JointMode = Line2D.LineJointMode.Round,
                };
                frontRoot.AddChild(ring);

                strike.Front = frontRoot;
                strike.RayGlow = glow;
                strike.RayCore = core;
                strike.Flash = flash;
                strike.Ring = ring;
            }

            if (strike.Back == null && strike.RayCore == null)
            {
                Cleanup(strike);
                return null;
            }

            Current = strike;
            _ = DriveAsync(strike);

            GD.Print($"[Subaru] shaula strike start (finisher={plan.Finisher} willKill={plan.WillKill} " +
                     $"from=({strike.From.X:F0},{strike.From.Y:F0}) to=({strike.To.X:F0},{strike.To.Y:F0}))");
            return strike;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula strike start failed: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 终结演出接管底色（<see cref="ShaulaFinisher"/> 调用）：底色强制转灰白，并且一直留到演出播完，
    /// 最后一帧才收干净（<see cref="OutMs"/> 在 ms 之内淡出）。
    /// 没接管的时候按 <see cref="FinisherFallbackMs"/> 兜底，不会把底色永久留在屏幕上。
    /// </summary>
    public static void KeepBackdropForFinisher(float ms)
    {
        try
        {
            var strike = Current;
            if (strike == null || strike.Freed || !GodotObject.IsInstanceValid(strike.Back))
            {
                GD.Print("[Subaru] shaula strike: finisher backdrop request ignored (no active strike)");
                return;
            }

            strike.ForceFinisherBackdrop = true;

            var now = (float)(Time.GetTicksMsec() - strike.StartAt);
            strike.BackFadeStart = now + Mathf.Max(0f, ms - OutMs);

            GD.Print($"[Subaru] shaula strike: backdrop kept for finisher " +
                     $"(now={now:F0}ms fadeAt={strike.BackFadeStart:F0}ms out={OutMs:F0}ms)");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula strike: backdrop request failed: " + e.Message);
        }
    }

    /// <summary>收场：两个根节点一起删（子节点跟着走）。</summary>
    private static void Cleanup(Strike strike)
    {
        try
        {
            if (strike.Root != null && GodotObject.IsInstanceValid(strike.Root))
                strike.Root.QueueFree();

            if (strike.Front != null && GodotObject.IsInstanceValid(strike.Front))
                strike.Front.QueueFree();
        }
        catch
        {
            // 收尾失败无所谓
        }

        if (ReferenceEquals(Current, strike))
            Current = null;

        strike.Root = null;
        strike.Front = null;
    }

    private static async Task DriveAsync(Strike strike)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return;

        try
        {
            while (!strike.Freed)
            {
                if (!GodotObject.IsInstanceValid(strike.Root)
                    && !GodotObject.IsInstanceValid(strike.RayCore))
                {
                    strike.Freed = true;
                    break;
                }

                var now = (float)(Time.GetTicksMsec() - strike.StartAt);
                Update(strike, now);

                // 存活时长跟着「底色淡出时刻」走：终结演出会把 BackFadeStart 往后推
                var impactAt = InMs + RayMs;
                var finisherNow = strike.Plan.Finisher || strike.ForceFinisherBackdrop
                                  || (strike.Killed && strike.Finisher);

                var fadeStart = strike.BackFadeStart;
                if (fadeStart < 0f)
                {
                    fadeStart = impactAt + BurstMs
                                + (finisherNow
                                    ? FinisherFallbackMs
                                    : strike.Killed ? KillHoldMs : NormalHoldMs);
                }

                var total = Mathf.Min(MaxLifeMs,
                    Mathf.Max(fadeStart + OutMs, impactAt + BurstMs + RetractMs + OutMs));
                if (now >= total)
                {
                    strike.Freed = true;
                    Cleanup(strike);
                    GD.Print("[Subaru] shaula strike freed");
                    break;
                }

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] shaula strike drive failed: " + e.Message);
            strike.Freed = true;
            strike.ImpactTcs.TrySetResult(false);   // 宁可没演出，也不能把出牌卡死
            Cleanup(strike);
        }
    }

    private static void Update(Strike strike, float now)
    {
        var impactAt = InMs + RayMs;

        // 结算前的估算先决定观感（灰白底/黑射线），结算后的真相再兜一次：
        // 结算在整个演出结束之后才回来（DamageCmd 会 await 死亡动画），等不及。
        var finisher = strike.Plan.Finisher || strike.ForceFinisherBackdrop
                       || (strike.Killed && strike.Finisher);

        // 命中帧：音效 + 放行伤害结算
        if (!strike.Impacted && now >= impactAt)
        {
            strike.Impacted = true;
            Audio.ShaulaHitSfx.Play();
            strike.ImpactTcs.TrySetResult(true);
        }

        // ① 底色：橘色淡入；终结时在命中瞬间换成灰白
        if (strike.Back != null)
        {
            var a = Mathf.Clamp(now / InMs, 0f, 1f);
            var col = finisher && now >= impactAt ? GrayWhite : Orange;

            // 收场起点（绝对时刻）：
            //  · 普通攻击 —— 爆开后再补 NormalHoldMs（2026-11-21 用户：原来太短）
            //  · 普通击杀 —— 留 KillHoldMs 给通用击杀特效
            //  · 终结 —— 由 ShaulaFinisher 调 KeepBackdropForFinisher 把时刻推后；没接管就按兜底
            var fadeStart = strike.BackFadeStart;
            if (fadeStart < 0f)
            {
                fadeStart = impactAt + BurstMs
                            + (finisher
                                ? FinisherFallbackMs
                                : strike.Killed ? KillHoldMs : NormalHoldMs);
            }

            var fade = Mathf.Clamp(1f - (now - fadeStart) / OutMs, 0f, 1f);

            strike.Back.Color = new Color(col, a * fade);
        }

        // ② 射线：从起点伸到命中点；终结时变黑；命中后**尾线回收**（尾巴往弹着点收，发光层滞后当拖尾）
        if (strike.RayCore != null && strike.RayGlow != null)
        {
            var extend = Mathf.Clamp(now / impactAt, 0f, 1f);
            var tip = strike.From.Lerp(strike.To, extend);

            var rayColor = finisher ? Colors.Black : Colors.White;

            // 回收：爆开一小段之后开始，尾巴（From）向弹着点追过去
            var retractStart = impactAt + BurstMs * 0.35f;
            var rt = Mathf.Clamp((now - retractStart) / RetractMs, 0f, 1f);

            var coreTail = strike.From.Lerp(strike.To, rt);
            var glowTail = strike.From.Lerp(strike.To, rt * 0.72f);   // 发光层落后 → 拖尾
            var coreA = 1f - rt;
            var glowA = (1f - rt * rt) * 0.75f;

            strike.RayCore.Points = [coreTail, tip];
            strike.RayGlow.Points = [glowTail, tip];
            strike.RayCore.DefaultColor = new Color(rayColor, coreA);
            strike.RayGlow.DefaultColor = new Color(rayColor, RayGlowAlpha * glowA);
        }

        // ③ 爆开 A：白闪 + 冲击环（在命中点）
        if (strike.Flash != null && strike.Ring != null)
        {
            var t = Mathf.Clamp((now - impactAt) / BurstMs, 0f, 1f);
            if (now >= impactAt && t < 1f)
            {
                var f = 1f - t;
                strike.Flash.Color = new Color(Colors.White, 0.35f * f * f);

                var radius = Mathf.Lerp(10f, 190f, 1f - f) * strike.S;
                strike.Ring.Points = Circle(strike.To, radius, 32);
                strike.Ring.DefaultColor = new Color(finisher ? Colors.Black : Colors.White, 0.9f * f);
            }
            else
            {
                strike.Flash.Color = new Color(Colors.White, 0f);
                strike.Ring.DefaultColor = new Color(Colors.White, 0f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────

    private static Vector2[] Circle(Vector2 center, float radius, int segments)
    {
        var pts = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return pts;
    }

    /// <summary>目标身上的命中点：优先用游戏给的可视矩形中心，退化成节点位置。</summary>
    private static Vector2 ImpactPoint(Creature? target, Vector2 view)
    {
        try
        {
            var node = target?.GetCreatureNode();
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                var visuals = node.Visuals as Node2D;
                if (visuals != null && GodotObject.IsInstanceValid(visuals))
                {
                    var rect = KillEffect.VisualRect(node, visuals, out var source);
                    if (source != "none" && rect.Size.X > 4f && rect.Size.Y > 4f)
                        return rect.GetCenter();
                }

                return node.GetGlobalTransformWithCanvas().Origin + new Vector2(0f, -view.Y * 0.12f);
            }
        }
        catch
        {
            // 退到屏幕中间偏右
        }

        return new Vector2(view.X * 0.72f, view.Y * 0.55f);
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
