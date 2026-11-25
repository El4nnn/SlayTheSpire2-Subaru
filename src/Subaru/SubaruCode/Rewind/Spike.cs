using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Rewind;

public static class DeathReturnSpike
{
    private static BattleSnapshot? _snapshot;
    private static BattleSnapshot? _manual;
    private static bool _pending;
    private static bool _busy;
    private static int _madnessAppliedAtTurn = -1;
    private static int _lastDeathTurn = -1;

    /// <summary>
    /// 这一回合的敌方行动是否要整个跳过。
    ///
    /// <para>玩家在**自己回合内**被判定死亡时，我们会延迟 1 秒把回合交出去（让玩家看到倒下），
    /// 但这一回合马上要被还原 —— 让敌人真打一轮既拖时间，又会出现「尸体被敌人打」的画面。
    /// 置上之后 <see cref="Patches.SkipEnemyTurnPatch"/> 会让每个敌人的 <c>TakeTurn</c> 直接返回，
    /// 而 <c>EndEnemyTurn</c> 照常执行，回合机不会断链。</para>
    ///
    /// <para>清理点：玩家回合开始 / 战斗重置 / 抓快照（见 <see cref="OnPlayerTurnStart"/>、
    /// <see cref="ResetForCombat"/>）。</para>
    /// </summary>
    public static bool SkipEnemyTurn;

    private static bool _forceEndTurnPending;

    /// <summary>
    /// 「立即死归」按钮的防连点标志：从按下到死亡记账完成之间有效。
    ///
    /// <para>不能用 <see cref="_rewinding"/> 来做这件事 —— 它会让
    /// <see cref="CapLethalDamage"/> 直接放行致命伤害（第一行就有这个判断），
    /// 自伤就变成真伤害了。</para>
    /// </summary>
    private static bool _manualInFlight;

    /// <summary>死亡后延迟多久交回合（毫秒）。给玩家看紫闪和倒下。</summary>
    private const int ForceEndTurnDelayMs = 1000;

    /// <summary>
    /// 每次成功回档 +1。能力里那些「实例字段」的计数（例如斯芬克斯的每回合发牌次数、
    /// 未兑换的累计伤害）拿它对照，发现版本变了就把计数清回存档点的状态。
    /// </summary>
    public static int RewindVersion { get; private set; }

    /// <summary>同一回合里「又被打到致命」的次数，只用来给日志降噪。</summary>
    private static int _cappedAgainCount;

    // 死亡时没有 PlayerChoiceContext，无法安全施加能力：
    // 先记账，等下一个玩家回合开始（有 ctx）再一次性施加。
    private static int _pendingFragrance;

    // 每次死亡欠一张「魔女瘴气」。只在这里发，回合开始不再补——
    // 否则玩家把瘴气消耗掉后，下一回合又会被补回来。
    private static int _pendingCurses;

    // 死归奖励（力量/敏捷）在快照还原时会被抹掉，所以这里记住「已经累计发了多少」，
    // 每次回档后按 累计 - 存档点里已有 的差额补发，才能做到「死一次 +2、死两次 +4」。
    private static int _blessingAppliedStr;
    private static int _blessingAppliedDex;
    private static int _blessingInSnapshotStr;
    private static int _blessingInSnapshotDex;

    // 「荆棘王冠」按「3 + 2 × 死归次数」累计，回档会把荆棘层数还原成存档点里的值，
    // 所以这里记住存档点那一刻已经死过几次，回档后只补「这之后新长出来的」那部分。
    private static int _deathsInSnapshot;

    /// <summary>存档点时的「造成过伤害次数」，回档要退回到这个值。</summary>
    private static int _damageGivenInSnapshot;

    /// <summary>存档点时的「本场打出过的[阴魔法]张数」，回档要退回到这个值（封魔石用）。</summary>
    private static int _yinInSnapshot;
    /// <summary>快照那刻「本场打出的技能牌数」（阿尔·幕的斩杀线，死归要退回）。</summary>
    private static int _skillsPlayedInSnapshot;

    /// <summary>
    /// 这次死亡已经把死归演出提前播过了（致命一击结算完后立刻播）。
    /// 真回档发生在玩家回合开始，那时看到这个标记就不再重播一遍。
    /// </summary>
    private static bool _rewindAnimPlayedEarly;

    /// <summary>非主线程（求解器模拟）里跳过截断时的日志，只打一次，免得刷屏。</summary>
    private static bool _loggedSimulatedLethal;

    /// <summary>
    /// 存档点那一刻「本回合已出过几张牌」（战斗历史口径）。
    /// 仪式兽耳鸣的放行判定读的就是这个数，回档后要裁回它 —— 见
    /// <see cref="Subaru.SubaruCode.Patches.CeremonialRingingRollback"/>。
    /// </summary>
    private static int _playsThisTurnInSnapshot;

    /// <summary>存档点那一刻玩家身上有没有耳鸣。</summary>
    private static bool _ringingInSnapshot;

    public static int DeathCount { get; private set; }

    /// <summary>战斗开始时保底的傲慢层数（由初始遗物设定，升级版为 4）。</summary>
    public static int MinPride { get; set; } = 3;

    static DeathReturnSpike()
    {
        RewindLedger.Register<MadnessPower>();
        RewindLedger.Register<PridePower>();
        RewindLedger.Register<WitchFragrancePower>();
        RewindLedger.Register<LostSavepointPower>();



        // 「腐蚀」能力自动登记（傲慢司教的加护、强欲的契约等）
        Corrosion.RegisterAll();
    }

    public static void ResetForCombat()
    {
        var previousDeaths = DeathCount;
        var previousBlessing = $"{_blessingAppliedStr}/{_blessingAppliedDex}";
        var previousFragrance = _pendingFragrance;

        _snapshot = null;
        _manual = null;
        _pending = false;
        _busy = false;
        _rewinding = false;
        DeathCount = 0;
        _pendingFragrance = 0;
        _pendingCurses = 0;
        _blessingAppliedStr = 0;
        _blessingAppliedDex = 0;
        _blessingInSnapshotStr = 0;
        _blessingInSnapshotDex = 0;
        _deathsInSnapshot = 0;
        _lastDeathTurn = -1;
        _madnessAppliedAtTurn = -1;
        _rewindAnimPlayedEarly = false;
        _earlyAnimTask = null;
        SkipEnemyTurn = false;
        _forceEndTurnPending = false;
        TurnStartQueue.Reset();
        CombatStats.Reset();
        CurseRemoval.Reset();
        RelicCounterSnapshot.Clear();
        RelicStateSnapshot.Clear();
        CorrodedCards.Reset();
        CorLeonisTurnLedger.Reset();
        YinLedger.Reset();
        RewindPersistentCards.Reset();

        GD.Print($"[Subaru] rewind state reset (previous deathCount={previousDeaths}, "
                 + $"blessing={previousBlessing}, pendingFragrance={previousFragrance})");
    }

    public static void CaptureSnapshot(ICombatState state)
    {
        if (_busy)
            return;

        // 多人里没有回档：快照一拍就没用了，白白花几十毫秒（还在主线程）。
        if (!MultiplayerSupport.RewindEnabled)
            return;

        try
        {
            _snapshot = BattleSnapshot.Capture(state);
            RelicCounterSnapshot.Capture(state.Players?.FirstOrDefault());
            // 遗物自身的字段（一场一次 / 本回合计数）也要记一份，回档时写回去
            RelicStateSnapshot.Capture(state.Players?.FirstOrDefault());
            // 战斗开始的快照里没有任何死归奖励
            _blessingInSnapshotStr = 0;
            _blessingInSnapshotDex = 0;
            _deathsInSnapshot = 0;
            // 「造成过伤害次数」也跟着存档点走（过路魔·塞蕾丝缇雅的 N）
            _damageGivenInSnapshot = CombatStats.DamageGiven;
            // 「本场打出过的[阴魔法]张数」同样跟着存档点走（封魔石）
            _yinInSnapshot = YinLedger.Count;
            _skillsPlayedInSnapshot = CombatStats.SkillsPlayed;
            // 耳鸣回滚要的两个数（仪式兽）：本回合已出牌数 + 有没有耳鸣
            _playsThisTurnInSnapshot =
                Subaru.SubaruCode.Patches.CeremonialRingingRollback.CountCardPlaysThisTurn(
                    state, state.Players?.FirstOrDefault());
            _ringingInSnapshot =
                Subaru.SubaruCode.Patches.CeremonialRingingRollback.HasRinging(
                    state.Players?.FirstOrDefault());
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] capture snapshot failed: " + e);
        }
    }

    /// <summary>
    /// 当前存档点里有没有这张牌（实例）。
    ///
    /// <para>给 <see cref="PlayedPowerCards.Record"/> 用：判断打出的这张能力牌
    /// 是不是「存档点里本来就有」。药水/幼女使者这类中途生成的牌不在快照里，
    /// 死归时就不该补回手牌。</para>
    ///
    /// <para>没有存档点时按「有」处理 —— 保持旧行为，不让极早期的情况变样。</para>
    /// </summary>
    public static bool SavepointContainsCard(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        var snapshot = _manual ?? _snapshot;
        if (snapshot == null)
            return true;

        return snapshot.ContainsCard(card);
    }

    public static void SetManualSavepoint(ICombatState state)
    {
        try
        {
            _manual = BattleSnapshot.Capture(state);
            RelicCounterSnapshot.Capture(state.Players?.FirstOrDefault());
            RelicStateSnapshot.Capture(state.Players?.FirstOrDefault());
            // 这个存档点是「此刻」，此刻已经发过的死归奖励也在力量/敏捷里，
            // 回档后不能重复补发。
            _blessingInSnapshotStr = _blessingAppliedStr;
            _blessingInSnapshotDex = _blessingAppliedDex;
            _deathsInSnapshot = DeathCount;
            _damageGivenInSnapshot = CombatStats.DamageGiven;
            _yinInSnapshot = YinLedger.Count;
            _skillsPlayedInSnapshot = CombatStats.SkillsPlayed;
            _playsThisTurnInSnapshot =
                Subaru.SubaruCode.Patches.CeremonialRingingRollback.CountCardPlaysThisTurn(
                    state, state.Players?.FirstOrDefault());
            _ringingInSnapshot =
                Subaru.SubaruCode.Patches.CeremonialRingingRollback.HasRinging(
                    state.Players?.FirstOrDefault());
            GD.Print($"[Subaru] manual savepoint captured (blessing in snapshot: "
                     + $"{_blessingInSnapshotStr} str / {_blessingInSnapshotDex} dex)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] manual savepoint failed: " + e);
        }
    }

    /// <summary>
    /// 正在执行死归演出/回档。必须从「进入死归」那一刻就置位：
    /// _busy 是在快照还原那一步才置位的，而演出要好几秒，
    /// 这中间的连点会同时跑好几套回档，把牌堆和回合状态搅坏（表现为角色无法行动）。
    /// </summary>
    private static bool _rewinding;

    /// <summary>
    /// 致命一击当帧起播的那段演出（在敌方回合里跑）。回档收尾要等它跑完再放开操作，
    /// 否则演出结束时的收尾会和回档的收尾互相覆盖 PlayerActionsDisabled。
    /// </summary>
    private static Task? _earlyAnimTask;

    /// <summary>等提前播出的那段演出结束（最多 20 秒，防极端情况下卡住回合开始）。</summary>
    private static async Task WaitEarlyAnimAsync()
    {
        var task = _earlyAnimTask;
        _earlyAnimTask = null;

        if (task == null || task.IsCompleted)
            return;

        GD.Print("[Subaru] rewind restore: waiting for early anim to finish");

        try
        {
            var finished = await Task.WhenAny(task, Task.Delay(20000));
            if (finished != task)
                GD.PushWarning("[Subaru] early anim wait timed out");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] early anim wait failed: " + e.Message);
        }
    }

    public static bool IsRewinding => _rewinding;

    /// <summary>
    /// 当前是否处在「为了死归而强制结束回合」这一段里。
    ///
    /// <para><c>ScheduleForceEndTurn</c> 排定时置 true（<c>Spike.cs:748</c>），
    /// 一直到回档还原开始才清掉（<c>:810</c>）——正好覆盖<b>这一次</b>被强迫的回合结束。
    /// 给回合结束相位里的「要不要弹选牌框」判断用：这一回合马上就要被抹掉，
    /// 任何选牌都没有意义（2026-11-23 用户报「点立即死归还要求选 2 张保留」）。</para>
    /// </summary>
    public static bool IsForcedEndTurn => _forceEndTurnPending;

    /// <summary>
    /// 现在允许死归吗？只有「战斗进行中」才允许。
    ///
    /// 事件 / 地图 / 商店里掉的命是真死，不触发死归；放弃一局同样不算
    /// （放弃流程会先标记 IsAbandoned，再直接杀玩家）。所有入口都先过这一关。
    /// </summary>
    public static bool RewindAllowedNow()
    {
        try
        {
            var combat = CombatManager.Instance;
            if (combat == null || !combat.IsInProgress || combat.IsOverOrEnding)
                return false;

            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            #if GAME_BETA
        if (run != null && (run.IsAbandoned || run.HasRunEnded || run.IsCleaningUp || run.IsGameOver))
#else
        if (run != null && (run.IsAbandoned || run.IsCleaningUp || run.IsGameOver))
#endif
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>致命伤害截断；每回合只结算一次死亡。</summary>
    public static decimal CapLethalDamage(
        Creature target,
        decimal amount,
        ValueProp props = default,
        Creature? dealer = null,
        CardModel? card = null)
    {
        // _busy：正在还原/演出，别记账（避免重入）
        // _rewinding && !_pending：正在回档且这一回合还没记过死亡 → 放行
        // _rewinding &&  _pending：这一回合**已经记过死亡**，正在等回合开始去还原。
        //   这段窗口里游戏还在跑（回合末瘴气、毒、敌方出手），如果放行，1 血玩家会被
        //   真打死 —— 2026-11-21 实测「按完按钮等的那 1 秒里被瘴气压死」。
        if (_busy || (_rewinding && !_pending) || !target.IsPlayer)
            return amount;

        // 求解器（CombatSolver 等）在线程池线程里模拟时也会走到这里（走 Mirror 的 ModifyHpLost）。
        // 那种调用必须当只读：真记一次账会把死亡数/遗香/欠诅咒全算歪，
        // 而且后面 PlayDeathFlash 会在非主线程 AddChild，Godot 直接报错。
        // 真战斗的伤害都在主线程结算，所以这里按线程分就够了。
        if (!MainThreadGuard.IsMainThread)
        {
            if (!_loggedSimulatedLethal)
            {
                _loggedSimulatedLethal = true;
                GD.Print("[Subaru] lethal cap skipped: off main thread (combat simulation)");
            }

            return amount;
        }

        // 多人：致命伤害截断等于单方面改伤害结算，两端必然分叉 —— 直接放行原始伤害。
        // 判据与说明见 MultiplayerSupport。
        if (MultiplayerSupport.BlockRewind())
            return amount;

        if (!RewindAllowedNow())
        {
            GD.Print("[Subaru] lethal not capped: not in combat (event death)");
            return amount;
        }

        if (target.HasPower<MadnessPower>())
        {
            GD.Print("[Subaru] madness active: rewind disabled");
            return amount;
        }

        if (_snapshot == null && _manual == null)
            return amount;

        var hp = target.CurrentHp;
        if (amount < hp)
            return amount;

        // 缓冲（Buffer）会把这次掉血整个吃掉，吃掉的量是 0 —— 那就不是「死」。
        // 原版的 BufferPower 挂在 ModifyHpLostAfterOstyLate（phase 8），比我们晚一步，
        // 所以必须在这里先放行，否则会出现「一点血没掉、却被记一次死亡并回档」。
        //
        // 例外：魔女瘴气的回合末扣血**不吃缓冲**（Patches.MiasmaBleed），
        // 那种情况缓冲救不了人，必须照常判定致命 —— 否则 1 血玩家被瘴气打死不会死归。
        var buffer = target.Powers.FirstOrDefault(p => p is BufferPower);
        if (buffer != null && buffer.Amount > 0 && !Patches.MiasmaBleed.Active)
        {
            GD.Print($"[Subaru] lethal not capped: 缓冲 x{buffer.Amount} 会吃掉这一次 "
                     + $"(hp={hp} amount={amount} dealer={dealer?.Name ?? "-"})");
            return amount;
        }

        // 滑溜（Slippery）会把这次掉血压到 1 点 —— 和缓冲是同一类问题，但它挂在
        // **和我们相同的** ModifyHpLostAfterOsty 相位，谁先谁后取决于模型顺序。
        // 如果我们在它前面跑，就会看到「原始致命伤害」而误记一次死亡：
        // 滑溜白救一次，还多做一次死归、多欠 20 遗香和 1 张瘴气。
        //
        // 例外①：不可被抵挡的伤害绕开滑溜（见 UnblockablePiercePatch），那种照常记死亡。
        // 例外②：魔女瘴气的扣血同样不吃滑溜（Patches.MiasmaBleed），也照常记死亡。
        if (Patches.UnblockablePierce.IsPiercing(props, dealer) || Patches.MiasmaBleed.Active)
        {
            // 不可被抵挡 / 瘴气扣血：滑溜不生效，继续往下走正常的致命判定
        }
        else
        {
            var slippery = target.Powers.FirstOrDefault(p => p is SlipperyPower);
            if (slippery != null && slippery.Amount > 0)
            {
                GD.Print($"[Subaru] lethal not capped: 滑溜 x{slippery.Amount} 会压到 1 点 "
                         + $"(hp={hp} amount={amount} props={props} dealer={dealer?.Name ?? "-"})");
                return amount;
            }
        }

        _pending = true;

        var player = target.Player;
        var state = target.CombatState;
        var turn = player?.PlayerCombatState?.TurnNumber ?? -1;

        if (turn != _lastDeathTurn)
        {
            _lastDeathTurn = turn;
            _cappedAgainCount = 0;
            DeathCount++;
            RewindProgress.Add();

            _pendingFragrance += 20;   // 权能10
            _pendingCurses += 1;       // 每次死亡欠一张「魔女瘴气」

            // 视觉反馈只在这一回合第一次致命时给。
            // 挨了第二下致命又建一层紫光的话，没人会再把它撤掉 → 整屏一直是紫的。
            Subaru.SubaruCode.UI.RewindVisuals.PlayDeathFlash(target);

            // 这一击结算完就把死归演出播出来（不等下回合开始）——观感上和按「立即死归」一致。
            // 只播视觉，真正的回档仍然留到玩家回合开始做。
            ScheduleEarlyRewindAnimation(player);

            // 诊断：把判定依据打全，否则「我算过打不死」这类反馈没法对账。
            // props / dealer / card 能直接指出这一下是谁打的、是不是不可格挡的自伤。
            GD.Print($"[Subaru] lethal capped (death #{DeathCount}), hp={hp} amount={amount} "
                     + $"props={props} dealer={dealer?.Name ?? "-"} card={card?.Id.ToString() ?? "-"}, "
                     + $"pending fragrance={_pendingFragrance}, pendingCurses={_pendingCurses}, "
                     + $"powers=[{string.Join(",", target.Powers.Select(p => p.GetType().Name + "x" + p.Amount))}]");

            if (player != null && state != null)
                RewindLedger.Capture(player);

            // 玩家回合内被判死：延迟 1 秒把回合交出去（这一回合敌人的 TakeTurn 会被跳过），
            // 让玩家看到紫闪 + 倒下，然后回合机自己走到下一个玩家回合开始再还原。
            ScheduleForceEndTurn(player);
        }
        else
        {
            // 同回合的重复致命：玩家只剩 1 血，之后每一滴伤害都会再走到这里，
            // 一条一条打会刷几十行。只留第 1 条，之后每 10 条汇总一次。
            _cappedAgainCount++;
            if (_cappedAgainCount == 1 || _cappedAgainCount % 10 == 0)
            {
                GD.Print($"[Subaru] lethal capped again this turn x{_cappedAgainCount}: "
                         + $"hp={hp} amount={amount} props={props} dealer={dealer?.Name ?? "-"} "
                         + $"card={card?.Id.ToString() ?? "-"}");
            }
        }

        return Math.Max(0, hp - 1);
    }

    /// <summary>
    /// 致命一击结算完立刻播死归演出。
    ///
    /// 用 CallDeferred + 等两帧：让这次伤害指令的收尾（扣血飘字、敌人攻击动画）先跑完，
    /// 否则演出的 die 动画会和敌人这一下攻击抢同一个 Spine 轨道。
    /// </summary>
    private static void ScheduleEarlyRewindAnimation(Player player)
    {
        if (_rewindAnimPlayedEarly || player == null)
            return;

        // 双保险：CapLethalDamage 已经挡掉非主线程了，这里再挡一次，
        // 因为 CallDeferred 的目标是 Godot 节点，非主线程排进去一样会炸。
        if (!MainThreadGuard.IsMainThread)
            return;

        _rewindAnimPlayedEarly = true;

        try
        {
            Callable.From(() => { _earlyAnimTask = PlayEarlyRewindAsync(player); }).CallDeferred();
        }
        catch (Exception e)
        {
            _rewindAnimPlayedEarly = false;
            GD.PushWarning("[Subaru] schedule early rewind anim failed: " + e.Message);
        }
    }

    private static async Task PlayEarlyRewindAsync(Player player)
    {
        try
        {
            // 等回档还欠着才播；已经回完了就什么都不做
            if (!_pending)
                return;

            if (Engine.GetMainLoop() is SceneTree tree)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            await Subaru.SubaruCode.UI.RewindAnimation.PlayAsync(player, PredictMadnessOnRestore(player));
            GD.Print("[Subaru] rewind animation played early (on lethal hit)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] early rewind anim failed: " + e);
        }
    }

    private static async Task AddCurse(ICombatState state, Player player)
    {
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            var card = state.CreateCard<CurseWitchMiasma>(player);
            if (card != null)
            {
                await CardPileCmd.Add(card, pcs.Hand, CardPilePosition.Top, null, false);
                GD.Print("[Subaru] curse added");
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] add curse failed: " + e);
        }
    }

    /// <summary>
    /// 沙坑闭合这类「回合中途直接判死」的拦截：只记账，**不当场回档**。
    ///
    /// 为什么不当场回：这一刻人还在敌方回合的行动链里（沙坑是在敌方回合开始刷层数的），
    /// 当场起死归会和回合机的「切边/开新回合」并行跑，最后牌堆被两边各写一遍，
    /// 结果就是回档完手牌在、能量在，但一张也打不出去（PlayerActionsDisabled 卡住）。
    /// 所以交给 OnPlayerTurnStart 的 _pending 机制：等回合机自己走到玩家回合，再一次性还原。
    /// </summary>
    public static void SchedulePendingDeath(Player player)
    {
        try
        {
            if (player == null)
                return;

            if (!RewindAllowedNow())
            {
                GD.Print("[Subaru] pending rewind rejected: not in combat");
                return;
            }

            if (player.Creature != null && player.Creature.HasPower<MadnessPower>())
            {
                GD.Print("[Subaru] pending rewind rejected: madness active");
                return;
            }

            if (_snapshot == null && _manual == null)
                return;

            _pending = true;
            Subaru.SubaruCode.UI.RewindVisuals.PlayDeathFlash(player.Creature);

            var turn = player.PlayerCombatState?.TurnNumber ?? -1;
            if (turn == _lastDeathTurn)
            {
                GD.Print($"[Subaru] pending rewind already counted this turn (#{DeathCount})");
                return;
            }

            _lastDeathTurn = turn;
            DeathCount++;
            RewindProgress.Add();
            _pendingFragrance += 20;
            _pendingCurses += 1;
            RewindLedger.Capture(player);

            GD.Print($"[Subaru] death scheduled (death #{DeathCount}), rewind at next turn start");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] schedule pending death failed: " + e);
        }
    }

    /// <summary>「立即死归」按钮用：不等致命伤害，直接按一次死亡结算并回档。</summary>
    public static async Task ForceRewindAsync(ICombatState state)
    {
        try
        {
            var player = state?.Players?.FirstOrDefault();
            if (player == null)
                return;

            // 多人里没有「立即死归」按钮，这里再兜一次（手动调用 / 读档残留）。
            if (MultiplayerSupport.BlockRewind())
                return;

            if (_busy || _rewinding || _manualInFlight)
            {
                GD.Print("[Subaru] manual rewind ignored: busy/rewinding");
                return;
            }

            if (!RewindAllowedNow())
            {
                GD.Print("[Subaru] manual rewind ignored: not in combat");
                return;
            }

            if (_snapshot == null && _manual == null)
            {
                GD.Print("[Subaru] manual rewind ignored: no savepoint");
                return;
            }

            // 癫狂期间禁止死归（和致命伤害那条路的规则保持一致，否则会跳过回合）
            if (player.Creature != null && player.Creature.HasPower<MadnessPower>())
            {
                GD.Print("[Subaru] manual rewind rejected: madness active");
                return;
            }

            // 选牌界面挂着时禁止死归：那个 await 引擎没有办法取消（PlayerChoiceContext 只有
            // 开始/结束信号，没有 Cancel），玩家选完之后药水 / 幼女使者会把牌加进**还原后**的
            // 手牌 —— 2026-11-21 实测的「先不选、点死归、再选就白赚一张」。
            // 原版对「选牌时按结束回合」也是让选择优先（CardSelectCmd 会撤销已按下的结束回合）。
            if (Subaru.SubaruCode.UI.RewindButtonFollow.IsCardSelectionOpen())
            {
                GD.Print("[Subaru] manual rewind rejected: card selection in progress");
                return;
            }

            if (_pending)
            {
                GD.Print("[Subaru] manual rewind ignored: death already pending");
                return;
            }

            // 从这一刻起锁住按钮：自伤 → 交回合 → 还原，中间被连点会并行起两套流程。
            // 解锁发生在 OnPlayerTurnStart（还原结束），失败路径会自己解开。
            //
            // 注意这里**不能**置 _rewinding：CapLethalDamage 的第一行就是
            // `if (_busy || _rewinding || !IsPlayer) return amount;` —— 置了它，
            // 下面这次自伤就不会被截断，1 血角色会真的被打死（2026-11-21 实测）。
            // 所以自伤期间用 _manualInFlight 防连点，等死亡记账成功之后再置 _rewinding。
            _manualInFlight = true;

            try
            {
                // 和自然死亡完全同一条路：先对自己造成一次「血量上限」的伤害。
                //
                // 走的是伤害管线（不是直接改 HP），所以引擎自己的致命判定、
                // CapLethalDamage 的压血 / 计数 / 紫闪 / 倒下演出全都会照跑。
                // 伤害包在魔女瘴气的作用域里（ValueProp.Unblockable + MiasmaBleed），
                // 这样缓冲 / 滑溜都挡不住它 —— 否则玩家身上有缓冲时按钮会「按了没反应」。
                await SelfDamageForRewindAsync(player);

                // 兜底：减伤 / 伤害上限可能让它打不死。按钮必须永远有效。
                if (!_pending)
                {
                    GD.Print("[Subaru] manual self-damage did not kill, falling back to pending death");
                    SchedulePendingDeath(player);
                }

                if (!_pending)
                {
                    GD.Print("[Subaru] manual rewind failed: could not schedule death");
                    _rewinding = false;
                    return;
                }

                // 死亡已经记账：从这里开始锁到还原结束（OnPlayerTurnStart 解开）。
                _rewinding = true;

                GD.Print("[Subaru] manual rewind requested (death #{0}), restore at next turn start", DeathCount);

                // 1 秒后交回合（敌人这一回合的行动会被跳过），回合机走到下个玩家回合开始再还原。
                ScheduleForceEndTurn(player);
            }
            finally
            {
                _manualInFlight = false;
            }
        }
        catch (Exception e)
        {
            _rewinding = false;
            GD.PushError("[Subaru] manual rewind failed: " + e);
        }
    }

    /// <summary>
    /// 按钮用：对自己造成一次「血量上限」的伤害。
    ///
    /// <para>和魔女瘴气走同一条路径：<c>ValueProp.Unblockable</c> 穿格挡，
    /// <see cref="Patches.MiasmaBleed"/> 作用域让缓冲 / 滑溜都让路。
    /// 真正的判定、压血、记账、演出都在 <see cref="CapLethalDamage"/> 里。</para>
    /// </summary>
    private static async Task SelfDamageForRewindAsync(Player player)
    {
        var creature = player?.Creature;
        if (creature == null)
            return;

        var amount = Math.Max(1, creature.MaxHp);

        try
        {
            var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();

            using (Patches.MiasmaBleed.Enter())
            {
                await CreatureCmd.Damage(ctx, new[] { creature }, amount, ValueProp.Unblockable, creature);
            }

            GD.Print($"[Subaru] manual self-damage: {amount} unblockable -> hp={creature.CurrentHp}, "
                     + $"pending={_pending}");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] manual self-damage failed: " + e);
        }
    }

    /// <summary>
    /// 延迟 <see cref="ForceEndTurnDelayMs"/> 毫秒后，等手牌空闲再交回合。
    ///
    /// <para>为什么要延迟：<see cref="CapLethalDamage"/> 是在伤害结算链里被调用的，
    /// 当场交回合会和正在进行的牌/攻击 await 打架（软锁）。1 秒是给玩家看紫闪和倒下。</para>
    ///
    /// <para>为什么要等空闲：多段伤害的最后一下判死时，牌还在结算。</para>
    /// </summary>
    private static void ScheduleForceEndTurn(Player player)
    {
        if (player == null || _forceEndTurnPending)
            return;

        _forceEndTurnPending = true;
        GD.Print($"[Subaru] force end turn: queued in {ForceEndTurnDelayMs / 1000.0:F1}s");
        _ = ForceEndTurnAsync(player);
    }

    private static async Task ForceEndTurnAsync(Player player)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null)
                return;

            var deadline = Time.GetTicksMsec() + (ulong)ForceEndTurnDelayMs;
            while (Time.GetTicksMsec() < deadline && tree.Root != null)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            // 等空闲最多 10 秒，超过就放弃（宁可这一回合不交，也别在结算中间硬切）。
            for (var i = 0; i < 600; i++)
            {
                if (tree.Root == null)
                    return;

                var manager = CombatManager.Instance;
                var state = player.Creature?.CombatState;
                if (manager == null || state == null)
                    return;

                if (manager.IsOverOrEnding || manager.IsAboutToLose)
                {
                    GD.Print("[Subaru] force end turn: skipped (combat ending)");
                    return;
                }

                if (state.CurrentSide != CombatSide.Player
                    || manager.IsEnemyTurnStarted
                    || manager.EndingPlayerTurnPhaseOne
                    || manager.EndingPlayerTurnPhaseTwo)
                {
                    GD.Print("[Subaru] force end turn: skipped (not player side anymore)");
                    return;
                }

                if (!IsHandBusy())
                {
                    SkipEnemyTurn = true;
                    manager.SetReadyToEndTurn(player, false, null);
                    GD.Print("[Subaru] force end turn: SetReadyToEndTurn(canBackOut=false)");
                    return;
                }

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            GD.Print("[Subaru] force end turn: gave up waiting for idle");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] force end turn failed: " + e.Message);
        }
        finally
        {
            _forceEndTurnPending = false;
        }
    }

    /// <summary>手牌是否正在出牌（这时不能交回合）。</summary>
    private static bool IsHandBusy()
    {
        try
        {
            return MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.Ui?.Hand?.InCardPlay == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 现在是否还欠一次回档（致命已记账、但还没执行）。
    /// 供「回合开始流程末尾补回档」判断用。
    /// </summary>
    public static bool HasPendingRewind => _pending && !_busy && !_rewinding;

    /// <summary>
    /// 回合开始流程**末尾**的补回档。
    ///
    /// <para><b>补的是什么</b>：致命伤害如果恰好落在「回合开始流程内部」
    /// （<c>AfterPlayerTurnStartLate</c> 之后、<c>AfterSideTurnStart</c> 一带 ——
    /// 中毒、回合开始的持续伤害都属于这一段），那么 <see cref="OnPlayerTurnStart"/> 里那句
    /// <c>if (_pending)</c> 早就判过了，这一次死亡会被漏到**下一个**回合开始才执行。</para>
    ///
    /// <para><b>漏掉的后果</b>（玩家 2026-11-18 反馈）：致命那一刻只播演出、血被压到 1，
    /// 牌堆状态没回来 → 玩家看到「应该死归却没收」；随后还得带着 1 血把整个回合打完
    /// （期间每滴伤害都再判一次致命），下一回合开始才回档 → 观感变成「然后一直死归」。</para>
    ///
    /// <para><b>为什么在这里补是安全的</b>：这个时刻回合机已经把「玩家回合开始」走完、
    /// 还没把操作权交给玩家，不在敌方行动链里，重写牌堆不会和回合切边打架。</para>
    /// </summary>
    public static async Task FlushPendingRewindAsync(ICombatState state, Player player)
    {
        if (!HasPendingRewind)
            return;

        if (state == null || player == null)
            return;

        if (_snapshot == null && _manual == null)
            return;

        try
        {
            _rewinding = true;
            GD.Print("[Subaru] pending rewind flushed at end of turn start (same turn)");

            var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
            await RestoreToSavepointAsync(ctx, state, player);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] flush pending rewind failed: " + e);
        }
        finally
        {
            _rewinding = false;
        }
    }

    /// <summary>把当前战斗恢复到存档点（死归的核心流程，供回合开始与手动按钮共用）。</summary>
    private static async Task RestoreToSavepointAsync(
        PlayerChoiceContext ctx, ICombatState state, Player player)
    {
        _pending = false;

        // 静默期开始：直到玩家下一次回合开始，沙坑的一切演出/补间都跳过。
        // 否则「还原时删掉存档点里没有的沙坑实例」会触发 AfterRemoved 的吞人收尾演出。
        Subaru.SubaruCode.Patches.SandpitGuard.Silent = true;

            // 死归演出：3× 播死亡 → 3× 倒放 → 回待机，这段期间玩家操作全锁。
            // 放在快照还原之前：此时人还在 1 血，观感上是「倒下又站起来」，然后才回档。
            // 致命一击那条路已经在受击当帧提前播过了，这里不再重播。
            if (!_rewindAnimPlayedEarly)
                await Subaru.SubaruCode.UI.RewindAnimation.PlayAsync(player, PredictMadnessOnRestore(player));
            else
                GD.Print("[Subaru] rewind anim already played on lethal hit (skip replay)");

            _rewindAnimPlayedEarly = false;

            // 只保留「内存快照」这一条路径（官方 SL 已停用，便于后续改造）
            var targetSnapshot = _manual ?? _snapshot;

            // 用的是「战斗开始」那张快照吗？它拍在 AfterSideTurnStart **之前**
            // （见下方 ReplayAfterSideTurnStartAsync 的注释），还原后要补跑那一段。
            // 手动存档点是回合中途拍的，已经包含这些效果，不能补。
            var usedInitialSnapshot = _manual == null && targetSnapshot != null;
            if (targetSnapshot != null)
            {
                _busy = true;
                try
                {
                    await targetSnapshot.RestoreCreatures(ctx, state);
                    await targetSnapshot.RestoreRngAndPotions(state);
                    await targetSnapshot.RestorePiles(ctx, state);

                    // 牌堆只回「哪张牌在哪」，还要把每张牌身上的附着/临时关键词回滚
                    targetSnapshot.RestoreCardStates();

                    // 状态锁链：它的计数在战斗历史里，不在能力字段上
                    targetSnapshot.RestoreChainsState(state);

                    // 能量 / 星星不在牌堆里，必须单独回档
                    targetSnapshot.RestoreResources();

                    // 遗物计数器（阳剑第几回合之类）也要跟着回去
                    RelicCounterSnapshot.Restore(player);

                    // 遗物实例字段（一场一次 / 本回合计数）回到存档点的值。
                    // 必须排在下面「补跑 AfterSideTurnStart」之前：那些在该钩子里自增的计数
                    // （开心小花 _turnsSeen）要以存档点的值为起点重新数一遍。
                    RelicStateSnapshot.Restore(player);

                    // 初始快照拍在「第一回合 AfterSideTurnStart 之前」，那一段要补跑一次
                    if (usedInitialSnapshot)
                        await ReplayAfterSideTurnStartAsync(state, player);

                    // 「只在战斗第一回合触发一次」的遗物（赌徒筹码）：它的钩子跑在快照**之前**，
                    // 快照里已经带着它那一次的结果，所以引擎回档后不会再触发。
                    // 但玩家的预期是「回到回合开始 = 那次选择重新来一遍」，而且原版的
                    // GamblingChip 是无状态、只靠 TurnNumber == 1 判断的，补跑一次正好对上。
                    if (usedInitialSnapshot)
                        await ReplayFirstTurnRelicsAsync(state, player, ctx);

        // 能力牌打出后会从战斗牌里消失，快照恢复不回来 —— 单独补一次
        try
        {
            await PlayedPowerCards.RestoreAsync(ctx, state, player);
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] restore power cards failed: " + e);
        }
                    await RewindLedger.Reapply(ctx, player);

                    // 贤者·夏乌拉：本场第一次打出的判定受死归重置（卡表备注）
                    CombatStats.ResetShaula();

                    // 柯尔·雷欧尼斯「每回合只能打 1 张」：回合号会被还原成 1，
                    // 但打出的记录不能跟着回档（否则第 1 回合打过的牌回档后永远打不出去）
                    CorLeonisTurnLedger.Reset();

                    // 「造成过伤害次数」退回存档点时的值（过路魔·塞蕾丝缇雅的 N）
                    CombatStats.SetDamageGiven(_damageGivenInSnapshot);

                    // 「本场打出过的[阴魔法]张数」也退回存档点（封魔石按它削力量）
                    YinLedger.SetCount(_yinInSnapshot);
                    CombatStats.SetSkillsPlayed(_skillsPlayedInSnapshot);

                    // 仪式兽「耳鸣」回滚：把回档点之后那段被打回的时间线的出牌记录裁掉，
                    // 否则耳鸣的放行判定会认为「本回合已出过牌」，带耳鸣的牌全锁死。
                    try
                    {
                        await Subaru.SubaruCode.Patches.CeremonialRingingRollback.ApplyAsync(
                            ctx, state, player, _playsThisTurnInSnapshot, _ringingInSnapshot);
                    }
                    catch (Exception e)
                    {
                        GD.PushWarning("[Subaru] ringing rollback failed: " + e.Message);
                    }

                    // [腐蚀]：本场已经打过的腐蚀牌，死归后从战斗里移除
                    await CorrodedCards.Purge(player);

                    // 契约·贝蒂：存档点里已经有贝蒂 → 打击要再变一次迷尼亚
                    await Cards.BeatriceContract.ReapplyTransformIfNeeded(ctx, state, player);

                    // 回档后补这次死亡欠下的瘴气（每次死亡只补一张，不再每回合强行补齐）
                    await GrantPendingCurses(state, player, ctx);

                    // 死亡欠下的遗香（每次死亡 +10 层）：必须在还原之后加，否则会被抹掉
                    await ApplyPendingPowers(player, ctx);

                    // 贝蒂的守护（阴魔法阵营遗物）：每次死归后立刻给「遗香 ÷ 10」点格挡。
                    // 同样必须在还原之后 —— 放在前面会被快照里的格挡值盖掉。
                    await ApplyBeatriceGuard(player, ctx);

                    // 傲慢司教（腐蚀）：每次死归后额外 +2 力量 / +2 敏捷
                    await ApplyRewindBlessing(player, ctx);

                    // 荆棘王冠（事件遗物）：每次死归后荆棘再长 2 层
                    await ApplyThornCrown(player, ctx);

                    // 吾星（事件遗物）：每次死归后回复 6 点生命
                    await ApplyMyStar(player);

                    // 战斗历史截回存档点的长度。放在最后：连上面这些「补账」产生的条目
                    // 也一起抹掉，否则历史课/情感芯片会读到被打回那段时间线的记录。
                    targetSnapshot.RestoreHistory();

                    GD.Print("[Subaru] snapshot restored (no native load)");
                }
                catch (Exception e)
                {
                    GD.PushError("[Subaru] snapshot restore failed: " + e);
                }
                finally
                {
                    _busy = false;
                }
            }

            // 能力里「随死归回滚」的计数靠这个版本号判定（斯芬克斯的每回合发牌计数）
            RewindVersion++;

            // 回档这一回合同样要判定癫狂，否则「第 3 张瘴气到手」要等到下一回合才生效
            await TryTriggerMadness(player, ctx);
            // 兜底：回档收尾时把紫光/黑幕/闪光全部拆干净。
            // 演出可能是刚才那一帧提前播的，也可能被中途打断，不能指望它自己收尾。
            Subaru.SubaruCode.UI.RewindVisuals.ForceClearAll();

            // 致命一击那条路的演出可能还在跑（它会把 PlayerActionsDisabled 写回 true）。
            // 先等它收尾，再重建手牌界面，顺序才不会互相覆盖。
            await WaitEarlyAnimAsync();

            // [腐蚀]能力：存档点之后才拿到的那些，本回合的「回合开始」效果要补跑一次
            // （宫廷小丑的四选一、强欲的抽牌……）。必须排在 RefreshHandUi 之前：
            // 这些效果会加牌/抽牌甚至弹选卡界面，界面最后统一重建。
            if (targetSnapshot != null)
                await ReplayKeptPowerTurnStartAsync(targetSnapshot, player, ctx);

            // 先古契约：快照是「第一回合开始、抽完牌、契约还压在抽牌堆底」那一刻拍的，
            // 还原之后契约又回到了牌堆底 —— 必须在这里重新塞进手牌，否则玩家看到的就是
            // 「死归之后契约失效」（2026-11-18 实测：手动死归后完全没有 moved to hand 日志）。
            // 界面刷新交给下面那一次 RefreshHandUi，所以这里 refreshHandUi: false。
            // MoveIntoHandAsync 自己判 TurnNumber == 1，迷失那种更晚的存档点不会误塞。
            await AncientContractHand.MoveIntoHandAsync(ctx, player, false);

            // 手牌界面重建：牌是回档时重新 Add 进去的，界面可能还停在回档前的状态，
            // 表现就是「点牌没反应，只能结束回合」。
            RefreshHandUi(state);

            return;

        await TryTriggerMadness(player, ctx);
    }

    /// <summary>
    /// 补跑「存档点之后才拿到、但因为是[腐蚀]而保留下来」的能力的回合开始效果。
    ///
    /// <para><b>为什么需要</b>：快照拍在 <c>AfterPlayerTurnStartLate</c>，即
    /// <c>AfterPlayerTurnStart</c> **之后**。玩家在这一回合中途打出腐蚀能力牌
    /// （宫廷小丑、强欲的契约、怠惰因子），能力被记事本保住、死归后还在，
    /// 但它的「回合开始」时机早就过去了 —— 于是这一回合能看到能力图标，
    /// 却拿不到它的收益（宫廷小丑没有四选一）。</para>
    ///
    /// <para><b>判据</b>：现在身上有、但存档点的能力清单里没有的能力。
    /// 存档点之前就有的能力效果已经跑过一次，绝不能重跑。</para>
    ///
    /// <para><b>边界</b>：只处理玩家自己；只跑 <c>AfterPlayerTurnStart</c>，
    /// 不碰 <c>AfterSideTurnStart</c>（那个由 <see cref="ReplayAfterSideTurnStartAsync"/> 负责）。
    /// 同一回合连续死归会各跑一次 —— 语义上「每次都回到回合开始」，与玩家预期一致。</para>
    /// </summary>
    private static async Task ReplayKeptPowerTurnStartAsync(
        BattleSnapshot snapshot, Player player, PlayerChoiceContext ctx)
    {
        try
        {
            var creature = player?.Creature;
            if (creature == null)
                return;

            var atSavepoint = snapshot.CapturedPowerTypes(creature);
            var replayed = 0;

            // ToList()：效果里可能删/加能力，边遍历边改会抛
            foreach (var power in creature.Powers.ToList())
            {
                if (atSavepoint.Contains(power.GetType()))
                    continue;

                replayed++;
                GD.Print($"[Subaru] kept power turn-start replayed: {power.GetType().Name}x{power.Amount}");
                await power.AfterPlayerTurnStart(ctx, player);
            }

            if (replayed == 0)
                GD.Print("[Subaru] kept power turn-start: nothing to replay");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] kept power turn-start replay failed: " + e);
        }
    }

    /// <summary>
    /// 初始快照缺的那一段：<c>Hook.AfterSideTurnStart</c>。
    ///
    /// <para><b>为什么缺</b>：<c>CombatManager.StartTurn</c> 的顺序是
    /// <c>BeforeSideTurnStart</c> → <c>AfterBlockCleared</c> → <c>SetupPlayerTurn</c>
    /// → <c>AfterSideTurnStart</c>；而我们的初始快照是在 <c>AfterPlayerTurnStartLate</c>
    /// 里拍的（<see cref="OnPlayerTurnStart"/>），也就是 <c>SetupPlayerTurn</c> **内部**、
    /// <c>AfterSideTurnStart</c> **之前**。所以快照里的能量是「还没扣过的满值」，
    /// 而 <c>AfterSideTurnStart</c> 里的一次性效果（原版「面包」第一回合扣 2 能量）
    /// 还原后不会重跑 —— 每次死归都白拿。</para>
    ///
    /// <para><b>为什么补跑一次就够</b>：快照的语义本来就是「这一钩子执行前的瞬间」，
    /// 还原后补跑一次，正好把「原时间线里本该发生的那一次」补上。
    /// 面包这类效果靠 <c>TurnNumber == 1</c> 判断（自身没有任何状态），
    /// 而 <c>TurnNumber</c> 已经跟着快照一起还原成 1，所以补跑会正常触发。</para>
    ///
    /// <para><b>边界</b>：只在「初始快照 + 复原后是第 1 回合」时补；手动存档点不补。
    /// 本 mod 自己没有任何 <c>AfterSideTurnStart</c> 实现，不会被重复触发。</para>
    /// </summary>
    private static async Task ReplayAfterSideTurnStartAsync(ICombatState state, Player player)
    {
        try
        {
            var turn = player?.PlayerCombatState?.TurnNumber ?? 0;
            if (turn > 1)
            {
                GD.Print($"[Subaru] after-side-turn-start replay skipped: turn={turn}");
                return;
            }

            var participants = state.Players
                .Select(p => p.Creature)
                .Where(c => c != null)
                .ToList();

            await Hook.AfterSideTurnStart(state, CombatSide.Player, participants);

            GD.Print("[Subaru] after-side-turn-start replayed (turn 1 restore), energy="
                     + (player?.PlayerCombatState?.Energy.ToString() ?? "?"));
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] after-side-turn-start replay failed: " + e.Message);
        }
    }

    /// <summary>
    /// 「只在战斗第 1 回合触发一次」的遗物白名单。
    ///
    /// <para><b>为什么需要补跑</b>：<c>CombatManager.StartTurn</c> 的顺序是
    /// 抽牌 → <c>AfterPlayerTurnStart</c>（遗物在这里）→ <c>AfterPlayerTurnStartLate</c>
    /// （我们在这里拍快照）。所以这类遗物的效果**已经包含在快照里**，回档把它原样还原回来，
    /// 引擎也不会再触发一次 —— 玩家看到的就是「死归回到第一回合后，赌徒筹码不再弹弃牌界面」。</para>
    ///
    /// <para><b>为什么只放白名单，不整体重跑所有遗物</b>：快照拍在回合开始**之后**，
    /// 若对所有遗物重跑 <c>AfterPlayerTurnStart</c>，那些「每回合 +1 能量 / +X 格挡」的遗物
    /// 会被重复结算一次，等于每次死归白送一份。只有「无状态、靠
    /// <c>TurnNumber == 1</c> 判断、效果又是让玩家做一次选择」的这类才适合补跑。</para>
    ///
    /// <para><b>加入条件</b>：遗物必须**完全没有**「本场已用过」这类字段。
    /// 梦剑·正梦原本用 <c>_usedThisCombat</c> 记状态，那个字段在快照之前就被置成 true，
    /// 回档还原后会把补跑挡掉；2026-11-18 已把它改成按 <c>TurnNumber == 1</c> 判断，
    /// 才进得了这个名单。</para>
    /// </summary>
    private static readonly Type[] FirstTurnRelics =
    [
        typeof(MegaCrit.Sts2.Core.Models.Relics.GamblingChip),
        typeof(Subaru.SubaruCode.Relics.DreamSword),
    ];

    /// <summary>回档回第 1 回合后，补跑「开局一次」遗物的 <c>AfterPlayerTurnStart</c>。</summary>
    private static async Task ReplayFirstTurnRelicsAsync(
        ICombatState state, Player player, PlayerChoiceContext ctx)
    {
        try
        {
            if (player?.Relics == null)
                return;

            var turn = player.PlayerCombatState?.TurnNumber ?? 0;
            if (turn > 1)
            {
                GD.Print($"[Subaru] first-turn relic replay skipped: turn={turn}");
                return;
            }

            foreach (var relic in player.Relics.ToList())
            {
                if (relic == null || !FirstTurnRelics.Contains(relic.GetType()))
                    continue;

                try
                {
                    GD.Print($"[Subaru] first-turn relic replayed: {relic.GetType().Name}");
                    await relic.AfterPlayerTurnStart(ctx, player);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"[Subaru] first-turn relic {relic.GetType().Name} failed: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] first-turn relic replay failed: " + e.Message);
        }
    }


    /// <summary>
    /// 回档结束后重建手牌界面。
    ///
    /// 时序：StartTurn 先把 PlayerActionsDisabled 置 false，再 SetupPlayerTurn
    /// （重置能量 → 抽牌 → AfterPlayerTurnStart）。我们的回档挂在 AfterPlayerTurnStartLate，
    /// 也就是<strong>抽完牌、手牌界面已经建好之后</strong>才重写牌堆。
    ///
    /// 而 NPlayerHand 只在两个事件里跟着更新：PlayerActionsDisabledChanged 和 OnCombatStateChanged。
    /// 回档这两条都不发，holder 还指着回档前的旧牌对象——点下去等于点一张已经不在手里的牌，
    /// 表现就是「打不出牌、只能结束回合」，下一回合自愈。
    /// （玩家反馈里「用富足塞一张牌就好了」，正是加牌触发了 OnCombatStateChanged。）
    ///
    /// 所以这里手动补一次：清残留出牌态 → 重算禁用态 → 延后一帧重绑 holder。
    /// 敌方回合直接跳过：那时手牌本来就该禁用。
    /// </summary>
    /// <remarks>
    /// 2026-11-18：先古契约「第一回合开始后塞进手牌」也复用这一套 —— 只往模型里加牌
    /// 不会重建手牌节点，玩家看到的手还是 5 张（日志里其实已经 moved to hand）。
    /// </remarks>
    internal static void RefreshHandUi(ICombatState state)
    {
        if (state == null)
            return;

        try
        {
            var manager = CombatManager.Instance;
            if (manager == null)
                return;

            var hand = MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance;
            if (hand == null || !GodotObject.IsInstanceValid(hand))
            {
                GD.Print("[Subaru] hand ui refresh skipped: no hand node");
                return;
            }

            // 玩家侧就直接按玩家回合处理。不能再按 IsEnemyTurnStarted 跳过：
            // 死归是在敌方回合里发生的，那个标记是残留的 true，跳过就等于什么都不做
            // （0.2.8 的日志：hand ui refresh skipped: enemy turn）。
            var side = state.CurrentSide;
            GD.Print($"[Subaru] hand ui refresh: side={side}, actionsDisabled={manager.PlayerActionsDisabled}");

            // 残留的动作锁：玩家回合却还挂着 disabled，就按回合开始时的语义放开
            if (side == CombatSide.Player && manager.PlayerActionsDisabled)
            {
                manager.PlayerActionsDisabled = false;
                GD.Print("[Subaru] force player actions enabled");
            }

            // 1) 清掉可能残留的出牌/选牌态
            hand.CancelAllCardPlay();

            // 2) 按当前状态重算禁用
            hand.UpdateHandDisabledState(state);

            // 3) 延后一帧：等牌堆改动稳定，再让 holder 重新绑到实际牌对象
            var snapshot = state;
            Callable.From(() =>
            {
                try
                {
                    var h = MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance;
                    if (h == null || !GodotObject.IsInstanceValid(h))
                        return;

                    if (snapshot is CombatState cs)
                        h.OnCombatStateChanged(cs);

                    h.UpdateHandDisabledState(snapshot);
                    h.RefreshLayout();
                    GD.Print("[Subaru] hand ui refreshed");
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] deferred hand refresh failed: " + e.Message);
                }
            }).CallDeferred();
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] hand ui refresh failed: " + e.Message);
        }
    }

    public static async Task OnPlayerTurnStart(ICombatState state, PlayerChoiceContext ctx, Player player)
    {
        _busy = false;
        _lastDeathTurn = -1;

        // 排队期间锁住的按钮在这里解开；若这一回合要还原，下面会重新锁上。
        _rewinding = false;

        // 上一回合「死亡 → 交回合」时可能把敌方行动跳过了，回到玩家回合必须清掉，
        // 否则下一次真正的敌方回合也会被跳过。
        SkipEnemyTurn = false;

        // 静默期结束：玩家重新能操作，沙坑立刻恢复正常演出。
        // （若这一回合本身还要回档，RestoreToSavepointAsync 会重新打开它。）
        Subaru.SubaruCode.Patches.SandpitGuard.Silent = false;

        // 记录回合开始时的血量（三分的加护要判断「本回合有没有受过伤」）
        CombatStats.OnTurnStart(player.Creature.CurrentHp);

        // 合并重复实例，避免伤害被重复计算
        CollapsePower<WitchFragrancePower>(player.Creature);
        CollapsePower<MadnessPower>(player.Creature);
        CollapsePower<PridePower>(player.Creature);

        await EnsurePride(player, ctx);

        // 待回档时不要在这里补欠账：紧接着的快照还原会把刚加上的遗香一起抹掉，
        // 所以交给 RestoreToSavepointAsync 在还原之后补。
        if (!_pending)
            await ApplyPendingPowers(player, ctx);

        // 「下回合开始时」的延迟效果（硬撑的脆弱）
        await TurnStartQueue.Apply(ctx, player);

        if (_pending && MultiplayerSupport.RewindEnabled)
        {
            // 自然死亡这条路径也要上锁，否则演出期间连点「立即死归」会并行回档
            _rewinding = true;
            try
            {
                await RestoreToSavepointAsync(ctx, state, player);
                return;
            }
            finally
            {
                _rewinding = false;
            }
        }

        if (_snapshot == null)
            CaptureSnapshot(state);

        await TryTriggerMadness(player, ctx);
        TickMadness(player);
    }

    /// <summary>
    /// 贝蒂的守护：每次死归后立即获得「[魔女遗香]层数 ÷ 10」点格挡（向下取整）。
    ///
    /// 只对持有该遗物的玩家生效（阴魔法阵营专属，见 <see cref="Relics.BeatriceGuard"/>）。
    /// 必须在快照还原之后结算，否则这份格挡会被快照里的格挡值覆盖掉。
    /// </summary>
    private static async Task ApplyBeatriceGuard(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            if (player?.Creature == null || player.Relics == null)
                return;

            if (!player.Relics.Any(r => r is Subaru.SubaruCode.Relics.BeatriceGuard))
                return;

            var fragrance = player.Creature.Powers
                .FirstOrDefault(p => p is WitchFragrancePower)?.Amount ?? 0;

            var block = fragrance / Subaru.SubaruCode.Relics.BeatriceGuard.FragrancePerBlock;
            if (block <= 0)
                return;

            await CreatureCmd.GainBlock(player.Creature, block, ValueProp.Unpowered, null, false);
            GD.Print($"[Subaru] beatrice guard: fragrance {fragrance} => +{block} block");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] beatrice guard failed: " + e);
        }
    }

    /// <summary>
    /// 荆棘王冠（事件遗物）：每次死归回档后再获得 2 层[荆棘]，按次数累加
    /// （开场 3 层 + 2 × 死归次数；战斗开始的快照与手动存档点都按「存档点之后死了几次」算）。
    ///
    /// 回档会把能力还原成存档点里的层数，所以这里必须补「累计应得」而不是无脑 +2：
    /// 同一个存档点连死两次时，无脑 +2 会在还原后得到 3 → 5、第二次又回到 3 → 5，
    /// 叠不起来。差额 = 2 ×（当前死亡数 − 存档点时的死亡数）。
    /// </summary>
    private static async Task ApplyThornCrown(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            if (player?.Creature == null || player.Relics == null)
                return;

            if (!player.Relics.Any(r => r is Subaru.SubaruCode.Relics.ThornCrown))
                return;

            var deathsSinceSnapshot = Math.Max(0, DeathCount - _deathsInSnapshot);
            var stacks = Subaru.SubaruCode.Relics.ThornCrown.ThornsPerRewind * deathsSinceSnapshot;
            if (stacks <= 0)
                return;

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.ThornsPower>(
                ctx, player.Creature, stacks, player.Creature, null, false);

            GD.Print($"[Subaru] thorn crown: +{stacks} thorns after rewind "
                     + $"(death #{DeathCount}, {deathsSinceSnapshot} since savepoint)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] thorn crown failed: " + e);
        }
    }

    /// <summary>
    /// 吾星（事件遗物）：每次死归回档后回复 6 点生命。
    /// 必须排在快照还原之后，否则这份治疗会被存档点里的血量覆盖掉。
    /// </summary>
    private static async Task ApplyMyStar(Player player)
    {
        try
        {
            if (player?.Creature == null || player.Relics == null)
                return;

            if (!player.Relics.Any(r => r is Subaru.SubaruCode.Relics.MyStar))
                return;

            var creature = player.Creature;
            var missing = creature.MaxHp - creature.CurrentHp;
            if (missing <= 0)
                return;

            var heal = Math.Min(Subaru.SubaruCode.Relics.MyStar.HealAfterRewind, missing);
            if (heal <= 0)
                return;

            await CreatureCmd.Heal(creature, heal, true);
            GD.Print($"[Subaru] my star: healed {heal} after rewind (hp={creature.CurrentHp}/{creature.MaxHp})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] my star failed: " + e);
        }
    }

    /// <summary>
    /// 多人版「傲慢因子」开战奖励（2026-11-19 用户定的方案：单机方案 1 的数值 ×2）。
    ///
    /// <para>多人没有死归，<see cref="ApplyRewindBlessing"/> 永远不会被调用 —— 初始遗物会变成空槽。
    /// 所以改成开战给一次：普通 <b>+4/+2</b>、精英 <b>+6/+4</b>、首领 <b>+8/+6</b>
    /// （= 单机每次死归 2/1、3/2、4/3 的两倍），「傲慢司教的加护」按原来的每层 +2/+1 附加上去，不乘 2。</para>
    ///
    /// <para>每场战斗只给一次：按 <c>CombatState</c> 实例判重（<see cref="AfterSideTurnStart"/> 每个玩家回合都会进来一次）。</para>
    /// </summary>
    public static async Task ApplyMultiplayerOpeningBonusAsync(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            if (player?.Creature == null)
                return;

            var combat = player.Creature.CombatState;
            if (combat == null || ReferenceEquals(_mpBonusCombat, combat))
                return;

            var roomType = player.RunState?.CurrentRoom?.RoomType;
            var (strBase, dexBase) = roomType switch
            {
                MegaCrit.Sts2.Core.Rooms.RoomType.Elite => (6, 4),
                MegaCrit.Sts2.Core.Rooms.RoomType.Boss => (8, 6),
                _ => (4, 2),
            };

            var blessingStacks = player.Creature.Powers
                .OfType<ArchbishopBlessingPower>()
                .FirstOrDefault()?.Amount ?? 0m;

            var blessingCount = blessingStacks <= 0m ? 0 : (int)blessingStacks;
            var str = strBase + 2 * blessingCount;
            var dex = dexBase + blessingCount;

            _mpBonusCombat = combat;

            if (str > 0)
                await PowerCmd.Apply<StrengthPower>(ctx, player.Creature, str, player.Creature, null, false);
            if (dex > 0)
                await PowerCmd.Apply<DexterityPower>(ctx, player.Creature, dex, player.Creature, null, false);

            GD.Print($"[Subaru] mp: pride factor opening bonus +{str} str / +{dex} dex "
                     + $"(room={roomType?.ToString() ?? "-"})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] mp pride factor bonus failed: " + e);
        }
    }

    /// <summary>多人开战奖励已经发过的战斗（按实例判重）。</summary>
    private static object? _mpBonusCombat;

    /// <summary>
    /// 死归奖励：傲慢因子每次死归都给本场战斗 +2 力量 / +1 敏捷；
    /// 「傲慢司教的加护」每层再额外 +2 力量 / +1 敏捷（2026-11-19 起可叠层）。
    /// </summary>
    private static async Task ApplyRewindBlessing(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            // 每次死归 +2 力量 / +1 敏捷（初始遗物）；「傲慢司教的加护」按层数各再加一份。
            // 注意：快照还原会把之前发过的力量/敏捷一起抹掉，所以这里按「累计应得」补差额，
            // 而不是每次都 +2——否则永远只有 +2，叠不起来。
            var blessingStacks = player.Creature.Powers
                .OfType<ArchbishopBlessingPower>()
                .FirstOrDefault()?.Amount ?? 0m;

            // 按房间难度给底数：普通战 2/1、精英战 3/2、首领战 4/3
            var roomType = player.RunState?.CurrentRoom?.RoomType;
            var (baseStr, baseDex) = roomType switch
            {
                MegaCrit.Sts2.Core.Rooms.RoomType.Elite => (3, 2),
                MegaCrit.Sts2.Core.Rooms.RoomType.Boss => (4, 3),
                _ => (2, 1),
            };

            var blessingCount = blessingStacks <= 0m ? 0 : (int)blessingStacks;
            var strPer = baseStr + 2 * blessingCount;
            var dexPer = baseDex + 1 * blessingCount;
            var deaths = Math.Max(1, DeathCount);

            // 大贤者的指引（2026-11-21 用户定）：死归加成**一共额外多给一份**（不是每次翻倍）。
            // 层数无意义（升级不变这一条），存在即 +1 份。
            var extraTriggers = player.Creature.Powers.OfType<GreatSagePower>().Any() ? 1 : 0;

            var strTotal = strPer * (deaths + extraTriggers);
            var dexTotal = dexPer * (deaths + extraTriggers);

            // 回档把状态还原成了存档点里的样子，所以「已发」要退回到存档点里的那份
            _blessingAppliedStr = _blessingInSnapshotStr;
            _blessingAppliedDex = _blessingInSnapshotDex;

            var strDelta = strTotal - _blessingAppliedStr;
            var dexDelta = dexTotal - _blessingAppliedDex;

            if (strDelta > 0)
                await PowerCmd.Apply<StrengthPower>(ctx, player.Creature, strDelta, player.Creature, null, false);
            if (dexDelta > 0)
                await PowerCmd.Apply<DexterityPower>(ctx, player.Creature, dexDelta, player.Creature, null, false);

            if (strDelta > 0 || dexDelta > 0)
            {
                _blessingAppliedStr = strTotal;
                _blessingAppliedDex = dexTotal;
                GD.Print($"[Subaru] rewind bonus: +{strDelta} str / +{dexDelta} dex"
                         + $" => 本轮累计 {strTotal} str / {dexTotal} dex（第 {DeathCount} 次死归）");
            }

            // 嫉妒魔女·莎缇拉：必须是「还原之后」才结算，否则伤害会被回档吃掉
            var satella = player.Creature.Powers.OfType<SatellaPower>().FirstOrDefault();
            if (satella != null)
            {
                var state = player.Creature.CombatState;
                var targets = state?.HittableEnemies?.ToList();
                if (targets != null && targets.Count > 0)
                {
                    var stacks = Math.Max(1, DeathCount);
                    // 基数由卡牌写入（默认 15，升级过的 17）；
                    // 打过多张莎缇拉就按张数叠（层数＝打出的张数）。
                    var copies = satella.Amount <= 0m ? 1m : satella.Amount;
                    var dmg = satella.DamagePerRewind * copies * stacks;
                    await CreatureCmd.Damage(ctx, targets, dmg, ValueProp.Move, player.Creature);
                    GD.Print($"[Subaru] satella: death #{stacks} => {dmg} AoE"
                             + $" (per={satella.DamagePerRewind} x {copies} copy)");;
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] blessing failed: " + e);
        }
    }

    /// <summary>同名能力合并成一个实例：多实例会让伤害修正被重复累加。</summary>
    private static void CollapsePower<T>(Creature creature) where T : PowerModel
    {
        try
        {
            var list = creature.Powers.Where(p => p is T).ToList();
            if (list.Count <= 1)
                return;

            var total = list.Sum(p => p.Amount);
            foreach (var extra in list.Skip(1))
                extra.RemoveInternal();

            list[0].Amount = total;
            GD.Print($"[Subaru] collapsed {typeof(T).Name}: {list.Count} -> 1, total={total}");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] collapse failed: " + e);
        }
    }

    private static async Task EnsurePride(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            var existing = player.Creature.Powers.FirstOrDefault(p => p is PridePower);
            if (existing != null)
            {
                if (existing.Amount < MinPride)
                    existing.Amount = MinPride;

                return;
            }

            await PowerCmd.Apply<PridePower>(ctx, player.Creature, MinPride, player.Creature, null, false);
            GD.Print($"[Subaru] pride granted: {MinPride}");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] pride grant failed: " + e);
        }
    }

    /// <summary>把死亡时欠下的能力一次性补上（此时有 ctx，可安全施加）。</summary>
    private static async Task ApplyPendingPowers(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            if (_pendingFragrance > 0)
            {
                await PowerCmd.Apply<WitchFragrancePower>(ctx, player.Creature, _pendingFragrance, player.Creature, null, false);
                GD.Print($"[Subaru] fragrance +{_pendingFragrance}");
                _pendingFragrance = 0;
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] pending powers failed: " + e);
        }
    }

    /// <summary>
    /// 只发「死亡欠账」的魔女瘴气：每次死亡一张，发完清零。
    /// 绝不能按 DeathCount 补足——那样玩家消耗掉瘴气后，下一回合又会被凭空补回来。
    /// </summary>
    public static async Task GrantPendingCurses(ICombatState state, Player player, PlayerChoiceContext ctx)
    {
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            while (_pendingCurses > 0)
            {
                _pendingCurses--;

                var card = state.CreateCard<CurseWitchMiasma>(player);
                if (card == null)
                    continue;

                await BattleSnapshot.AddCardSafely(pcs.Hand, card, pcs);
                GD.Print("[Subaru] curse granted (pending cleared)");
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] grant curses failed: " + e);
        }
    }

    /// <summary>
    /// 预测「这次死归收尾时会不会触发癫狂」——只给素材 5 的 50/50 用，不影响任何规则。
    ///
    /// 判据和 <see cref="TryTriggerMadness"/> 一致，但必须在还原**之前**算（演出那时已经播完）。
    /// 关键：瘴气是跨回档保留的牌（RestorePile 会留下 IsPersistentCard），
    /// 所以还原后手牌里的瘴气 = **现在手里这些** + 本次还欠的 _pendingCurses。
    /// 不能去数存档点手牌——存档点是开战那一刻，那里永远是 0。
    /// （0.2.29 就是这么错的：日志里 madness predict 恒为 saved=0 pending=1 pride=3 => False，
    /// 预测永远为假，视频一次也抽不到。）
    /// </summary>
    private static bool PredictMadnessOnRestore(Player player)
    {
        try
        {
            if (player?.Creature == null)
                return false;

            if (player.Creature.HasPower<MadnessPower>())
                return false;

            var pride = player.Creature.Powers.FirstOrDefault(p => p is PridePower)?.Amount ?? 0;
            if (pride <= 0)
                return false;

            var inHand = player.PlayerCombatState?.Hand.Cards
                .Count(c => c is CurseWitchMiasma) ?? 0;

            var predicted = inHand + _pendingCurses;
            GD.Print($"[Subaru] madness predict: hand={inHand} pending={_pendingCurses} pride={pride} => {predicted >= pride}");
            return predicted >= pride;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] madness predict failed: " + e.Message);
            return false;
        }
    }

    private static async Task TryTriggerMadness(Player player, PlayerChoiceContext ctx)
    {
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null || player.Creature.HasPower<MadnessPower>())
                return;

            var curses = pcs.Hand.Cards.Where(c => c is CurseWitchMiasma).ToList();
            var pride = player.Creature.Powers.FirstOrDefault(p => p is PridePower)?.Amount ?? 0;

            GD.Print($"[Subaru] madness check: curses={curses.Count}, pride={pride}");

            if (pride <= 0 || curses.Count < pride)
                return;

            // 癫狂每次触发都清空瘴气（清空后手牌瘴气归零，要攒满才会再触发）。
            //
            // 必须开 MiasmaGuard 的允许窗口：瘴气「不可被移出战斗」那道防线
            // （Patches\MiasmaGuard.cs 的 MiasmaRemoveGuard）会把这里的 RemoveFromCombat
            // 直接拦掉 —— 2026-11-23 实测日志是三行 `miasma remove blocked`，
            // 结果癫狂加上了、瘴气一张没少（癫狂结束后立刻二次触发、永久癫狂）。
            using (Patches.MiasmaGuard.Allow())
            {
                foreach (var curse in curses)
                    await CardPileCmd.RemoveFromCombat(curse, false);
            }

            await ApplySingleMadness(player, ctx);
            _madnessAppliedAtTurn = pcs.TurnNumber;
            GD.Print("[Subaru] madness triggered");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] madness check failed: " + e);
        }
    }

    /// <summary>
    /// 癫狂只允许存在一个实例，层数固定为 3（层数只表示剩余回合）。
    /// 每获得一层癫狂，额外获得一层易伤 —— 易伤是引擎原生状态，所以意图上的受伤数字会同步变红。
    /// </summary>
    private static async Task ApplySingleMadness(Player player, PlayerChoiceContext ctx)
    {
        const decimal stacks = 3m;

        try
        {
            var existing = player.Creature.Powers.Where(p => p is MadnessPower).ToList();
            if (existing.Count > 0)
            {
                existing[0].Amount = (int)stacks;
                foreach (var extra in existing.Skip(1))
                    extra.RemoveInternal();
            }
            else
            {
                await PowerCmd.Apply<MadnessPower>(ctx, player.Creature, stacks, player.Creature, null, false);

                var madness = player.Creature.Powers.FirstOrDefault(p => p is MadnessPower);
                if (madness != null)
                    madness.Amount = (int)stacks;
            }

            // 1 层癫狂 → 1 层易伤
            await PowerCmd.Apply<VulnerablePower>(ctx, player.Creature, stacks, player.Creature, null, false);
            GD.Print($"[Subaru] madness applied: {stacks}, vulnerable +{stacks}");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] apply madness failed: " + e);
        }
    }

    private static void TickMadness(Player player)
    {
        try
        {
            var madness = player.Creature.Powers.FirstOrDefault(p => p is MadnessPower);
            if (madness == null)
                return;

            var turn = player.PlayerCombatState?.TurnNumber ?? 0;
            if (_madnessAppliedAtTurn < 0)
            {
                _madnessAppliedAtTurn = turn;
                return;
            }

            if (turn > _madnessAppliedAtTurn)
            {
                madness.Amount -= 1;
                _madnessAppliedAtTurn = turn;

                if (madness.Amount <= 0)
                    madness.RemoveInternal();

                GD.Print("[Subaru] madness tick => " + madness.Amount);
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] madness tick failed: " + e);
        }
    }
}
