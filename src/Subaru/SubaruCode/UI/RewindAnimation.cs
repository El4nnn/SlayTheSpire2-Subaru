using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 死归演出：紫光 A（全程亮着）→ 死亡动画播到跪地 → 魔女剪影（频闪 + 音频）
/// → 紫闪 B + 音效 → 回待机。
///
/// 节奏（2026-11-13 定）：
///   挨打那一帧起紫光 A 就亮，**不淡出**，一直压着整段倒地；
///   die 播到「膝盖着地」（0.76 s）就切走，剩下的前扑、伏地全部砍掉；
///   魔女出现的同一帧紫光 A 才撤，所以是「紫光持续到魔女出现」而不是一闪而过。
///
/// timeScale 是 AnimationState 级的（GetAnimationState().SetTimeScale），
/// 跨 SetAnimation 依然生效，所以必须在切 idle_loop 前显式复位成 1。
///
/// 播放期间玩家操作全锁（CombatManager.PlayerActionsDisabled），
/// 否则这段演出里还能出牌，会和动画打架。
/// </summary>
public static class RewindAnimation
{
    private const string DieAnim = "die";
    private const string IdleAnim = "idle_loop";

    private const int Track = 0;

    /// <summary>
    /// die 播到「膝盖着地」就切走，这一刻在抽帧后的时间轴上 = 第 19 帧 = 0.76 s。
    /// （对应原 80 帧版的 f34：高度从 303 落到 271 之后开始平台）
    ///
    /// 2026-11-20 现实：**死亡动画素材还没做**，骨架里的 `die` 现在指回 idle（见
    /// `_anim/tools/build_spine_for_mod.py` 的 DIE_* 说明）。叫 die 只是一次无害的
    /// 重新播放，0.76 s 这个切点保留着 —— 它同时也是紫光 A 的蓄势时间。
    /// 素材做出来放 `_anim/frames/die/` 后自动接上，这里不用改。
    /// </summary>
    private const double KneeCutTime = 0.76;

    /// <summary>
    /// 黑幕停留时长的兜底值（秒）= 素材 1 的语音 1.384 s + 0.1 s 余量。
    /// 正常路径不用它：每条素材按自己语音的长度算，见 <see cref="FaceHoldPadding"/>。
    /// </summary>
    private const double FallbackFaceHold = 1.484;

    /// <summary>音频尾巴的余量：停留时长 = 语音长度 + 这个值，否则淡出会掐掉尾音。</summary>
    private const double FaceHoldPadding = 0.1;

    /// <summary>
    /// 播完整段死归演出。失败不抛异常，最坏情况就是不播动画。
    /// <paramref name="madnessLikely"/>：预测这次死归会不会触发癫狂（决定素材 5 是否参与 50/50）。
    /// </summary>
    public static async Task PlayAsync(Player player, bool madnessLikely = false)
    {
        CombatManager manager = null;
        var prevDisabled = false;

        // timeScale 是 AnimationState 级的，异常中断时若停在 8× 会把之后所有动画带快，
        // 所以把它提到 try 外面，在 finally 里统一复位。
        SpineAnimationAccess anim = default;
        var hasAnim = false;

        // 血条伪装必须在任何退出路径上撤掉，否则会一直停在「0 血 + 紫色」
        Action restoreHealth = null;
        var healthRestored = false;

        try
        {
            manager = CombatManager.Instance;
            if (manager != null)
            {
                prevDisabled = manager.PlayerActionsDisabled;
                manager.PlayerActionsDisabled = true;
            }

            // SpineAnimationAccess 是结构体（不是类），只能用 IsValid 判断
            var node = player?.Creature?.GetCreatureNode();
            if (node == null)
            {
                GD.Print("[Subaru] rewind animation skipped: no creature node");
                return;
            }

            anim = node.SpineAnimation;
            if (!anim.IsValid)
            {
                GD.Print("[Subaru] rewind animation skipped: no spine animation");
                return;
            }

            hasAnim = true;

            anim.SetTimeScale(1f);

            // 视觉上「血归零」：逻辑仍是 1 血，只是把血条和数字伪装成死了
            restoreHealth = RewindHealthMask.Apply(player.Creature);

            // 死亡动画只播到跪地。紫光 A 从挨打那一帧就已经亮着，全程不淡出。
            anim.SetAnimation(DieAnim, false, Track);

            // 用「紫光 A 已经亮了多久」来扣时间，保证切点对齐的是挨打那一刻，
            // 而不是进入这个协程的时刻（两者之间隔着几次 await）。
            var remain = KneeCutTime - RewindVisuals.ElapsedSinceDeathFlash;
            if (remain > 0)
                await WaitSeconds(remain);

            // 魔女出现的同一帧：紫光 A 退场 + 音频起播 + 黑幕淡入
            // 素材从池里等概率抽一条（图 + 语音成对），黑幕停多久由这条语音的长度决定
            RewindVisuals.EndDeathFlash(0.15);

            // 素材 5（视频）：只在预测到「这次死归会触发癫狂」时参与，50%；
            // 播不了（引擎缺 Theora / 文件没进 pck）就回落到图片池，不会黑屏卡住
            var videoSeconds = 0.0;
            if (RewindWitchPool.TryTakeVideo(madnessLikely, out var videoPath))
                videoSeconds = await RewindVisuals.PlayWitchVideoAsync(videoPath);

            if (videoSeconds <= 0)
            {
                var asset = RewindWitchPool.Pick();
                var audioSeconds = Subaru.SubaruCode.Audio.WitchFaceSfx.Play(asset.AudioPath);
                var hold = audioSeconds > 0 ? audioSeconds + FaceHoldPadding : FallbackFaceHold;
                GD.Print($"[Subaru] rewind witch hold {hold:0.###}s (audio {audioSeconds:0.###}s)");

                await RewindVisuals.BlackoutAsync(asset.FacePath, hold);
            }

            // 黑幕之后：紫闪 B + 音效（同一帧），回待机。
            // 血量遮罩先不撤——要等整段演出结束、角色站好之后再恢复，
            // 否则黑幕一淡掉就会看到「人还躺着，血条已经是 1」。
            RewindVisuals.PlayReviveFlash();
            Subaru.SubaruCode.Audio.RewindSfx.Play();

            anim.SetTimeScale(1f);
            anim.SetAnimation(IdleAnim, true, Track);

            // 最后把黑幕淡掉，露出已经站好的角色
            RewindVisuals.RevealFromBlackout(0.25);

            // 等黑幕淡完再恢复血条：整个死亡动画期间数字都是 0
            await WaitSeconds(0.35);
            restoreHealth?.Invoke();
            healthRestored = true;

            GD.Print("[Subaru] rewind animation played (flash A -> witch -> idle)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] rewind animation failed: " + e);
        }
        finally
        {
            try
            {
                if (manager != null)
                    RestoreActionLock(manager, player, prevDisabled);
            }
            catch
            {
                // 战斗已经结束、CombatManager 不可用时忽略
            }

            try
            {
                // 正常情况下切 idle 之前已经复位过，这里是异常/提前 return 的兜底
                if (hasAnim && anim.IsValid)
                    anim.SetTimeScale(1f);
            }
            catch
            {
                // 节点已销毁时忽略
            }

            try
            {
                // 中途抛异常时血条还停在「0 血 + 紫色」，必须撤掉
                if (!healthRestored)
                {
                    restoreHealth?.Invoke();
                    healthRestored = true;
                }
            }
            catch
            {
                // 已经离开战斗时忽略
            }
        }
    }

    /// <summary>用 SceneTreeTimer 等待，确保续体回到主线程（不要用 Task.Delay）。</summary>
    private static async Task WaitSeconds(double seconds)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        await tree.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>
    /// 演出收尾时的操作锁复位。
    ///
    /// 不能无条件写回 <c>prevDisabled</c>：致命一击那条路是在**敌方回合**里起播的，
    /// 那时 prevDisabled 必然是 true；演出却要跑 2.5–6 秒，这期间回合机已经切到玩家侧
    /// （StartTurn 会把 PlayerActionsDisabled 置 false）。此时再把 true 写回去，
    /// 玩家这一整回合就既打不出牌也喝不了药——NPlayerHand.AreCardActionsAllowed 和
    /// NPotionPopup.RefreshButtons 读的都是这一个开关，下回合开始才自愈。
    ///
    /// 所以按「此刻轮到谁」决定：玩家侧一律放开；还在敌方回合才还原旧值。
    /// </summary>
    private static void RestoreActionLock(CombatManager manager, Player player, bool prevDisabled)
    {
        var state = player?.Creature?.CombatState;
        if (state != null && state.CurrentSide == CombatSide.Player)
        {
            if (manager.PlayerActionsDisabled)
                GD.Print("[Subaru] rewind anim end: player side, actions re-enabled");

            manager.PlayerActionsDisabled = false;
            return;
        }

        manager.PlayerActionsDisabled = prevDisabled;
    }
}
