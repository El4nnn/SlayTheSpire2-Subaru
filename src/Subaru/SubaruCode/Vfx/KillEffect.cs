using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 击杀特效（impact frame 式）：白闪 → 角色/敌人紫色剪影定格 → 敌人身上裂开 → 碎片爆散 → 恢复。
///
/// <para><b>逻辑不暂停</b>：敌人真正的移除照常发生在引擎里，只是被白闪盖住了。</para>
///
/// <para><b>让奖励界面等我们</b>：<see cref="NotifyKill"/> 会返回一个 Task（只在这批的第一只返回非 null），
/// 补丁把它接在 <c>Hook.AfterDeath</c> 的返回值后面 —— 引擎 await 这个钩子，于是
/// 「这一只的死亡处理」和「战斗结束 → 奖励界面」都会等特效播完。
/// 多人 / 设置关闭 / 已经在播 时返回 null，完全不阻塞。</para>
///
/// <para><b>为什么所有东西都在 <see cref="NotifyKill"/> 里抓快照</b>（2026-11-20 的教训）：
/// 这个函数是 <c>Hook.AfterDeath</c> 的 postfix，此刻生物正在离开战斗。
/// 到两帧后再读 <c>creature.CombatState</c> 永远是 null、读节点可能已被释放。
/// 所以「战斗状态 / 队伍人数 / 房间类型 / 剪影副本 / 屏幕位置 / 骨架姿势」全部在这一刻抓。</para>
///
/// <para><b>剪影来源</b>：昴用**固定贴图**（<see cref="SilhouettePath"/>，cast 抬手帧烤的白色 alpha 图），
/// 位置借玩家 spine 节点的全局变换；敌人仍然复制 `NCreature.Visuals` 子树（`Duplicate()`）并回放死亡动画。
/// 2026-11-20 之前昴也是复制活体，但 spine 的 track 播完会回落 setup 姿势（= 待机帧），
/// 玩家看到「最后一帧闪回待机」，所以改成固定图。</para>
///
/// <para><b>紫色平涂</b>：spine-godot 的绘制走它自己的材质通道，只设 <c>CanvasItem.Material</c>
/// 会被忽略，所以对每个节点同时设 <c>Material</c> 并调 <c>set_normal_material</c>（存在才调）。</para>
///
/// <para><b>慢放</b>：直接压 <c>Engine.TimeScale</c>（0.18，约 0.4s 真实时间），所以特效自己的计时
/// 全部按<b>墙钟</b>（<see cref="Time.GetTicksMsec"/>）走，不能用 <c>SceneTreeTimer</c>。</para>
/// </summary>
public static class KillEffect
{
    // 以前的 CanvasLayer 层级（130/131/132，会被 UI 盖住的那套）已废弃：
    // 现在统一挂到房间的 CombatVfxContainer（角色之上、UI 之下），画序靠节点顺序。

    // ---- 满档时间轴（毫秒，墙钟）----
    private const int FullFlashMs = 50;
    private const int FullNegativeMs = 33;
    private const int FullHoldMs = 250;
    private const int FullChargeMs = 150;

    /// <summary>裂缝从怪物蔓延到全屏。</summary>
    private const int FullCrackMs = 450;   // 2026-11-20 用户定：散开提前 0.3s（原 750）

    private const int FullOverMs = 60;

    /// <summary>碎裂前把白闪压下去、等一帧让截屏拿到「画面 + 剪影」。</summary>
    private const int FullShatterPrepMs = 140;

    /// <summary>全屏碎块从中心扩散消失。</summary>
    private const int FullShatterMs = 450;    // 2026-11-20 用户定：再砍一半（1800 → 900 → 450）

    /// <summary>
    /// 碎块**速度**基准（毫秒）：位移和自转按 <c>FullShatterMs / 本值</c> 等比缩，
    /// 所以砍相位时长只会缩短飞行距离，<b>速度不变</b>。想改速度就改这里，别动 <see cref="FullShatterMs"/>。
    /// </summary>
    private const int FullShatterSpeedRefMs = 900;


    // ---- 轻量档 ----
    private const int LiteFlashMs = 30;
    private const int LiteHoldMs = 100;
    private const int LiteOverMs = 40;
    private const int LiteShardMs = 150;
    private const int LiteFadeMs = 120;

    /// <summary>径向辐条数（= 主要放射裂纹的条数）。</summary>
    private const int GlassSpokes = 26;

    /// <summary>同心环数（和辐条一起撒种子 → Voronoi 的边界会落成放射线 + 弧线）。</summary>
    private const int GlassRings = 8;

    /// <summary>额外随机点：把「辐条+环」的规则感打散。</summary>
    private const int GlassRandomExtra = 40;

    /// <summary>随机点的径向密度指数：越大 = 命中点附近越密。</summary>
    private const float GlassDensity = 1.9f;

    /// <summary>短于这个长度的裂纹不画（几何照旧参与切块，只是不画出来 —— 少了会显得像马赛克）。</summary>
    private const float GlassMinEdgeDraw = 12f;

    /// <summary>慢放目标（1 = 正常速度）。</summary>
    private const float SlowTimeScale = 0.18f;

    /// <summary>
    /// 碎块提亮的 gamma（&lt;1 = 变亮，1 = 原样）。2026-11-21 用户定：缝改纯黑后，
    /// 碎块要亮一档才拉得开对比。作用在切下来的那帧画面上，所以抬的是暗部：
    /// <c>0.05 → 0.106</c>、<c>0.10 → 0.178</c>、<c>0.50 → 0.595</c>、白（1.0）不变。
    /// 只影响碎块，不影响备碎前的整屏亮度。想更亮就往 0.6 调，想收一点往 0.85 调。
    /// </summary>
    private const float ShardGamma = 0.75f;

    /// <summary>
    /// 本次播放的亮度系数（1 = 原样），<see cref="PlayAsync"/> 开头从设置读一次。
    /// 设置项：设置 → 特效 →「击杀特效亮度」（50%–150%）。
    /// </summary>
    private static float _brightness = 1f;

    /// <summary>颜色乘亮度：只乘 RGB，alpha 不动，超过 1 截断。用于暗底 / 剪影这类有色层。</summary>
    private static Color Tint(Color c) => new(
        Math.Min(1f, c.R * _brightness),
        Math.Min(1f, c.G * _brightness),
        Math.Min(1f, c.B * _brightness),
        c.A);

    /// <summary>
    /// alpha 乘亮度。用于「本身就是白色」的层（白闪 / 定格白底 / 裂纹 / 破口光）——
    /// 这些的 RGB 已经是 1.0，乘系数没有任何变化，只能靠不透明度调。
    /// </summary>
    /// <summary>
    /// 击杀特效的背景色（2026-11-21 用户要求）：**灰白**，不要纯白 —— 纯白闪一下太刺眼。
    /// 只改亮场的底色；紫色剪影那一段（<c>white.Color = 深紫</c>）不受影响。
    /// </summary>
    private static readonly Color BackdropColor = new(0.90f, 0.89f, 0.86f, 1f);

    private static float TintAlpha(float a) => Math.Min(1f, a * _brightness);

    /// <summary>
    /// 终结动画里昴用**固定贴图**做的剪影（不再实时复制活体）。
    /// 素材 = cast 第 7 帧（0 基 index 6 = 0.25s）烤成的「白色 RGB + 原 alpha」，
    /// 颜色由 <see cref="BuildFlatMaterial"/> 的平涂 shader 上，所以负片/正片换色照样能用。
    ///
    /// <para>为什么不再 Clone 活体：spine 的 track 播完会被清掉，骨架回落到 <b>setup 姿势</b>
    /// —— 我们这套翻页骨架的默认 attachment 就是待机帧，于是玩家看到「最后一帧闪回待机状」。
    /// 固定贴图没有状态机，也就没有这个回落。</para>
    /// </summary>
    private const string SilhouettePath = "res://Subaru/images/vfx/kill_silhouette.png";

    /// <summary>
    /// 剪影贴图的画布尺寸与「实际画面占的区域」（贴图里非透明部分的包围盒，360×480 画布坐标）。
    /// 用来把剪影摆到游戏给的可视矩形里：缩放按高度算，位置让两边的「内容中心」对齐。
    /// </summary>
    private const float SilCanvasW = 360f;
    private const float SilCanvasH = 480f;
    private const float SilArtX0 = 122f;
    private const float SilArtY0 = 117f;
    private const float SilArtX1 = 343f;
    private const float SilArtY1 = 463f;

    /// <summary>
    /// 最后的手动微调倍数（1 = 完全按实测矩形）。位置/大小如果还差一点，只改这个数。
    /// 2026-11-20 用户实测：位置对了，尺寸要放大 → 1.3。
    /// 缩放是以「内容中心」为锚点的（中心位置保持不动）；如果哪天觉得脚踩不到地，
    /// 把 <see cref="SpawnPlayerSilhouette"/> 里的 pos 改成按底边对齐即可。
    /// </summary>
    private const float SilScaleTweak = 1.3f;

    /// <summary>通知那一刻的快照。所有晚读会失效的东西都在这里。</summary>
    private sealed record KillInfo(
        Creature Creature,
        ICombatState? State,
        RoomType RoomType,
        bool Multiplayer,
        Vector2 Center,
        Node2D? VisualCopy,
        Transform2D VisualTransform,
        string? AnimName,
        float AnimTime);

    private static readonly List<KillInfo> Pending = [];
    private static bool _scheduled;
    private static bool _busy;

    /// <summary>
    /// 战斗里死了一只敌人时调用（同一波范围伤害会连续调多次）。
    /// 返回非 null 时，调用方应把 <c>Hook.AfterDeath</c> 的返回值接在它后面（= 等特效播完）。
    /// </summary>
    public static Task? NotifyKill(Creature? creature)
    {
        try
        {
            if (creature == null || !Settings.SubaruVisualSettings.KillEffectEnabled)
                return null;

            // 实验体第一、第二形态是「原地复活」的转阶段，不是真死：不吃击杀特效（2026-11-24 用户定）。
            // 白闪 / 裂缝会盖住复活动作，剪影副本对齐的又是「本该消失的本体」。
            if (FinisherExempt.IsRevivingTestSubject(creature))
                return null;

            Pending.Add(Snapshot(creature));

            // 同一批只让第一只负责阻塞
            if (_scheduled)
                return null;

            _scheduled = true;
            var tcs = new TaskCompletionSource<bool>();
            _ = FlushAsync(tcs);
            return tcs.Task;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect notify failed: " + e.Message);
            return null;
        }
    }

    /// <summary>抓快照：状态、房间、人数、屏幕位置、剪影副本、骨架姿势。</summary>
    private static KillInfo Snapshot(Creature creature)
    {
        ICombatState? state = null;
        try
        {
            state = creature.CombatState;
        }
        catch
        {
            // 生物已在脱离战斗
        }

        var multiplayer = false;
        try
        {
            var players = state?.Players;
            multiplayer = players != null && players.Count > 1;
        }
        catch
        {
            multiplayer = false;
        }

        // 房间类型必须在这一刻读：生物离开战斗后 CombatState 会变 null
        var roomType = RoomType.Unassigned;
        try
        {
            var runState = state?.RunState;
            var room = runState?.CurrentRoom;
            if (room != null)
                roomType = room.RoomType;
        }
        catch
        {
            roomType = RoomType.Unassigned;
        }

        var center = Vector2.Zero;
        var copy = default(Node2D);
        var transform = default(Transform2D);
        string? animName = null;
        var animTime = 0f;

        try
        {
            var cnode = creature.GetCreatureNode();
            if (cnode != null && GodotObject.IsInstanceValid(cnode))
            {
                try
                {
                    var hitbox = cnode.Hitbox;
                    if (hitbox != null && GodotObject.IsInstanceValid(hitbox))
                        center = hitbox.GetGlobalRect().GetCenter();
                }
                catch
                {
                    center = Vector2.Zero;
                }

                var visuals = cnode.Visuals as Node2D;
                if (visuals != null && GodotObject.IsInstanceValid(visuals))
                {
                    if (center == Vector2.Zero)
                        center = visuals.GetGlobalTransformWithCanvas().Origin;

                    transform = visuals.GetGlobalTransformWithCanvas();
                    copy = visuals.Duplicate() as Node2D;

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
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect snapshot failed: " + e.Message);
        }

        return new KillInfo(creature, state, roomType, multiplayer, center, copy, transform, animName, animTime);
    }

    private static async Task FlushAsync(TaskCompletionSource<bool> tcs)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null || tree.Root == null)
            {
                tcs.TrySetResult(false);
                Reset();
                return;
            }

            // 等两帧：同一波范围伤害的死亡会在这期间全部进来
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var kills = Pending.ToList();
            Pending.Clear();
            _scheduled = false;

            if (kills.Count == 0 || _busy)
            {
                FreeCopies(kills);
                tcs.TrySetResult(false);
                return;
            }

            if (!Settings.SubaruVisualSettings.KillEffectEnabled)
            {
                GD.Print("[Subaru] kill effect: skipped (setting off)");
                FreeCopies(kills);
                tcs.TrySetResult(false);
                return;
            }

            if (kills.Any(k => k.Multiplayer))
            {
                GD.Print("[Subaru] kill effect: skipped (multiplayer)");
                FreeCopies(kills);
                tcs.TrySetResult(false);
                return;
            }

            GD.Print($"[Subaru] kill effect: start (kills={kills.Count})");
            try
            {
                await PlayAsync(tree, kills);
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        }
        catch (Exception e)
        {
            Reset();
            tcs.TrySetResult(false);
            GD.PushWarning("[Subaru] kill effect failed: " + e.Message);
        }
    }

    private static void Reset()
    {
        foreach (var info in Pending)
        {
            if (info.VisualCopy != null && GodotObject.IsInstanceValid(info.VisualCopy))
                info.VisualCopy.QueueFree();
        }

        Pending.Clear();
        _scheduled = false;
    }

    private static void FreeCopies(List<KillInfo> kills)
    {
        foreach (var k in kills)
        {
            if (k.VisualCopy != null && GodotObject.IsInstanceValid(k.VisualCopy))
                k.VisualCopy.QueueFree();
        }
    }

    /// <summary>
    /// 满档判定（2026-11-21 用户定，规则只有一条）：
    /// <para><b>把「爪牙」这层状态筛掉之后，看剩下的敌人有没有清空</b>；
    /// 而且这一批必须真的死了「非爪牙」—— 只死爪牙（召唤物）永远不触发满档。</para>
    ///
    /// <para>房间类型<b>不再参与</b>：精英 / Boss 跟普通怪同一条规则。以前是
    /// <c>RoomType.Elite or Boss → 直接 true</c>，多怪精英里每杀一只都会播一次 1.58s 满档，
    /// 还会把死亡流程一起按住。</para>
    ///
    /// <para>「爪牙」用引擎自己的口径 <see cref="Creature.IsSecondaryEnemy"/> ——
    /// IL 实证：<c>Side == Enemy &amp;&amp; Powers.Any(p =&gt; p.OwnerIsSecondaryEnemy)</c>，
    /// 目前只有 <c>MinionPower</c> 把 <c>OwnerIsSecondaryEnemy</c> 覆写成 true，
    /// 而 <c>MinionPower.ShouldPowerBeRemovedAfterOwnerDeath()</c> 返回 false
    /// —— 所以死亡这一刻还读得到，不会因为「死前被清状态」误判。</para>
    /// </summary>
    private static bool IsFullTier(List<KillInfo> kills)
    {
        var state = kills.Select(k => k.State).FirstOrDefault(s => s != null);
        if (state == null)
            return false;

        try
        {
            // 名单里的怪（死后还会生成敌人）不算终结：地精佣兵爆小地精、异蛙寄生虫这类
            if (kills.Any(k => FinisherExempt.Contains(k.Creature)))
                return false;

            // 这一批里必须死了一只「非爪牙」（Boss / 主怪 / 普通怪都算）。
            if (!kills.Any(k => !k.Creature.IsSecondaryEnemy))
                return false;

            // 场上剩下的敌人里把爪牙筛掉：只要还有非爪牙活着就不是最后一击。
            var left = state.HittableEnemies;
            if (left == null)
                return true;

            var dead = kills.Select(k => k.Creature).ToHashSet();
            return left.All(c => c == null || c.IsDead || c.IsSecondaryEnemy || dead.Contains(c));
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- 主流程

    private static async Task PlayAsync(SceneTree tree, List<KillInfo> kills)
    {
        _busy = true;

        // 亮度在这里读一次：改滑杆 → 下一次击杀立刻生效。
        _brightness = Settings.SubaruVisualSettings.KillEffectBrightnessPercent / 100f;

        // 整批特效的根节点（房间前景容器里），收尾时一把删掉
        Node2D? vfxRoot = null;

        // 特效期间被藏起来的玩家本体（播完恢复）
        var hiddenVisuals = new List<CanvasItem>();

        // 裂缝网络（碎裂时藏起来：裂纹由碎片之间的缝来表现）
        Node2D? crackRoot = null;

        // Voronoi 玻璃：碎片形状 = 裂纹切出来的块
        Glass? glass = null;
        var impact = Vector2.Zero;

        try
        {
            var full = IsFullTier(kills);

            // 整个击杀特效挂在房间的「前景 VFX 容器」里（角色之上、**UI 之下**）。
            // 以前用 CanvasLayer(130~132)，那是盖在所有 UI（含手牌）上面的 —— 2026-11-21 用户要求改掉。
            var view = tree.Root.GetVisibleRect().Size;
            vfxRoot = RoomVfx.Front("SubaruKillFx");
            if (vfxRoot == null)
            {
                GD.PushWarning("[Subaru] kill effect: no front vfx container, abort");
                return;
            }

            var whiteHolder = new Node2D { Name = "SubaruKillWhite" };
            vfxRoot.AddChild(whiteHolder);
            var white = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = new Color(BackdropColor, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            whiteHolder.AddChild(white);

            var silRoot = new Node2D { Name = "SubaruKillSilhouette" };
            vfxRoot.AddChild(silRoot);

            var fxRoot = new Node2D { Name = "SubaruKillFx" };
            vfxRoot.AddChild(fxRoot);

            var material = BuildFlatMaterial();
            var shardMaterial = BuildShardMaterial();

            // ---- 剪影 ----
            var silhouettes = new List<Node2D>();

            var player = kills.Select(k => k.State).FirstOrDefault(s => s != null)
                ?.Players?.FirstOrDefault(p => p?.Character is Character.Subaru);
            if (player?.Creature != null)
            {
                var subaruSil = SpawnPlayerSilhouette(player.Creature, silRoot, material, hiddenVisuals);
                if (subaruSil != null)
                    silhouettes.Add(subaruSil);
            }

            foreach (var kill in kills)
            {
                var sil = AttachCopy(kill, silRoot, material);
                if (sil != null)
                    silhouettes.Add(sil);
            }

            GD.Print($"[Subaru] kill effect: tier={(full ? "full" : "lite")} silhouettes={silhouettes.Count} " +
                     $"brightness={Mathf.RoundToInt(_brightness * 100f)}%");

            if (silhouettes.Count == 0)
            {
                GD.Print("[Subaru] kill effect: no silhouette, abort");
                return;
            }

            // ---- 白闪 ----
            white.CreateTween().TweenProperty(white, "color:a", TintAlpha(0.95f), FullFlashMs / 1000.0);
            await WaitMs(tree, full ? FullFlashMs : LiteFlashMs);

            if (full)
            {
                // 慢放开始（墙钟计时，不受影响）
                SetTimeScale(SlowTimeScale);

                // 负片：暗底白剪影 1~2 帧
                SetSilhouetteColors(material, Tint(new Color(1f, 1f, 1f, 1f)), Tint(new Color(0.88f, 0.90f, 1f, 1f)));
                white.Color = Tint(new Color(0.14f, 0.02f, 0.18f, 0.97f));
                await WaitMs(tree, FullNegativeMs);

                // 回白底紫剪影，定格
                SetSilhouetteColors(material, Tint(new Color(0.69f, 0.44f, 1f, 1f)), Tint(new Color(0.23f, 0.06f, 0.37f, 1f)));
                white.CreateTween().TweenProperty(white, "color", new Color(BackdropColor, TintAlpha(0.86f)), 0.05);
                await WaitMs(tree, FullHoldMs);

                // 蓄力：剪影轻微上浮，给“要炸了”的预期
                var charge = silRoot.CreateTween();
                charge.SetParallel(true);
                charge.TweenProperty(silRoot, "position", new Vector2(0, -6f), FullChargeMs / 1000.0)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                await WaitMs(tree, FullChargeMs);

                // ---- 裂缝：从怪物身上一路蔓延到全屏（Voronoi 的边 = 裂纹 = 碎片形状）----
                // 先把底的亮度压下去：白裂纹画在白底上等于看不见 ——
                // 玻璃裂口的观感来自「暗底上一条硬亮线」，所以定格画面在裂缝阶段转暗。
                white.CreateTween().TweenProperty(white, "color", Tint(new Color(0.05f, 0.02f, 0.09f, 0.90f)),
                                                  FullCrackMs / 1000.0 * 0.35);
                var viewSize0 = ViewportSize(tree);
                impact = kills.Count > 0 ? kills[0].Center : viewSize0 * 0.5f;
                glass = BuildGlass(impact, viewSize0, new Random(System.Environment.TickCount ^ impact.GetHashCode()));
                crackRoot = new Node2D { Name = "SubaruKillCracks" };
                fxRoot.AddChild(crackRoot);
                SpawnCracks(glass.Edges, impact, crackRoot, viewSize0, FullCrackMs);
                SpawnImpactGlint(impact, crackRoot);
                await WaitMs(tree, FullCrackMs);
            }
            else
            {
                white.CreateTween().TweenProperty(white, "color", new Color(BackdropColor, TintAlpha(0.80f)), 0.05);
                await WaitMs(tree, LiteHoldMs);
            }

            // ---- 爆点：恢复正常速度 + 过曝 + 抖动 ----
            SetTimeScale(1f);
            white.Color = new Color(BackdropColor, TintAlpha(1f));
            ShakeScreen();
            await WaitMs(tree, full ? FullOverMs : LiteOverMs);

            if (full)
            {
                // ---- 全屏碎裂：按 Voronoi 单元撕开，从破裂中心（怪物身上）向外扩散消失 ----
                var viewSize = ViewportSize(tree);

                // 裂缝先藏掉：截屏不带它，裂纹由碎片之间的缝表现（否则每条缝会粗一倍）
                if (crackRoot != null && GodotObject.IsInstanceValid(crackRoot))
                    crackRoot.Visible = false;

                // 负片：暗底 + **白**剪影（2026-11-20 用户定：散开时也用负片剪影）
                // 碎块贴的是这一帧，所以飞出去的就是「负片画面本身」
                SetSilhouetteColors(material, Tint(new Color(1f, 1f, 1f, 1f)), Tint(new Color(0.90f, 0.92f, 1f, 1f)));
                white.CreateTween().TweenProperty(white, "color", new Color(0.05f, 0.02f, 0.10f, 0.96f),
                                                  FullShatterPrepMs / 1000.0);
                await WaitMs(tree, FullShatterPrepMs);

                var shattered = glass != null &&
                                ShatterGlass(tree, fxRoot, glass, impact, viewSize, FullShatterMs, shardMaterial);
                if (!shattered)
                    SpawnShards(impact, fxRoot); // 截屏失败就退回局部碎片

                // 碎片下面铺**纯黑**：碎块之间的缝就是这个颜色（2026-11-21 用户定：
                // 缝要纯黑 #000，不透明，这样飞出去的是「黑虚空 + 发亮的碎块」）。
                // 注意别顺手把上面备碎那条颜色也改黑 —— 那条是「被切进碎片里的画面」，
                // 改了碎片会跟着变黑，碎片只会更暗。
                white.CreateTween().TweenProperty(white, "color", new Color(0f, 0f, 0f, 1f), 0.10);
                silRoot.Visible = false;

                await WaitMs(tree, FullShatterMs);
            }
            else
            {
                foreach (var kill in kills)
                    SpawnShards(kill.Center, fxRoot);

                var fadeSil = silRoot.CreateTween();
                fadeSil.SetParallel(true);
                fadeSil.TweenProperty(silRoot, "modulate:a", 0f, LiteShardMs / 1000.0);

                await WaitMs(tree, LiteShardMs);
            }

            // ---- 收尾 ----
            // 2026-11-20 用户定：满档不要收尾，碎块散完直接结束（图层的释放就是收尾）。
            if (!full)
            {
                white.CreateTween().TweenProperty(white, "color:a", 0f, LiteFadeMs / 1000.0);
                await WaitMs(tree, LiteFadeMs);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect play failed: " + e.Message);
        }
        finally
        {
            SetTimeScale(1f);
            _busy = false;

            // 玩家本体恢复显示
            foreach (var item in hiddenVisuals)
            {
                if (GodotObject.IsInstanceValid(item))
                    item.Visible = true;
            }

            vfxRoot?.QueueFree();
        }
    }

    /// <summary>墙钟等待：慢放（压 TimeScale）时依然按真实毫秒走。</summary>
    private static async Task WaitMs(SceneTree tree, int ms)
    {
        if (ms <= 0)
            return;

        var end = Time.GetTicksMsec() + (ulong)ms;
        while (Time.GetTicksMsec() < end)
        {
            if (tree?.Root == null)
                return;

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void SetTimeScale(float scale)
    {
        try
        {
            Engine.TimeScale = scale;
        }
        catch
        {
            // 改不了就按正常速度跑
        }
    }

    private static void ShakeScreen()
    {
        try
        {
            var game = NGame.Instance;
            if (game != null && GodotObject.IsInstanceValid(game))
                game.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short, 0f);
        }
        catch
        {
            // 抖动失败不影响特效
        }
    }

    // ---------------------------------------------------------------- 剪影

    /// <summary>
    /// 放**固定贴图**剪影（昴专用）：不再复制活体，姿势永远是烤好的「抬手」那一帧。
    ///
    /// <para><b>位置/缩放</b>：直接借玩家 spine 节点的全局变换。骨架画布 360×480 的中心就在节点原点上
    /// —— 打包脚本写 attachment 坐标时是 <c>x = x0 - 180</c>（画布中心为 0），所以一张「整张画布大小」的
    /// 居中贴图按这个变换贴上去，和 spine 自己画的帧是同一个矩形。</para>
    ///
    /// <para><b>方向</b>：贴图是 y 向下的画布坐标，所以只取「位置 + 标量缩放」，
    /// 不继承 spine 节点的翻转/旋转 —— 否则会上下颠倒。</para>
    ///
    /// <para><b>遮掉本体</b>：本体（可能在待机）会被整块隐藏，剪影边上不会漏出待机的轮廓。</para>
    /// </summary>
    private static Node2D? SpawnPlayerSilhouette(Creature creature, Node2D parent, ShaderMaterial material,
                                                 List<CanvasItem> hidden)
    {
        try
        {
            var cnode = creature.GetCreatureNode();
            if (cnode == null || !GodotObject.IsInstanceValid(cnode))
                return null;

            var visuals = cnode.Visuals as Node2D;
            if (visuals == null || !GodotObject.IsInstanceValid(visuals))
                return null;

            if (!ResourceLoader.Exists(SilhouettePath))
            {
                GD.PushWarning("[Subaru] kill effect: silhouette texture missing, fallback to live clone");
                return CloneLiveVisuals(creature, parent, material);
            }

            var tex = GD.Load<Texture2D>(SilhouettePath);
            if (tex == null)
            {
                GD.PushWarning("[Subaru] kill effect: silhouette texture load failed");
                return CloneLiveVisuals(creature, parent, material);
            }

            var spine = (FindSpineSprite(visuals) as Node2D) ?? visuals;
            var xf = spine.GetGlobalTransformWithCanvas();

            var sprite = new Sprite2D
            {
                Texture = tex,
                Centered = true,
                Material = material,
            };
            parent.AddChild(sprite);

            // 尺寸优先取游戏自己给的「可视矩形」（%Bounds / Hitbox）：
            // 只信 spine 节点的 scale 会偏 —— 2026-11-20 第一次上线时日志里 scale=0.815，
            // 贴出来的剪影比真人小了一圈（游戏对角色还会另加一层缩放）。
            var rect = VisualRect(cnode, visuals, out var rectSource);
            if (rectSource != "none")
            {
                var artH = SilArtY1 - SilArtY0;
                var artW = SilArtX1 - SilArtX0;
                var s = Mathf.Min(rect.Size.Y / artH, rect.Size.X / artW) * SilScaleTweak;
                var artCenter = new Vector2((SilArtX0 + SilArtX1) * 0.5f, (SilArtY0 + SilArtY1) * 0.5f);
                var texCenter = new Vector2(SilCanvasW * 0.5f, SilCanvasH * 0.5f);
                var pos = rect.GetCenter() - (artCenter - texCenter) * s;

                sprite.GlobalTransform = new Transform2D(0f, new Vector2(s, s), 0f, pos);
                GD.Print($"[Subaru] kill effect: fixed silhouette rect={rectSource} " +
                         $"{rect.Position.X:F1},{rect.Position.Y:F1} {rect.Size.X:F1}x{rect.Size.Y:F1} " +
                         $"scale={s:F3} pos=({pos.X:F1},{pos.Y:F1})");
            }
            else
            {
                // 兜底：直接用 spine 节点的变换（位置对，尺寸可能偏小）
                sprite.GlobalTransform = new Transform2D(
                    0f,
                    new Vector2(Mathf.Abs(xf.Scale.X), Mathf.Abs(xf.Scale.Y)),
                    0f,
                    xf.Origin);
                GD.Print($"[Subaru] kill effect: fixed silhouette (no rect) at ({xf.Origin.X:F1},{xf.Origin.Y:F1}) " +
                         $"scale=({xf.Scale.X:F3},{xf.Scale.Y:F3})");
            }

            if (visuals.Visible)
            {
                visuals.Visible = false;
                hidden.Add(visuals);
            }

            return sprite;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect fixed silhouette failed: " + e.Message);
            return null;
        }
    }

    private static Node2D? CloneLiveVisuals(Creature creature, Node2D parent, ShaderMaterial material)
    {
        try
        {
            var cnode = creature?.GetCreatureNode();
            if (cnode == null || !GodotObject.IsInstanceValid(cnode))
                return null;

            var visuals = cnode.Visuals as Node2D;
            if (visuals == null || !GodotObject.IsInstanceValid(visuals))
                return null;

            if (visuals.Duplicate() is not Node2D copy)
                return null;

            parent.AddChild(copy);
            copy.GlobalTransform = visuals.GetGlobalTransformWithCanvas();
            ApplyMaterial(copy, material);

            // 终结动画里不用它当前的姿势，固定摆成「抬手」那一帧（cast 第 7 帧 = 0.25s）
            // 只在固定贴图缺失时才会走到这里（兜底路径，仍有「播完回落待机」的毛病）
            FreezePose(copy, "cast", 6f / 24f);

            return copy;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect player silhouette failed: " + e.Message);
            return null;
        }
    }

    /// <summary>拿游戏自己给的「可视矩形」：优先视觉节点里的 %Bounds（Control），其次 Hitbox。</summary>
    internal static Rect2 VisualRect(NCreature cnode, Node2D visuals, out string source)
    {
        try
        {
            if (visuals is NCreatureVisuals ncv && ncv.Bounds != null && GodotObject.IsInstanceValid(ncv.Bounds))
            {
                var r = ncv.Bounds.GetGlobalRect();
                if (r.Size.X > 8f && r.Size.Y > 8f)
                {
                    source = "Bounds";
                    return r;
                }
            }
        }
        catch
        {
            // 读不到就试下一个
        }

        try
        {
            var hitbox = cnode.Hitbox;
            if (hitbox != null && GodotObject.IsInstanceValid(hitbox))
            {
                var r = hitbox.GetGlobalRect();
                if (r.Size.X > 8f && r.Size.Y > 8f)
                {
                    source = "Hitbox";
                    return r;
                }
            }
        }
        catch
        {
            // 同上
        }

        source = "none";
        return default;
    }

    private static Node2D? AttachCopy(KillInfo kill, Node2D parent, ShaderMaterial material)
    {
        var copy = kill.VisualCopy;
        if (copy == null || !GodotObject.IsInstanceValid(copy))
            return null;

        try
        {
            parent.AddChild(copy);
            copy.GlobalTransform = kill.VisualTransform;
            ApplyMaterial(copy, material);
            FreezePose(copy, kill.AnimName, kill.AnimTime);
            return copy;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect attach failed: " + e.Message);
            return null;
        }
    }

    internal static void ApplyMaterial(Node node, ShaderMaterial material)
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
            ApplyMaterial(child, material);
    }

    private static void FreezePose(Node2D copy, string? animName, float animTime)
    {
        if (string.IsNullOrEmpty(animName))
            return;

        try
        {
            var spine = FindSpineSprite(copy);
            if (spine == null)
                return;

            var state = new MegaSprite(spine).GetAnimationState();
            state?.SetAnimation(animName, false, 0);
            state?.Update(0f);
            state?.GetCurrent(0)?.SetTrackTime(animTime);
            state?.Update(0f);
        }
        catch
        {
            // 姿势同步失败不影响白闪/裂缝
        }
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

    private static void SetSilhouetteColors(ShaderMaterial material, Color top, Color bottom)
    {
        try
        {
            material.SetShaderParameter("color_top", top);
            material.SetShaderParameter("color_bottom", bottom);
        }
        catch
        {
            // 改不了就保持默认紫
        }
    }

    /// <summary>平涂材质（可指定上下渐变色）。夏乌拉的终结剪影用它上纯黑。</summary>
    internal static ShaderMaterial BuildFlatMaterial(Color top, Color bottom)
    {
        var mat = BuildFlatMaterial();
        try
        {
            mat.SetShaderParameter("color_top", top);
            mat.SetShaderParameter("color_bottom", bottom);
        }
        catch
        {
            // 设不进去就用默认紫
        }

        return mat;
    }

    private static ShaderMaterial BuildFlatMaterial()
    {
        const string code = """
            shader_type canvas_item;
            uniform vec4 color_top : source_color = vec4(0.69, 0.44, 1.0, 1.0);
            uniform vec4 color_bottom : source_color = vec4(0.23, 0.06, 0.37, 1.0);
            uniform float alpha_cut = 0.06;

            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                if (tex.a < alpha_cut) {
                    discard;
                }
                vec3 c = mix(color_bottom.rgb, color_top.rgb, UV.y);
                COLOR = vec4(c, tex.a);
            }
            """;

        return new ShaderMaterial { Shader = new Shader { Code = code } };
    }

    /// <summary>
    /// 碎块用的提亮材质（一整批共用一份，几十块不额外开材质）。
    ///
    /// <para>碎块是带 <c>TEXTURE</c> 的 <see cref="Polygon2D"/>（贴的是截下来的那帧屏幕），
    /// 所以这里必须先复现 canvas_item 的默认合成 <c>texture(TEXTURE, UV) * COLOR</c>，
    /// 再做 gamma 提亮 —— 直接写 <c>COLOR = ...</c> 会把贴图整个丢掉（碎块变纯色）。</para>
    /// </summary>
    private static ShaderMaterial BuildShardMaterial()
    {
        const string code = """
            shader_type canvas_item;

            uniform float gamma = 0.75;
            uniform float brightness = 1.0;

            void fragment() {
                vec4 c = texture(TEXTURE, UV) * COLOR;
                c.rgb = pow(max(c.rgb, vec3(0.0)), vec3(gamma)) * brightness;
                COLOR = min(c, vec4(1.0));
            }
            """;

        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("gamma", ShardGamma);
        material.SetShaderParameter("brightness", _brightness);
        return material;
    }

    // ---------------------------------------------------------------- 裂缝 / 碎片

    /// <summary>视口（画布）尺寸，拿不到就按 1920×1080。</summary>
    private static Vector2 ViewportSize(SceneTree tree)
    {
        try
        {
            var size = tree?.Root?.GetVisibleRect().Size ?? Vector2.Zero;
            if (size.X > 8f && size.Y > 8f)
                return size;
        }
        catch
        {
            // 读不到就用默认
        }

        return new Vector2(1920f, 1080f);
    }

    // ---------------------------------------------------------------- 玻璃碎裂（模板）

    private const string TemplatePath = "res://Subaru/data/glass_templates.txt";

    /// <summary>一块玻璃碎片（屏幕坐标的多边形）—— 就是模板里由裂纹切出来的那个面。</summary>
    private sealed class GlassPiece
    {
        public List<Vector2> Poly = [];
        public Vector2 Center;
    }

    /// <summary>一次破碎的几何：Edges = 裂纹线段，Pieces = 它切出来的面。</summary>
    private sealed class Glass
    {
        public List<GlassPiece> Pieces = [];
        public List<(Vector2 A, Vector2 B)> Edges = [];
    }

    /// <summary>烘好的裂纹模板：归一化方框 [-1,1]²、破口在原点。离线生成见 _tools/tmp/bake_glass.py。</summary>
    private sealed class GlassTemplate
    {
        public List<(Vector2 A, Vector2 B)> Segments = [];
        public List<Vector2[]> Faces = [];
    }

    private static List<GlassTemplate>? _templates;

    /// <summary>
    /// 读模板（只读一次）。行格式：
    /// <c>T</c> 新模板 / <c>S x1 y1 x2 y2</c> 裂纹线段 / <c>F n x1 y1 ... xn yn</c> 一个面。
    /// 不用 Godot 的 JSON：这里几万个数字，split + float.Parse 又快又不用碰 Variant。
    /// </summary>
    private static List<GlassTemplate> LoadTemplates()
    {
        if (_templates != null)
            return _templates;

        _templates = [];

        try
        {
            // 注意：不能用 ResourceLoader.Exists —— 它只认「导入过的资源」，
            // 我们这是原样打进 pck 的 .txt（raw file），Exists 会返回 false。
            if (!Godot.FileAccess.FileExists(TemplatePath))
            {
                GD.PushWarning($"[Subaru] kill effect: glass templates missing ({TemplatePath})");
                return _templates;
            }

            var text = Godot.FileAccess.GetFileAsString(TemplatePath);
            if (string.IsNullOrEmpty(text))
                return _templates;

            GlassTemplate? cur = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line == "T")
                {
                    cur = new GlassTemplate();
                    _templates.Add(cur);
                    continue;
                }

                if (cur == null)
                    continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "S" && parts.Length >= 5)
                {
                    cur.Segments.Add((
                        new Vector2(F(parts, 1), F(parts, 2)),
                        new Vector2(F(parts, 3), F(parts, 4))));
                }
                else if (parts[0] == "F" && parts.Length >= 2)
                {
                    var n = int.Parse(parts[1]);
                    if (parts.Length < 2 + n * 2)
                        continue;

                    var poly = new Vector2[n];
                    for (var i = 0; i < n; i++)
                        poly[i] = new Vector2(F(parts, 2 + i * 2), F(parts, 3 + i * 2));
                    cur.Faces.Add(poly);
                }
            }

            GD.Print($"[Subaru] kill effect: glass templates loaded={_templates.Count}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect: glass templates failed: " + e.Message);
        }

        return _templates;

        static float F(string[] parts, int i)
            => float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    /// <summary>
    /// 从模板生成一次破碎几何：随机挑一套模板 → 随机旋转 / 镜像 / 缩放 → 破口对准 impact。
    ///
    /// <para>缩放取「屏幕四个角离破口最远的那个」：模板方框是 [-1,1]²，
    /// 这样旋转后也保证整个屏幕被盖住。</para>
    ///
    /// <para><b>碎片 = 模板里的面 = 裂纹切出来的块</b>，不是另外算的 Voronoi。</para>
    /// </summary>
    private static Glass BuildGlass(Vector2 impact, Vector2 viewSize, Random rng)
    {
        var glass = new Glass();
        var templates = LoadTemplates();
        if (templates.Count == 0)
            return glass;

        var tpl = templates[rng.Next(templates.Count)];
        var w = viewSize.X;
        var h = viewSize.Y;

        var s = 0f;
        foreach (var corner in new[]
                 {
                     new Vector2(0f, 0f), new Vector2(w, 0f), new Vector2(w, h), new Vector2(0f, h),
                 })
        {
            var d = corner - impact;
            s = Mathf.Max(s, Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y)));
        }

        s *= 1.04f;

        var rot = rng.Next(4) * Mathf.Pi * 0.5f;
        var mirror = rng.Next(2) == 0 ? 1f : -1f;
        var cos = Mathf.Cos(rot);
        var sin = Mathf.Sin(rot);

        Vector2 Xf(Vector2 p)
        {
            var x = p.X * mirror;
            var y = p.Y;
            return impact + new Vector2(x * cos - y * sin, x * sin + y * cos) * s;
        }

        foreach (var (a, b) in tpl.Segments)
        {
            var pa = Xf(a);
            var pb = Xf(b);
            if ((pb - pa).Length() < 1.6f)
                continue; // 屏幕上已经看不见了
            glass.Edges.Add((pa, pb));
        }

        foreach (var face in tpl.Faces)
        {
            var poly = new List<Vector2>(face.Length);
            foreach (var p in face)
                poly.Add(Xf(p));

            if (poly.Count < 3)
                continue;

            var area = 0f;
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            if (Mathf.Abs(area) * 0.5f < 36f)
                continue; // 碎屑，飞起来也看不出来

            var c = Vector2.Zero;
            foreach (var p in poly)
                c += p;
            c /= poly.Count;

            glass.Pieces.Add(new GlassPiece { Poly = poly, Center = c });
        }

        GD.Print($"[Subaru] kill effect: glass template picked (segments={glass.Edges.Count}, " +
                 $"pieces={glass.Pieces.Count}, scale={s:F2})");
        return glass;
    }

    /// <summary>
    /// 按几何把裂纹画出来：每条线按「离破口多远」延迟出现（从破口往外爬），
    /// 宽度随距离收细（破口处粗、远处细），出现瞬间闪一下再回落到常态亮度。
    /// </summary>
    private static void SpawnCracks(List<(Vector2 A, Vector2 B)> edges, Vector2 impact, Node2D parent,
                                    Vector2 viewSize, int totalMs)
    {
        var maxDist = (viewSize * 1.15f).Length();
        var span = Mathf.Max(0.08f, totalMs / 1000f * 0.90f);
        var made = 0;

        foreach (var (a, b) in edges)
        {
            var mid = (a + b) * 0.5f;
            var d01 = Mathf.Clamp((mid - impact).Length() / maxDist, 0f, 1f);
            var width = 0.55f + 4.4f * Mathf.Pow(1f - d01, 2.2f);

            var line = new Line2D
            {
                Points = [a, b],
                Width = 0f,
                DefaultColor = new Color(1f, 1f, 1f, 1f),
                ZIndex = 4,
            };
            parent.AddChild(line);
            made++;

            var delay = d01 * span;
            var tween = line.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(line, "width", width, 0.05).SetDelay(delay);
            tween.TweenProperty(line, "modulate:a", TintAlpha(1f), 0.04).SetDelay(delay);
            tween.TweenProperty(line, "modulate:a", TintAlpha(0.70f), 0.22).SetDelay(delay + 0.10);
        }

        GD.Print($"[Subaru] kill effect: cracks drawn={made} (phase {totalMs}ms)");
    }

    /// <summary>破口处的一下小白闪：玻璃被敲中的那一下。</summary>
    private static void SpawnImpactGlint(Vector2 impact, Node2D parent)
    {
        try
        {
            var glint = new Polygon2D
            {
                Polygon =
                [
                    new Vector2(0, -26), new Vector2(18, -6), new Vector2(6, 18),
                    new Vector2(-16, 10), new Vector2(-22, -10),
                ],
                Color = new Color(1f, 1f, 1f, 1f),
                Modulate = new Color(1f, 1f, 1f, Math.Min(1f, _brightness)),
                Position = impact,
                ZIndex = 5,
            };
            parent.AddChild(glint);

            var tween = glint.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(glint, "scale", new Vector2(2.4f, 2.4f), 0.16f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(glint, "modulate:a", 0f, 0.18f).SetDelay(0.04f);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect glint failed: " + e.Message);
        }
    }

    /// <summary>
    /// 把画面按 Voronoi 单元撕开：每块 = 一个单元多边形，贴的正是它自己那块屏幕像素。
    /// 延迟按「离**破裂中心**（命中点）的距离」算 → 从怪物身上开始往外碎，不是屏幕中心。
    ///
    /// <para>每块向自己的中心内缩 0.9px：内缩留下的缝就是裂纹，底下透出来的是
    /// <b>纯黑</b>底（2026-11-21 用户定）→ 碎块飞散时看到的是「黑虚空 + 发亮的碎块」。
    /// 碎块自己带一层 gamma 提亮材质（<see cref="BuildShardMaterial"/>），所以黑底不会把画面压死。</para>
    /// </summary>
    private static bool ShatterGlass(SceneTree tree, Node2D parent, Glass glass, Vector2 impact, Vector2 viewSize,
                                     int durationMs, ShaderMaterial shardMaterial)
    {
        try
        {
            var img = tree?.Root?.GetTexture()?.GetImage();
            if (img == null || img.GetWidth() < 16 || img.GetHeight() < 16)
            {
                GD.Print("[Subaru] kill effect: shatter skipped (no screen image)");
                return false;
            }

            var tex = ImageTexture.CreateFromImage(img);
            var uvScale = new Vector2(img.GetWidth() / viewSize.X, img.GetHeight() / viewSize.Y);

            var rng = new Random(90210);
            var maxDist = (viewSize * 1.15f).Length();
            var dur = durationMs / 1000f;

            // 速度恒定的关键：位移/自转按「相位 ÷ 速度基准」等比缩。
            // 相位砍到 450 → 缩 0.5；飞行时间 life 也缩 0.5 → 位移 = 距离/2 ÷ 时间/2，速度不变。
            var speedScale = durationMs / (float)FullShatterSpeedRefMs;
            const float inset = 0.9f;

            var made = 0;
            foreach (var piece in glass.Pieces)
            {
                var poly = new Vector2[piece.Poly.Count];
                var uv = new Vector2[piece.Poly.Count];
                for (var i = 0; i < piece.Poly.Count; i++)
                {
                    var v = piece.Poly[i];
                    var toCenter = piece.Center - v;
                    var len = toCenter.Length();
                    var shrunk = len > inset * 2f ? v + toCenter / len * inset : v;
                    poly[i] = shrunk - piece.Center;
                    uv[i] = shrunk * uvScale;
                }

                var away = piece.Center - impact;
                var dist01 = Mathf.Clamp(away.Length() / maxDist, 0f, 1f);
                var dir = away.LengthSquared() < 1f
                    ? new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)).Normalized()
                    : away.Normalized();

                var delay = dist01 * dur * 0.40f;
                var life = dur * 0.62f;
                // 位移与自转都乘 speedScale → 相位减半时角速度、线速度都保持原样
                var travel = dir * (110f + dist01 * 340f) * speedScale;
                var spin = (float)(rng.NextDouble() - 0.5) * (0.5f + dist01 * 1.5f) * speedScale;

                var node = new Polygon2D
                {
                    Texture = tex,
                    Material = shardMaterial,
                    Polygon = poly,
                    UV = uv,
                    Position = piece.Center,
                    ZIndex = 6,
                };
                parent.AddChild(node);
                made++;

                var tween = node.CreateTween();
                tween.SetParallel(true);
                tween.TweenProperty(node, "position", piece.Center + travel, life)
                    .SetDelay(delay).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "rotation", spin, life).SetDelay(delay);
                tween.TweenProperty(node, "scale", new Vector2(0.8f, 0.8f), life).SetDelay(delay);
                tween.TweenProperty(node, "modulate:a", 0f, life * 0.55f).SetDelay(delay + life * 0.45f);
            }

            GD.Print($"[Subaru] kill effect: glass shattered into {made} pieces (cracks={glass.Edges.Count}, " +
                     $"image {img.GetWidth()}x{img.GetHeight()}, impact=({impact.X:F0},{impact.Y:F0}))");
            return made > 0;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect shatter failed: " + e.Message);
            return false;
        }
    }

    /// <summary>从某个位置放射出一圈折线裂缝（逐条由调用方控制节奏）。</summary>
    private static void SpawnCracksSequential(Vector2 center, Node2D parent, int seed)
    {
        try
        {
            if (center == Vector2.Zero)
                return;

            var rng = new Random(center.GetHashCode() + seed);
            var count = 3 + rng.Next(3);

            for (var i = 0; i < count; i++)
            {
                var angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                var len = 70f + (float)rng.NextDouble() * 90f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var side = new Vector2(-dir.Y, dir.X);

                var points = new Vector2[4];
                for (var k = 0; k < points.Length; k++)
                {
                    var t = k / (float)(points.Length - 1);
                    var jitter = (float)(rng.NextDouble() - 0.5) * 18f * t;
                    points[k] = center + dir * (len * t) + side * jitter;
                }

                var line = new Line2D
                {
                    Points = points,
                    Width = 0f,
                    DefaultColor = new Color(1f, 0.97f, 1f, 1f),
                    ZIndex = 1,
                };
                parent.AddChild(line);

                var tween = line.CreateTween();
                tween.SetParallel(true);
                tween.TweenProperty(line, "width", 2.6f, 0.14);
                tween.TweenProperty(line, "modulate:a", 0.95f, 0.07);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect cracks failed: " + e.Message);
        }
    }

    private static void SpawnShards(Vector2 origin, Node2D parent)
    {
        try
        {
            if (origin == Vector2.Zero)
                return;

            var rng = new Random(origin.GetHashCode());

            for (var i = 0; i < 28; i++)
            {
                var angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                var dist = 70f + (float)rng.NextDouble() * 120f;
                var size = 4f + (float)rng.NextDouble() * 10f;

                var shard = new Polygon2D
                {
                    Polygon =
                    [
                        new Vector2(0, -size),
                        new Vector2(size * 0.8f, size * 0.7f),
                        new Vector2(-size * 0.6f, size * 0.8f),
                    ],
                    Color = new Color(0.62f, 0.36f, 0.95f, 1f),
                    Position = origin + new Vector2(
                        (float)(rng.NextDouble() - 0.5) * 70f,
                        (float)(rng.NextDouble() - 0.5) * 140f),
                    ZIndex = 2,
                };
                parent.AddChild(shard);

                var target = shard.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                var tween = shard.CreateTween();
                tween.SetParallel(true);
                tween.TweenProperty(shard, "position", target, 0.34)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(shard, "rotation", (float)(rng.NextDouble() - 0.5) * 7f, 0.34);
                tween.TweenProperty(shard, "modulate:a", 0f, 0.34);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] kill effect shards failed: " + e.Message);
        }
    }
}
