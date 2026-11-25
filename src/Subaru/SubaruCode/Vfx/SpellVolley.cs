using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 法术弹幕：每发子弹 = 一份独立的 spine 骨架实例（<c>subaru_spell_bullet.tscn</c>），
/// 骨架素材只有一份（图集 / json / skel_data 共用），和原版储君的武器一个路子。
///
/// 一发的生命周期：
///   ① emerge 动画（孔洞张开 + 水晶冒出，16 帧 @30fps = 0.533s）
///   ② 停留 Dwell 秒
///   ③ fly：朝目标匀速飞（速度固定），命中时**单独结算这一发伤害**
///
/// 发数按 0.08s 的间隔依次在 15 个锚点轮转生成。
/// 锚点 / 节奏与 <c>_anim/data/spell_anchors.json</c> 一致（离线画布调好的那套数）。
/// </summary>
public static class SpellVolley
{
    /// <summary>总开关：关掉就退回卡牌原来的结算方式（打击特效总开关也会读成 false）。</summary>
    private static bool _enabled = true;

    /// <summary>
    /// 弹幕是否启用。读的时候会连带看设置页的「打击特效」总开关 ——
    /// 卡牌是靠这个属性决定「要不要等弹幕命中再出伤」，所以关掉总开关时
    /// 必须在这里读成 false，伤害才会立刻按原版结算（否则弹幕不播、伤害也不结算）。
    /// </summary>
    public static bool Enabled
    {
        get => _enabled && Settings.SubaruVisualSettings.HitFxEnabled;
        set => _enabled = value;
    }

    // ---- 与 _anim/data/spell_anchors.json 同步（2026-11-20 用户在画布上重摆的 15 个点）----
    private static readonly Vector2[] Anchors =
    [
        new(35, -563), new(100, -625), new(-124, -444), new(-30, -657), new(45, -663),
        new(-74, -397), new(16, -617), new(-127, -537), new(-32, -459), new(70, -693),
        new(4, -506), new(-37, -553), new(98, -659), new(-54, -610), new(-83, -487),
    ];

    /// <summary>
    /// 整套锚点的整体平移（x 向右为正、y 向下为正，单位≈屏幕像素）。
    /// 只改这一个数就能把 10 个法阵一起挪，不用动锚点表。
    /// </summary>
    public static Vector2 AnchorOffset = new Vector2(-100, 130);  // 累计：左 100、下 130

    /// <summary>
    /// 段数 -> 视觉发数（用户定，2026-11-21 下调）：
    ///   前 3 段**每段 3 发**（1 段 = 3、2 段 = 6、3 段 = 9），第 4 段起每段 +2（11、13、15…）。
    /// 多出来的那几发只飞不结算伤害。
    /// </summary>
    public static int VisualBullets(int hits)
    {
        if (hits <= 0) return 3;
        if (hits <= 3) return hits * 3;
        return 9 + (hits - 3) * 2;
    }

    private const double Gap = 0.08;          // 相邻子弹生成间隔
    private const double Dwell = 0.24;        // 冒出后原地停留
    private const double FlightSpeed = 2990;  // 飞行速度（单位/秒）
    /// <summary>子弹骨架的整体缩放（1.0 = 骨架画布 336x168 的原始大小，单位与角色节点空间一致）。</summary>
    public static float BulletScale = 0.34f;   // 0.2 × 1.7

    private const double EmergeDur = 16.0 / 30.0;
    private const string BulletScenePath =
        "res://Subaru/animations/characters/subaru_spell/subaru_spell_bullet.tscn";

    // 注意：骨架的「正前方」是 +x（水晶从洞里往右冒），所以节点 rotation = 朝向角
    private sealed class Shot
    {
        public int Index;
        public Node2D Node = null!;
        public MegaSprite? Sprite;
        public Vector2 Spawn;
        public double T0;
        public bool Fresh = true;
        public bool Flying;
        public Vector2 FlyFrom;
        public float FlightTime;
        public float FlyT;
    }

    // ─────────────────────────────────────────────────────────
    // 子弹节点池（2026-11-21 用户定：接段数的代价用池化抵掉）
    //
    // 原来每一发都 `scene.Instantiate()` + `new MegaSprite(...)`，而弹速很快、一梭子 5~13 发，
    // 全在几百毫秒内完成 —— Spine 实例化 + 骨架绑定是这条链路里最贵的一步（每发一次）。
    // 现在节点建好就留着复用：池里的节点**不挂在战斗树里**（RemoveChild 摘下来），
    // 所以换房间/换战斗不会跟着被 QueueFree，也不会残留在战场上。
    // ─────────────────────────────────────────────────────────

    private static readonly List<Shot> Pool = new();

    /// <summary>池子上限（一梭子最多 13 发，留一点余量，别让它无限吃内存）。</summary>
    private const int PoolCap = 24;

    private static Shot? TakeFromPool()
    {
        while (Pool.Count > 0)
        {
            var shot = Pool[^1];
            Pool.RemoveAt(Pool.Count - 1);

            if (GodotObject.IsInstanceValid(shot.Node))
                return shot;

            // 被场景一起释放掉了 → 丢掉，继续找下一个
        }

        return null;
    }

    /// <summary>一发打完：摘出战斗树、复位、放回池子（池满就真删）。</summary>
    private static void Release(Shot shot)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(shot.Node))
                return;

            if (Pool.Count >= PoolCap)
            {
                shot.Node.QueueFree();
                return;
            }

            shot.Node.GetParent()?.RemoveChild(shot.Node);
            shot.Node.Visible = false;
            shot.Node.Position = Vector2.Zero;
            shot.Node.Rotation = 0f;
            shot.Node.Scale = Vector2.One;
            shot.Node.Modulate = Colors.White;
            shot.Flying = false;
            shot.FlyT = 0f;
            shot.Fresh = true;
            Pool.Add(shot);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell bullet release failed: " + e.Message);

            if (GodotObject.IsInstanceValid(shot.Node))
                shot.Node.QueueFree();
        }
    }

    /// <summary>弹幕 + 每发命中单独结算（由牌主动调用，如 Celestia）。</summary>
    public static async Task PlayAsync(
        PlayerChoiceContext ctx,
        CardModel card,
        CardPlay play,
        Creature target,
        int hits,
        decimal perHit)
    {
        if (card?.Owner == null)
            return;

        await PlayAsyncCore(card.Owner, target, hits, perHit, card, play, ctx);
    }

    /// <summary>
    /// 弹幕核心。card/play/ctx 为 null 时**只播动画，不结算伤害**
    /// （给「伤害仍按卡牌自己结算」的牌用）。
    /// </summary>
    private static async Task PlayAsyncCore(
        Player owner,
        Creature target,
        int hits,
        decimal perHit,
        CardModel? card,
        CardPlay? play,
        PlayerChoiceContext? ctx,
        TaskCompletionSource<bool>? firstHit = null)
    {
        if (!Enabled || owner?.Creature == null || target == null)
            return;

        // 视觉发数按「段数 -> 发数」表算；伤害只按真实段数结算（多出来的纯视觉）
        var damageHits = Math.Max(0, hits);

        // ---- 活力（Vigor）要按「整张牌」算，不能只给第一段 ----
        //
        // 原版多段攻击 = **一条 AttackCommand 带 N 段**（WithHitCount）：
        //   · VigorPower.BeforeAttack 记住这条命令（只认第一条）
        //   · ModifyDamageAdditive 在**每一段**的伤害结算里都返回活力值
        //   · 整条命令结束后 AfterAttack 才 ModifyAmount(-amountWhenAttackStarted) 把活力清掉
        // 所以原版里多段牌的每一段都吃满活力。
        //
        // 我们是「每段一条 AttackCommand」→ 第 1 段就把活力吃掉、清零，第 2 段开始吃不到。
        // 这里在开打前把活力值记下来，从第 2 段起自己补上，等价于原版口径。
        var vigorBonus = 0m;
        try
        {
            var vig = owner.Creature?.Powers?.OfType<VigorPower>().FirstOrDefault();
            if (vig != null && vig.Amount > 0m)
                vigorBonus = vig.Amount;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell volley: read vigor failed: " + e.Message);
        }

        if (vigorBonus > 0m && damageHits > 1)
            GD.Print($"[Subaru] spell volley: vigor +{vigorBonus} carried into all {damageHits} hits");

        // 自己结算伤害的牌（如 Celestia）：多段命中音效从第 1 段开始循环
        if (card != null && play != null && ctx != null && damageHits > 1)
            Audio.MultiHitSfx.Restart();
        var shots_count = VisualBullets(damageHits);
        if (shots_count <= 0)
            return;

        var creatureNode = owner.Creature.GetCreatureNode();
        var root = FindVisualRoot(creatureNode);
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            GD.PushWarning("[Subaru] spell volley: no visual root");
            return;
        }

        PackedScene? scene = null;
        try
        {
            scene = GD.Load<PackedScene>(BulletScenePath);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell volley: load scene failed: " + e.Message);
        }

        if (scene == null)
        {
            GD.PushWarning("[Subaru] spell volley: scene missing, fallback to direct damage");
            if (card != null && play != null && ctx != null)
            {
                await DamageCmd.Attack(perHit).FromCard(card, play).Targeting(target)
                    .WithHitCount(hits).Execute(ctx);
            }
            return;
        }

        var tree = root.GetTree();
        var shots = new List<Shot>();
        var spawned = 0;
        var damaged = 0;
        var reusedCount = 0;
        var t = 0.0;
        var last = Time.GetTicksMsec() / 1000.0;

        GD.Print($"[Subaru] spell volley start: hits={hits}, visual={shots_count}, per={perHit}, bulletScale={BulletScale}, offset={AnchorOffset}");

        try
        {
            while (spawned < shots_count || shots.Count > 0)
            {
                // 目标没了就停下
                if (target == null || target.IsDead)
                    break;

                await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                var now = Time.GetTicksMsec() / 1000.0;
                var dt = now - last;
                if (dt < 0 || dt > 0.25) dt = 0.016;
                last = now;
                t += dt;

                // 目标位置（生成和飞行都用它）
                var targetPos = LocalTarget(root, target);

                // ---- ① 生成（朝向目标）----
                while (spawned < shots_count && t + 1e-6 >= spawned * Gap)
                {
                    var anchor = Anchors[spawned % Anchors.Length] + AnchorOffset;
                    var dir = targetPos - anchor;
                    var aim = dir.LengthSquared() > 0.01f ? dir.Angle() : 0f;
                    var shot = Spawn(scene, root, anchor, aim, t, spawned, out var reused);
                    if (shot != null)
                    {
                        shots.Add(shot);
                        if (reused)
                            reusedCount++;
                    }
                    spawned++;
                }

                // ---- ② 推进 ----
                for (var i = shots.Count - 1; i >= 0; i--)
                {
                    var s = shots[i];
                    if (!GodotObject.IsInstanceValid(s.Node))
                    {
                        shots.RemoveAt(i);
                        continue;
                    }

                    var age = t - s.T0;

                    // 冒出 -> 停留
                    if (age < EmergeDur + Dwell)
                        continue;

                    if (!s.Flying)
                    {
                        s.Flying = true;
                        s.FlyFrom = s.Node.Position;
                        var dist = s.FlyFrom.DistanceTo(targetPos);
                        s.FlightTime = Math.Max(0.04f, (float)(dist / FlightSpeed));
                        s.FlyT = 0f;
                        PlayAnim(s, "fly");
                    }

                    s.FlyT += (float)dt;
                    var k = Math.Clamp(s.FlyT / s.FlightTime, 0f, 1f);
                    s.Node.Position = s.FlyFrom.Lerp(targetPos, k);

                    if (k >= 1f)
                    {
                        if (s.Index == 0)
                            firstHit?.TrySetResult(true);

                        // ---- ③ 命中：这一发单独结算，但**不 await** ----
                        // 每段伤害都带自己的演出，await 会把整条弹幕串行化（多段时非常卡）。
                        // 这里只负责「按命中时刻发起」，结算各自并行跑。
                        // 只有前 damageHits 发带伤害，补出来的那几发纯视觉
                        if (card != null && play != null && ctx != null && s.Index < damageHits)
                        {
                            damaged++;

                            // 第 1 段让原版 Vigor 自己加（它会被这条命令吃掉并清零），
                            // 第 2 段起用开打前记下的数值手动补齐 → 每段都一样吃满活力
                            var hitDamage = damaged == 1 ? perHit : perHit + vigorBonus;

                            // 打击感 L1：这一发真的打伤害 → 按「伤害 / 目标最大生命」分档顿帧
                            // 打击感 L2：命中点光爆（纯视觉补的子弹也放，只是不顿帧）
                            try
                            {
                                var killed = target.IsDead || target.CurrentHp <= 0 || target.CurrentHp <= (int)hitDamage;
                                HitFeel.OnHit(hitDamage, target.MaxHp, killed, target);
                                HitFeel.SpawnBurst(root, targetPos, killed ? 108f : 78f);
                            }
                            catch (Exception feelEx)
                            {
                                GD.PushWarning("[Subaru] hit feel hook failed: " + feelEx.Message);
                            }

                            FireHit(ctx, card, play, target, hitDamage, damaged);
                        }
                        else
                        {
                            // 不结算伤害的那几发：只给命中点光爆
                            try
                            {
                                HitFeel.SpawnBurst(root, targetPos, 54f);
                            }
                            catch (Exception feelEx)
                            {
                                GD.PushWarning("[Subaru] hit burst failed: " + feelEx.Message);
                            }
                        }

                        Release(s);
                        shots.RemoveAt(i);
                    }
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] spell volley failed: " + e);
        }
        finally
        {
            foreach (var s in shots)
                Release(s);
        }

        GD.Print($"[Subaru] spell volley done: spawned={spawned}, damaged={damaged}, pooled={reusedCount}");
    }

    /// <summary>发起一次命中结算，不等待它完成（避免弹幕被伤害演出卡住）。</summary>
    private static void FireHit(PlayerChoiceContext ctx, CardModel card, CardPlay play,
                                Creature target, decimal perHit, int index)
    {
        _ = ApplyHitAsync(ctx, card, play, target, perHit, index);
    }

    private static async Task ApplyHitAsync(PlayerChoiceContext ctx, CardModel card, CardPlay play,
                                            Creature target, decimal perHit, int index)
    {
        try
        {
            // 每命中一段，依次播一段打击音效（比段数多时从头循环）
            Audio.MultiHitSfx.PlayNext();

            await DamageCmd.Attack(perHit)
                .FromCard(card, play)
                .Targeting(target)
                .Execute(ctx);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] spell hit #{index} failed: {e.Message}");
        }
    }


    /// <summary>
    /// 只播弹幕动画，不结算伤害（伤害仍按卡牌自己结算）。
    /// 返回的 Task 在**第一发命中**时完成 —— 调用方据此把伤害结算压到那一刻。
    /// </summary>
    public static Task? PlayVisualAndFirstHit(Player player, Creature? target, int hits, decimal perHit)
    {
        if (!Enabled || target == null)
            return null;

        var firstHit = new TaskCompletionSource<bool>();
        _ = RunVisualAsync(player, target, hits, perHit, firstHit);
        return firstHit.Task;
    }

    private static async Task RunVisualAsync(Player player, Creature target, int hits, decimal perHit,
                                             TaskCompletionSource<bool> firstHit)
    {
        try
        {
            await PlayAsyncCore(player, target, hits, perHit, null, null, null, firstHit);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell visual failed: " + e.Message);
        }
        finally
        {
            firstHit.TrySetResult(false);   // 兜底：没命中也别让调用方一直等
        }
    }

    private static Shot? Spawn(PackedScene scene, Node2D root, Vector2 anchor, float aim, double t, int idx,
        out bool reused)
    {
        reused = false;

        try
        {
            var shot = TakeFromPool();
            if (shot != null)
            {
                reused = true;
            }
            else
            {
                var inst = scene.Instantiate();
                if (inst is not Node2D node)
                {
                    inst.QueueFree();
                    return null;
                }

                shot = new Shot { Node = node };

                var visual = node.GetNodeOrNull<Node>("Visuals");
                if (visual != null)
                    shot.Sprite = new MegaSprite(visual);
                else
                    GD.PushWarning("[Subaru] spell volley: bullet has no Visuals child");
            }

            // 复位（池里出来的节点上一发可能停在任意位置/姿态）
            shot.Index = idx;
            shot.Spawn = anchor;
            shot.T0 = t;
            shot.Flying = false;
            shot.FlyT = 0f;
            shot.Fresh = true;

            shot.Node.Visible = true;
            shot.Node.Position = anchor;
            shot.Node.Rotation = aim;
            shot.Node.Scale = new Vector2(BulletScale, BulletScale);
            root.AddChild(shot.Node);

            PlayAnim(shot, "emerge");

            GD.Print($"[Subaru] spell shot #{idx} at {anchor} aim={Mathf.RadToDeg(aim):F1}deg" +
                     $"{(reused ? " (pooled)" : "")}");
            return shot;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell spawn failed: " + e.Message);
            return null;
        }
    }

    private static void PlayAnim(Shot shot, string name)
    {
        try
        {
            var state = shot.Sprite?.GetAnimationState();
            state?.SetAnimation(name, false, 0);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] spell anim failed: " + e.Message);
        }
    }


    /// <summary>
    /// 找到「角色可视根节点」（= subaru.tscn 的根，Node2D）。
    /// NCreature 本身不是 Node2D，它的子节点里有一个带 Visuals 子节点的 Node2D 就是我们要的坐标系。
    /// </summary>
    private static Node2D? FindVisualRoot(Node? creatureNode)
    {
        if (creatureNode == null)
            return null;

        foreach (var child in creatureNode.GetChildren())
        {
            if (child is Node2D n2 && n2.GetNodeOrNull<Node>("Visuals") != null)
                return n2;
        }

        // 兜底：往下找一层
        foreach (var child in creatureNode.GetChildren())
        {
            var found = FindVisualRoot(child);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>目标在子罗节点坐标系里的位置（取 CenterPos 标记，没有就抬高一点）。</summary>
    private static Vector2 LocalTarget(Node2D root, Creature? target)
    {
        var fallback = root.GlobalPosition + new Vector2(700, -120);
        if (target == null)
            return fallback;

        var cnode = target.GetCreatureNode();
        if (cnode == null || !GodotObject.IsInstanceValid(cnode))
            return fallback;

        var marker = cnode.GetNodeOrNull<Node2D>("CenterPos");
        if (marker != null)
            return root.ToLocal(marker.GlobalPosition);

        // 没有 CenterPos 标记：用它的可视根节点往上抬一点当作命中点
        var theirRoot = FindVisualRoot(cnode);
        var from = theirRoot != null ? theirRoot.GlobalPosition + new Vector2(0, -90)
                                     : root.GlobalPosition + new Vector2(700, -120);
        return root.ToLocal(from);
    }
}
