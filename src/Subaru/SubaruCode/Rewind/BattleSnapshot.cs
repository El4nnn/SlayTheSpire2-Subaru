using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Saves.Runs;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 卡牌自己的「回档计数」：成长值存在卡实例字段里的牌实现它（例：战虫将·卡夫马每进手牌 +3 伤害）。
///
/// <para>为什么需要：<c>BattleSnapshot</c> 只还原牌在哪个牌堆、关键词、附魔、费用覆盖这些
/// **引擎维度**上的状态；卡自己的私有字段不在任何维度里，不单独存就会「死归后伤害停在死亡那一刻」。</para>
///
/// <para>约束：<b>必须是可整数表示的单值状态</b>。要存多个字段就自己打包（位数/对象映射），
/// 不要往快照里塞引用 —— 快照的所有其他字段都是值类型或规范模型，理由同附魔那一段注释。</para>
/// </summary>
public interface IRewindCardState
{
    /// <summary>把当前计数拍成整数交给快照。</summary>
    int CaptureRewindCounter();

    /// <summary>回档时按快照写回（会被调用多次，必须幂等）。</summary>
    void RestoreRewindCounter(int value);
}

/// <summary>
/// 战斗状态快照。v3：血量 / 格挡 / 能力（精确增删）/ 敌人意图 / 回合数 全部回滚。
/// </summary>
public sealed class BattleSnapshot
{
    public sealed class PowerState
    {
        public Type? PowerType;
        public int Amount;
        public int AmountOnTurnStart;

        /// <summary>
        /// 这个能力身上**全部 DynamicVars 的 BaseValue**。
        ///
        /// 为什么必须存：原版有一批能力把「计数」放在动态变量里而不是 Amount 上，典型是
        /// <c>SlowPower</c>（旧日雕像进场给自己挂的[缓慢]）：每打一张牌
        /// <c>DynamicVars["SlowAmount"] += 1</c>，图标数字是 <c>DisplayAmount = SlowAmount × 10</c>，
        /// 在该生物一侧回合开始时清零。只还原 Amount 的话，死归后计数停在死亡那一刻
        /// ——图标继续显示「受到伤害 +50%」，实际也真的多挨这 50%（2026-11-18 反馈）。
        ///
        /// 存 BaseValue 而不是 Value：Value 会叠上钩子/修正，写回去会重复计算。
        /// </summary>
        public readonly Dictionary<string, decimal> Vars = new();

        /// <summary>
        /// 白名单里的「私有 int 计数」：字段名 → 值。
        ///
        /// <para>动态变量覆盖不到这一类。最典型的是 <c>SlothPower._cardsPlayedThisTurn</c>：
        /// 每打一张牌 +1、回合开始清零、图标直接显示它。死归后 <see cref="Amount"/> 回去了、
        /// 这个计数却没回去 → 玩家看到「懒惰层数不回档」，而且本回合还能打几张也按旧值算
        /// （2026-11-21 恐惧模式 + 知识恶魔实测）。</para>
        ///
        /// <para>只收白名单（见 <c>PowerCounterFields</c>），不做全量反射：
        /// 能力身上还有一堆「不该回滚」的引用/临时状态。</para>
        /// </summary>
        public readonly Dictionary<string, int> Fields = new();
    }

    public sealed class CreatureState
    {
        /// <summary>当前这一路恢复实际操作的实例（重建后会指向新怪）。</summary>
        public Creature Creature = null!;

        /// <summary>
        /// 快照时刻的原始实例，**永远不改**。
        /// 重建时按它的模型类型来造新怪，这样连续死归也不会因为上次重建而漂移。
        /// </summary>
        public Creature Captured = null!;
        public int Hp;
        public int MaxHp;
        public int Block;
        public readonly List<PowerState> Powers = new();
        public MoveState? NextMove;
        public bool IsMonster;
        public string? SlotName;
        public int EnemyIndex = -1;
        public bool HasNodePosition;
        public Godot.Vector2 NodePosition;

        // 怪物自己的「阶段计数」。实验体（TestSubject）靠 Respawns 决定贴图套哪一套
        // （GenerateAnimator 里 idle_loop1/2/3 是按 Respawns 选的），
        // 只回血量不回这个计数器，就会出现「阶段二的贴图、阶段一的状态」。
        public int MonsterRespawns;
        public int MonsterExtraMultiClawCount;

        // 仪式兽的「二阶段」只有两个布尔字段，却是整套二阶段 UI 的唯一开关：
        //   SetStunned() 里 IsInSecondPhase = true（外加挂 RitualPower、播 Stun 动画）
        //   NCeremonialBeastBgVfx.UpdateVfxAndMusic() 每帧读它决定背景花瓣/光环/骷髅
        // 回档把 RitualPower 删了、血写回去了，这两个布尔还是 true，于是背景停在二阶段。
        public bool BeastSecondPhase;
        public bool BeastStunnedByPlow;

        // 女王的招式转移条件读 HasAmalgamDied（GenerateMoveStateMachine 里 4 个 lambda 都用它），
        // 另外两个 MoveState 字段在大招之间流转。不回滚这三个，
        // 死归回到「副怪还活着」的存档点后女王会直接走狂暴分支。
        public bool QueenAmalgamDied;
        public string? QueenEnragedStateId;
        public string? QueenBurnBrightStateId;

        // 视觉节点状态：帝皇蟹那种「部件怪物」会有很多套动画，
        // 节点被销毁再重建时如果不回放，看起来就是「钳子倒在地上」。
        // 手动存档（迷失）和自动存档走的是同一段 Capture，所以这里记了就有。
        public string? AnimName;
        public bool AnimLoop;
        public float AnimTime = -1f;
        public List<string> AnimQueue = new();
        public Godot.Vector2 NodeScale = Godot.Vector2.One;
        public bool HasNodeScale;

        // 血条显示模式（∞/正常）：Creature.HpDisplay 是**存下来的字段**，不是算出来的。
        // 瀑布巨兽蓄力时游戏把它设成 Infinite，回档不写回去，血条就永远停在 ∞。
        public MegaCrit.Sts2.Core.Entities.Creatures.HpDisplay HpDisplay;
        public string? AnimTrack1;
        public int MonsterPressureIdx;

        // 单体 Boss 的内部计数器（火箭的充能/计算层数之类）。
        // 光回血量不回这些，就会出现「回档后它只会每回合打 3 点」这种退化行为。
        // 只回滚 MonsterScalarSpecs 里逐只列出来的字段，不做全量反射。
        public Dictionary<string, object>? MonsterScalars;

        // 怪物身上指向「某个招式状态」的字段（_terrorState / _deadState / _aboutToBlowState …）。
        // 招式状态机的计数器（不是怪物字段，MonsterScalarSpecs 抓不到，见 CaptureMoveCounters）
        public bool MovePerformedFirst;
        public readonly Dictionary<string, bool> MovePerformedOnce = new();

        // 存 StateId，回档时在重建后的状态机里按 id 找回同一个状态再赋回去。
        public readonly Dictionary<string, string> MonsterStateIds = new();
    }

    public sealed class PlayerState
    {
        public Player Player = null!;
        public int TurnNumber;
        public int Energy;

        /// <summary>
        /// 快照那一刻的**有效能量上限**（<c>PlayerCombatState.MaxEnergy</c> 内部会走
        /// <c>Hook.ModifyMaxEnergy</c>，拿到的是含「强欲的契约 / 龙珠 / 添水」等加成后的值）。
        ///
        /// 为什么必须存：能量还原是绝对值覆盖，但上限是**动态**的。跨死归保留的腐蚀能力
        /// （强欲的契约）在还原后仍然把上限 +1，于是「上限 3 时记下的 5 点」放到「上限 4」
        /// 的回合里就少算了 1 点（2026-11-17 玩家实测 5/4，应为 6/4）。
        /// 存下上限、还原时按差值平移，才能保住古茶具套装那类「超上限」的能量。
        /// </summary>
        public int MaxEnergy;

        public int Stars;
        public readonly List<CardModel> Hand = new();
        public readonly List<CardModel> Draw = new();
        public readonly List<CardModel> Discard = new();
        public readonly List<CardModel> Exhaust = new();
        public readonly List<CardModel> Play = new();
        public readonly List<PotionModel> Potions = new();

        /// <summary>
        /// 每张牌的「可变状态」：附着（感染/魂缚/束缚…）、临时关键词、强化等级。
        /// 这些挂在 CardModel 实例上，不在牌堆列表里 —— 只回牌堆不回它们，
        /// 死归后牌会停在死亡那一刻的样子（例如还带着敌人给的「魂缚」）。
        /// </summary>
        public readonly Dictionary<CardModel, CardExtraState> CardStates = new();

        // 状态锁链（ChainsOfBinding）：它靠战斗历史里「本回合已施予 Bound 的条数」判断
        // 这回合还能不能再绑牌，而不是用计数器。存档点那一刻的条数必须记下来，
        // 死归后把多出来的条目削掉，否则条数一直是满的，再抽牌就不施加魂缚了。
        public int ChainsBoundThisTurn;
        public bool ChainsBoundCardPlayed;
    }

    public sealed class CardExtraState
    {
        /// <summary>
        /// 卡牌自己的「回档计数」（见 <see cref="IRewindCardState"/>）。
        ///
        /// 有些牌的成长值存在卡自己的字段里（战虫将·卡夫马每进手牌 +3 伤害那种），
        /// 不在牌堆/关键词/附魔任何一个可还原维度上 —— 不单独存的话，
        /// 死归后卡面伤害会停在死亡那一刻的数值。
        /// </summary>
        public bool HasRewindCounter;
        public int RewindCounter;
        // 只存「规范模型 + 层数」，不存场上那份 AfflictionModel 引用：
        // ClearAffliction 会调 AfflictionModel.ClearInternal() 把那个对象清空，
        // 存引用等于存了个空壳，写回去也恢复不了。回档时用规范模型 ToMutable() 重建一份。
        public MegaCrit.Sts2.Core.Models.AfflictionModel? AfflictionCanonical;
        public int AfflictionAmount;
        public readonly List<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword> Keywords = new();
        public int UpgradeLevel;

        // 其余「临时」单卡状态。卡牌效果（含怪物给的）会临时改这些，
        // 死归只回牌堆就会停在死亡那一刻的样子。
        public bool SingleTurnRetain;
        public bool SingleTurnSly;
        public bool ExhaustOnNextPlay;
        public int ExtraReplayThisTurn;
        public bool IsDupe;
        public CardModel? TransformIntoAfterPlay;
        public MegaCrit.Sts2.Core.Models.EnchantmentModel? Enchantment;

        /// <summary>附魔的层数。和 <see cref="AfflictionAmount"/> 一样必须单独存，
        /// 还原时要经 <c>CardCmd.Enchant</c> 一起写回。</summary>
        public int EnchantmentAmount = 1;

        // 「本场」费用覆盖（例：宝石面具把抽到的那张能力牌变成 0 费）。
        // 这类状态挂在卡牌的 CardEnergyCost / TemporaryStarCost 上，不在原型上；
        // 牌被销毁后只能按原型重建，不单独存/写回就会丢（回档后那张牌变回满费）。
        public int? CombatEnergyCost;
        public int? CombatStarCost;
    }

    public readonly List<CreatureState> Creatures = new();
    public readonly List<PlayerState> Players = new();
    public int RoundNumber;
    public SerializableRunRngSet? Rng;

    /// <summary>
    /// 存档点那一刻的战斗历史条目数（<see cref="RestoreHistory"/> 用它把历史截回去）。
    /// 历史不回滚会留下「被打回那段时间线」的出牌记录，依赖它的东西会被误导：
    /// 历史课（问「上一回合最后打出的攻击牌」）会重放一张本该不存在的牌，
    /// 情感芯片/波纹水盆这类「本回合有没有…」的判断也会读到假数据。
    /// </summary>
    public int HistoryCount;

    /// <summary>HistoryCount 是否抓成功（抓失败就别截，否则会把历史清空）。</summary>
    public bool HistoryCaptured;

    public static BattleSnapshot Capture(ICombatState state)
    {
                // 设置存档点：之前打出的能力牌不该在死归后回来
        PlayedPowerCards.ClearFromSavepoint();

var snap = new BattleSnapshot { RoundNumber = state.RoundNumber };

        try
        {
            var entries = RawHistoryEntries();
            if (entries != null)
            {
                snap.HistoryCount = entries.Count;
                snap.HistoryCaptured = true;
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] capture history count failed: " + e.Message);
        }

        try
        {
            snap.Rng = state.RunState?.Rng?.ToSerializable();
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] capture rng failed: " + e);
        }

        var enemyIndex = 0;
        foreach (var creature in state.Creatures)
        {
            var cs = new CreatureState
            {
                Creature = creature,
                Captured = creature,
                Hp = creature.CurrentHp,
                MaxHp = creature.MaxHp,
                Block = creature.Block,
                IsMonster = creature.IsMonster,
                SlotName = creature.SlotName,
                HpDisplay = creature.HpDisplay,
            };

            if (creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.TestSubject subject)
            {
                cs.MonsterRespawns = subject.Respawns;
                cs.MonsterExtraMultiClawCount = subject.ExtraMultiClawCount;
            }

            if (creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast beast1)
            {
                cs.BeastSecondPhase = beast1.IsInSecondPhase;
                cs.BeastStunnedByPlow = beast1.IsStunnedByPlowRemoval;
            }

            if (creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.Queen queen0)
            {
                cs.QueenAmalgamDied = queen0.HasAmalgamDied;
                cs.QueenEnragedStateId = queen0.EnragedState?.StateId;
                cs.QueenBurnBrightStateId = queen0.BurnBrightForMeState?.StateId;
            }

            // 帝皇蟹的部件（火箭）是独立单体，招式强度由它自己的计数器决定，
            // 这类怪只回血量会退化成「每回合只会打 3 点」。
            if (creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant giant0)
                cs.MonsterPressureIdx = giant0.PressureBuildupIdx;

            // 每只怪的状态字段：只回滚 MonsterScalarSpecs 里逐只列出来的那些
            if (creature.Monster != null)
            {
                cs.MonsterScalars = CaptureMonsterScalars(creature.Monster);
                CaptureMonsterStateIds(creature.Monster, cs.MonsterStateIds);
            }

            try
            {
                var node0 = creature.GetCreatureNode();
                if (node0 != null)
                {
                    cs.HasNodePosition = true;
                    cs.NodePosition = node0.GlobalPosition;
                    cs.NodeScale = node0.Scale;
                    cs.HasNodeScale = true;

                    // 动画状态：部件怪物（帝皇蟹的钳子/火箭）动画很多，
                    // 节点重建后不回放就是「倒在地上」的那一帧。
                    var spine = node0.SpineAnimation;
                    if (spine.IsValid)
                    {
                        cs.AnimName = spine.GetCurrentAnimationName(0);
                        cs.AnimTrack1 = spine.GetCurrentAnimationName(1);

                        var entry = spine.GetCurrentTrack(0);
                        if (entry != null)
                        {
                            cs.AnimLoop = entry.IsLoop();
                            cs.AnimTime = entry.GetTrackTime();
                            cs.AnimQueue = entry.GetQueuedAnimationNames()?.ToList() ?? new List<string>();
                        }

                        GD.Print(
                            $"[Subaru] anim captured: {creature.Name}, name={cs.AnimName ?? "<none>"}, " +
                            $"loop={cs.AnimLoop}, t={cs.AnimTime:0.00}, queue={cs.AnimQueue.Count}");
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] capture node state failed: " + e.Message);
            }

            foreach (var power in creature.Powers)
            {
                var ps = new PowerState
                {
                    PowerType = power.GetType(),
                    Amount = power.Amount,
                    AmountOnTurnStart = power.AmountOnTurnStart,
                };

                CapturePowerVars(power, ps.Vars);
                CapturePowerFields(power, ps.Fields);
                cs.Powers.Add(ps);
            }

            if (creature.IsMonster && creature.Monster != null)
            {
                cs.NextMove = creature.Monster.NextMove;
                CaptureMoveCounters(creature.Monster, cs);
            }

            if (creature.IsMonster)
            {
                cs.EnemyIndex = enemyIndex;
                enemyIndex++;
            }

            snap.Creatures.Add(cs);
        }

        foreach (var player in state.Players)
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                continue;

            var ps = new PlayerState
            {
                Player = player,
                TurnNumber = pcs.TurnNumber,
                Energy = pcs.Energy,
                MaxEnergy = pcs.MaxEnergy,
                Stars = pcs.Stars,
            };

            ps.Hand.AddRange(pcs.Hand.Cards);
            ps.Draw.AddRange(pcs.DrawPile.Cards);
            ps.Discard.AddRange(pcs.DiscardPile.Cards);
            ps.Exhaust.AddRange(pcs.ExhaustPile.Cards);
            ps.Play.AddRange(pcs.PlayPile.Cards);

            CaptureCardStates(ps);

            try
            {
                ps.ChainsBoundThisTurn = CountBoundThisTurn(state, player.Creature);
                ps.ChainsBoundCardPlayed = ReadBoundCardPlayed(player.Creature);
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] capture chains state failed: " + e.Message);
            }

            try
            {
                ps.Potions.AddRange(player.PotionSlots);
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] capture potions failed: " + e);
            }

            snap.Players.Add(ps);
        }

        return snap;
    }

    /// <summary>
    /// 「迷失」专用：只恢复玩家自己（生命 / 格挡 / 能力层数），
    /// 不动敌人、不动牌堆、不回退回合，保证稳定。
    /// </summary>
    public void RestorePlayerOnly()
    {
        foreach (var cs in Creatures)
        {
            if (!cs.Creature.IsPlayer)
                continue;

            try
            {
                cs.Creature.MaxHp = cs.MaxHp;
                cs.Creature.CurrentHp = cs.Hp;
                cs.Creature.Block = cs.Block;
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] restore player hp failed: " + e);
            }

            try
            {
                foreach (var saved in cs.Powers)
                {
                    if (saved.PowerType == null)
                        continue;

                    var live = cs.Creature.Powers.FirstOrDefault(x => x.GetType() == saved.PowerType);
                    if (live != null)
                    {
                        live.Amount = saved.Amount;
                        live.AmountOnTurnStart = saved.AmountOnTurnStart;
                    }
                }
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] restore player powers failed: " + e);
            }
        }
    }

    /// <summary>
    /// 「迷失」专用：把所有单位（含敌人）的血量 / 格挡 / 能力 / 意图恢复到存档点，
    /// 不动牌堆、不回退回合。
    /// </summary>
    public async Task RestoreCreatures(PlayerChoiceContext ctx, ICombatState state)
    {
        try
        {
            state.RoundNumber = RoundNumber;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] restore round number failed: " + e);
        }

        // 把「存档点之后才出现」的单位清干净（死亡召唤类敌人会在这里被处理）。
        PurgeCreaturesNotInSnapshot(state);

        foreach (var cs in Creatures)
            await RestoreCreature(cs, ctx, state);

        EnforceExactMonsterSet(state);


        foreach (var ps in Players)
        {
            try
            {
                var pcs = ps.Player.PlayerCombatState;
                if (pcs != null)
                    pcs.TurnNumber = ps.TurnNumber;
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] restore turn number failed: " + e);
            }
        }

        // 全部单位都回档完再统一刷一次意图：
        // 意图上的受伤数字还受玩家这边能力（易伤/虚弱等）影响，先刷会显示旧值
        foreach (var cs in Creatures)
        {
            if (cs.IsMonster)
                RefreshIntentVisual(cs.Creature);
        }
    }

    /// <summary>恢复 RNG 与药水，保证「同样操作得到同样结果」。</summary>
    public async Task RestoreRngAndPotions(ICombatState state)
    {
        if (Rng != null)
        {
            try
            {
                state.RunState?.Rng?.LoadFromSerializable(Rng);
                GD.Print("[Subaru] rng restored");
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] restore rng failed: " + e);
            }
        }

        foreach (var ps in Players)
        {
            try
            {
                var player = ps.Player;
                var live = player.PotionSlots.ToList();

                foreach (var potion in live.Where(p => p != null && !ps.Potions.Contains(p)).ToList())
                {
                    // 存档点之后用掉的药水：实例已经从状态里移除，再 Discard 就是空引用
                    // （0.2.5 之前玩家反馈的 discard potion failed: NullReferenceException）。
                    // 它本来就已经没了，跳过即可——不放回、也不丢。
                    if (potion.HasBeenRemovedFromState || potion.Owner == null)
                    {
                        GD.Print("[Subaru] potion already gone, skip discard");
                        continue;
                    }

                    try
                    {
                        await PotionCmd.Discard(potion);
                    }
                    catch (System.Exception e)
                    {
                        GD.PushError("[Subaru] discard potion failed: " + e);
                    }
                }

                foreach (var potion in ps.Potions)
                {
                    if (potion == null)
                        continue;

                    if (player.PotionSlots.Contains(potion))
                        continue;

                    // 存档点之后喝掉的那瓶：实例已经被 RemoveBeforeUse 摘出状态
                    //（HasBeenRemovedFromState = true），再把它原样塞回格子，玩家会拿到
                    // 一瓶点不开/用不了的药水。所以先按规范模型重建一份再放回去。
                    var restore = potion;
                    if (potion.HasBeenRemovedFromState || potion.Owner == null)
                    {
                        var fresh = RebuildPotion(potion);
                        if (fresh == null)
                        {
                            GD.Print("[Subaru] potion rebuild skipped: " + potion.Id);
                            continue;
                        }

                        restore = fresh;
                        GD.Print("[Subaru] potion rebuilt: " + potion.Id);
                    }

                    try
                    {
                        await PotionCmd.TryToProcure(restore, player, -1);
                    }
                    catch (System.Exception e)
                    {
                        GD.PushError("[Subaru] restore potion failed: " + e);
                    }
                }
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] potions step failed: " + e);
            }
        }
    }

    /// <summary>完整回档：血量 / 格挡 / 能力增删 / 敌人意图 / 回合数。</summary>
    /// <summary>
    /// 这张牌（**实例**）在存档点的五个牌堆里存不存在。
    ///
    /// <para>用途：区分「本来就在牌组里的牌」和「战斗中生成的牌」（能力药水、幼女使者、
    /// 契约变形…）。两者打出后都会从战斗牌里消失，但只有前者该在死归后补回来 ——
    /// 后者连存档点都不存在，补回来就是白赚一张（2026-11-21 能力药水实测）。</para>
    ///
    /// <para>判据必须是实例引用：快照存的就是卡牌对象的引用，牌之后被打出也不会让引用失效。</para>
    /// </summary>
    public bool ContainsCard(CardModel card)
    {
        if (card == null)
            return false;

        foreach (var ps in Players)
        {
            if (ps.Hand.Contains(card) || ps.Draw.Contains(card) || ps.Discard.Contains(card)
                || ps.Exhaust.Contains(card) || ps.Play.Contains(card))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 恢复五个牌堆。规则：
    /// 1) 快照里存在的牌 → 用 Add 移动到目标牌堆（Add 会自己处理离开原牌堆）
    /// 2) 快照里不存在的牌（战斗中生成的临时牌）→ 才 RemoveFromCombat
    /// 关键：绝不能对「快照里存在的牌」调用 RemoveFromCombat，那会把牌永久销毁，导致总牌数变少。
    /// </summary>
    public async Task RestorePiles(PlayerChoiceContext ctx, ICombatState state)
    {
        foreach (var ps in Players)
        {
            var pcs = ps.Player.PlayerCombatState;
            if (pcs == null)
                continue;

            var alive = new HashSet<CardModel>();
            alive.UnionWith(ps.Hand);
            alive.UnionWith(ps.Draw);
            alive.UnionWith(ps.Discard);
            alive.UnionWith(ps.Exhaust);
            alive.UnionWith(ps.Play);

            // 顺序很重要：先恢复「手牌之外」的四个牌堆——这一步会把该回抽牌堆/弃牌堆的牌
            // 从手牌里搬走。最后才补手牌，否则手牌先被塞满（上限 10），后面的 Add 全部失败。
            await RestorePile(pcs.DrawPile, ps.Draw, alive, true, pcs, state, ps.Player, ps.CardStates);
            await RestorePile(pcs.DiscardPile, ps.Discard, alive, true, pcs, state, ps.Player, ps.CardStates);
            await RestorePile(pcs.ExhaustPile, ps.Exhaust, alive, true, pcs, state, ps.Player, ps.CardStates);
            await RestorePile(pcs.PlayPile, ps.Play, alive, false, pcs, state, ps.Player, ps.CardStates);
            await RestorePile(pcs.Hand, ps.Hand, alive, false, pcs, state, ps.Player, ps.CardStates);
        }
    }

    /// <summary>
    /// 记录每张牌的可变状态（附着 / 临时关键词 / 强化等级）。
    /// 牌堆列表只记「哪张牌在哪」，不记「这张牌被改成了什么样」。
    /// </summary>
    private static void CaptureCardStates(PlayerState ps)
    {
        foreach (var card in ps.Hand
                     .Concat(ps.Draw)
                     .Concat(ps.Discard)
                     .Concat(ps.Exhaust)
                     .Concat(ps.Play))
        {
            if (card == null)
                continue;

            try
            {
                var affliction = card.Affliction;

                var st = new CardExtraState
                {
                    UpgradeLevel = card.CurrentUpgradeLevel,
                };

                // 本场费用覆盖（宝石面具之类）：重建时要用，见 Rebuild/ApplyCombatCostOverride
                try
                {
                    st.CombatEnergyCost = card.EnergyCost?.GetResolved();
                }
                catch
                {
                    // 没有能量费用的牌
                }

                try
                {
                    st.CombatStarCost = card.GetStarCostThisCombat();
                }
                catch
                {
                    // 没有星费的牌
                }

                if (affliction != null)
                {
                    st.AfflictionCanonical = affliction.CanonicalInstance ?? affliction;
                    st.AfflictionAmount = affliction.Amount;
                }

                foreach (var keyword in card.LocalKeywords)
                    st.Keywords.Add(keyword);

                st.SingleTurnRetain = card.HasSingleTurnRetain;
                st.SingleTurnSly = card.HasSingleTurnSly;
                st.ExhaustOnNextPlay = card.ExhaustOnNextPlay;
#if GAME_BETA
                st.ExtraReplayThisTurn = card.ExtraReplayThisTurn;
#endif
                st.IsDupe = card.IsDupe;

#if GAME_BETA
                var transform = card.TransformIntoAfterPlay;
                st.TransformIntoAfterPlay = transform?.CanonicalInstance ?? transform;
#endif

                var enchantment = card.Enchantment;
                if (enchantment != null)
                {
                    st.Enchantment = enchantment.CanonicalInstance ?? enchantment;
                    st.EnchantmentAmount = enchantment.Amount;
                }

                // 牌自己的成长计数（卡夫马「每进手牌 +3」那种）
                if (card is IRewindCardState rewindState)
                {
                    try
                    {
                        st.HasRewindCounter = true;
                        st.RewindCounter = rewindState.CaptureRewindCounter();
                    }
                    catch (System.Exception e)
                    {
                        GD.PushWarning("[Subaru] capture card rewind counter failed: " + e.Message);
                    }
                }

                ps.CardStates[card] = st;
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] capture card state failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 把每张牌的可变状态按快照写回。
    /// 关键场景：女王/感染棱柱这类怪给手牌加附着（魂缚、活力火花…），
    /// 死归只回牌堆的话，附着会留在牌上，牌打不出去。
    /// </summary>
    public void RestoreCardStates()
    {
        foreach (var ps in Players)
        {
            var cleared = 0;
            var reapplied = 0;

            foreach (var pair in ps.CardStates)
            {
                var card = pair.Key;
                var st = pair.Value;

                // 被销毁重建的牌是全新实例，状态本来就是干净的
                if (card == null || card.HasBeenRemovedFromState)
                    continue;

                // 牌自己的成长计数回到存档点的值（卡夫马那种）
                if (st.HasRewindCounter && card is IRewindCardState rewindState)
                {
                    try
                    {
                        rewindState.RestoreRewindCounter(st.RewindCounter);
                        GD.Print($"[Subaru] restored card rewind counter: {card.Id} -> {st.RewindCounter}");
                    }
                    catch (System.Exception e)
                    {
                        GD.PushWarning("[Subaru] restore card rewind counter failed: " + e.Message);
                    }
                }

                try
                {
                    if (st.AfflictionCanonical == null)
                    {
                        if (card.Affliction != null)
                        {
                            MegaCrit.Sts2.Core.Commands.CardCmd.ClearAffliction(card);
                            cleared++;
                        }
                    }
                    else if (card.Affliction == null
                             || card.Affliction.GetType() != st.AfflictionCanonical.GetType())
                    {
                        // 类型不对就先清干净（AfflictInternal 对已附着的牌会直接抛）
                        if (card.Affliction != null)
                            MegaCrit.Sts2.Core.Commands.CardCmd.ClearAffliction(card);

                        card.AfflictInternal(st.AfflictionCanonical.ToMutable(), st.AfflictionAmount);
                        reapplied++;
                    }
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] restore affliction failed: " + e.Message);
                }

                // 强化等级：必须走引擎的升降级流程（见 CardUpgradeSync 的注释）。
                // 直接写 CurrentUpgradeLevel 不重算 DynamicVars → 「卡面未强化、数值还是强化后」
                // （2026-11-25 玩家反馈：剃刀牙在本场临时强化 + 死归）。
                // 顺序：等级 → 关键词 → 本场费用覆盖。降级会用 canonical 重建数值/关键词/费用，
                // 所以它必须排在关键词与费用之前。
                Extensions.CardUpgradeSync.SetLevel(card, st.UpgradeLevel);

                try
                {
                    foreach (var keyword in card.LocalKeywords.ToList())
                    {
                        if (!st.Keywords.Contains(keyword))
                            card.RemoveKeyword(keyword);
                    }

                    foreach (var keyword in st.Keywords)
                    {
                        if (!card.LocalKeywords.Contains(keyword))
                            card.AddKeyword(keyword);
                    }
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] restore keywords failed: " + e.Message);
                }

                // 降级会 ResetForDowngrade() 重置费用：本场费用覆盖（宝石面具之类）要补回来
                ApplyCombatCostOverride(card, st);

                // 其余临时状态：按值写回（对象状态存的是规范模型，这里重建一份）
                try
                {
                    if (card.HasSingleTurnRetain != st.SingleTurnRetain)
                        card.HasSingleTurnRetain = st.SingleTurnRetain;

                    if (card.HasSingleTurnSly != st.SingleTurnSly)
                        card.HasSingleTurnSly = st.SingleTurnSly;

                    if (card.ExhaustOnNextPlay != st.ExhaustOnNextPlay)
                        card.ExhaustOnNextPlay = st.ExhaustOnNextPlay;

#if GAME_BETA
                    if (card.ExtraReplayThisTurn != st.ExtraReplayThisTurn)
                        card.ExtraReplayThisTurn = st.ExtraReplayThisTurn;
#endif

                    if (card.IsDupe != st.IsDupe)
                        card.IsDupe = st.IsDupe;

#if GAME_BETA
                    if (!ReferenceEquals(card.TransformIntoAfterPlay, st.TransformIntoAfterPlay))
                    {
                        card.TransformIntoAfterPlay = st.TransformIntoAfterPlay?.ToMutable();
                    }
#endif

                    // 附魔：必须走 CardCmd.Enchant —— 它内部会调 EnchantmentModel.ApplyInternal，
                    // 后者做两件事：写 Amount、把 Enchantment.Card 反向引用指回这张牌。
                    // 只写 `card.Enchantment = xxx` 的话反向引用是 null，附魔查不到自己的卡，
                    // 效果整段失效（2026-11-18 反馈「死归后附魔失效」就是这个）。
                    if (st.Enchantment == null)
                    {
                        if (card.Enchantment != null)
                            MegaCrit.Sts2.Core.Commands.CardCmd.ClearEnchantment(card);
                    }
                    else if (card.Enchantment == null
                             || card.Enchantment.GetType() != st.Enchantment.GetType()
                             || card.Enchantment.Amount != st.EnchantmentAmount)
                    {
                        if (card.Enchantment != null)
                            MegaCrit.Sts2.Core.Commands.CardCmd.ClearEnchantment(card);

                        MegaCrit.Sts2.Core.Commands.CardCmd.Enchant(
                            st.Enchantment.ToMutable(), card, st.EnchantmentAmount);
                    }
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] restore transient card state failed: " + e.Message);
                }
            }

            if (cleared > 0 || reapplied > 0)
                GD.Print($"[Subaru] card afflictions restored: cleared={cleared}, re-applied={reapplied}");
        }
    }

    /// <summary>
    /// 状态锁链的历史回滚。它的「本回合已绑几张」= 战斗历史里符合条件的条目数，
    /// 死归不回滚这些条目，计数就永远是满的（再抽牌不再施加魂缚）。
    /// 这里把多出来的条目削掉，并把内部标志 boundCardPlayed 一起写回。
    /// </summary>
    public void RestoreChainsState(ICombatState state)
    {
        var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
        if (history == null)
            return;

        var field = typeof(MegaCrit.Sts2.Core.Combat.History.CombatHistory)
            .GetField("_entries",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field?.GetValue(history) is not System.Collections.IList entries)
            return;

        foreach (var ps in Players)
        {
            var creature = ps.Player?.Creature;
            if (creature == null)
                continue;

            try
            {
                var kept = 0;
                var removed = 0;

                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not MegaCrit.Sts2.Core.Combat.History.Entries.CardAfflictedEntry entry)
                        continue;
                    if (entry.Actor != creature)
                        continue;
                    if (entry.Affliction is not MegaCrit.Sts2.Core.Models.Afflictions.Bound)
                        continue;
                    if (!entry.HappenedThisTurn(state))
                        continue;

                    kept++;
                    if (kept > ps.ChainsBoundThisTurn)
                    {
                        entries.RemoveAt(i);
                        i--;
                        removed++;
                    }
                }

                if (removed > 0)
                    GD.Print($"[Subaru] chains history trimmed: -{removed} (kept {kept - removed})");
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] chains history restore failed: " + e.Message);
            }

            try
            {
                WriteBoundCardPlayed(creature, ps.ChainsBoundCardPlayed);
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] boundCardPlayed restore failed: " + e.Message);
            }
        }
    }

    private static int CountBoundThisTurn(ICombatState state, Creature creature)
    {
        var entries = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History?.Entries;
        if (entries == null || creature == null)
            return 0;

        var count = 0;
        foreach (var entry in entries)
        {
            if (entry is MegaCrit.Sts2.Core.Combat.History.Entries.CardAfflictedEntry afflicted
                && afflicted.Actor == creature
                && afflicted.Affliction is MegaCrit.Sts2.Core.Models.Afflictions.Bound
                && afflicted.HappenedThisTurn(state))
            {
                count++;
            }
        }

        return count;
    }

    private static object? GetInternalData(Creature? creature)
    {
        var power = creature?.Powers
            .FirstOrDefault(p => p is MegaCrit.Sts2.Core.Models.Powers.ChainsOfBindingPower);

        if (power == null)
            return null;

        return typeof(MegaCrit.Sts2.Core.Models.PowerModel)
            .GetField("_internalData",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(power);
    }

    private static bool ReadBoundCardPlayed(Creature? creature)
    {
        var data = GetInternalData(creature);
        if (data == null)
            return false;

        return data.GetType()
            .GetField("boundCardPlayed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(data) is bool played && played;
    }

    private static void WriteBoundCardPlayed(Creature? creature, bool value)
    {
        var data = GetInternalData(creature);
        if (data == null)
            return;

        data.GetType()
            .GetField("boundCardPlayed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(data, value);
    }

    /// <summary>
    /// 重建/移除「睡着」特效（Zzz）。原版只在 Sleep() 里创建一次、在受击/死亡时 Stop 掉，
    /// 死归把 _isAwake 写回去并不会把节点一起变回来，所以这里按最终状态补齐。
    /// 支持：乐加维林族母、沉睡甲虫。
    /// </summary>
    private static void RefreshSleepVfx(Creature creature)
    {
        var monster = creature.Monster;
        if (monster == null)
            return;

        MegaCrit.Sts2.Core.Nodes.Vfx.NSleepingVfx? current = null;
        var isAwake = true;

        if (monster is MegaCrit.Sts2.Core.Models.Monsters.LagavulinMatriarch lagavulin)
        {
            current = lagavulin.SleepingVfx;
            isAwake = lagavulin.IsAwake;
        }
        else if (monster is MegaCrit.Sts2.Core.Models.Monsters.SlumberingBeetle beetle)
        {
            current = beetle.SleepingVfx;
            isAwake = beetle.IsAwake;
        }
        else
        {
            return;
        }

        // 醒着却还挂着 Zzz → 撤掉
        if (isAwake)
        {
            if (GodotObject.IsInstanceValid(current))
                current!.QueueFree();

            AssignSleepVfx(monster, null);
            return;
        }

        // 睡着但没有 Zzz → 按原版做法重建
        if (GodotObject.IsInstanceValid(current))
            return;

        var node = creature.GetCreatureNode();
        if (node == null)
            return;

        var marker = node.GetSpecialNode<Godot.Marker2D>("%SleepVfxPos");
        if (marker == null)
        {
            GD.Print("[Subaru] sleep vfx skipped: no %SleepVfxPos marker");
            return;
        }

        var vfx = MegaCrit.Sts2.Core.Nodes.Vfx.NSleepingVfx.Create(marker.GlobalPosition, true);
        if (vfx == null)
            return;

        marker.AddChild(vfx);
        vfx.Position = Godot.Vector2.Zero;
        AssignSleepVfx(monster, vfx);

        GD.Print($"[Subaru] sleep vfx recreated: {creature.Name}");
    }

    private static void AssignSleepVfx(
        object monster, MegaCrit.Sts2.Core.Nodes.Vfx.NSleepingVfx? vfx)
    {
        switch (monster)
        {
            case MegaCrit.Sts2.Core.Models.Monsters.LagavulinMatriarch lagavulin:
                lagavulin.SleepingVfx = vfx;
                break;
            case MegaCrit.Sts2.Core.Models.Monsters.SlumberingBeetle beetle:
                beetle.SleepingVfx = vfx;
                break;
        }
    }


    /// <summary>
    /// 把战斗历史截回存档点的长度。必须在所有「补账」动作（补能力 / 补诅咒 / 补遗物状态）之后调用，
    /// 这样连还原过程本身产生的条目也一起被抹掉。
    /// </summary>
    public void RestoreHistory()
    {
        try
        {
            if (!HistoryCaptured)
            {
                GD.Print("[Subaru] history restore skipped: snapshot has no history count");
                return;
            }

            var entries = RawHistoryEntries();
            if (entries == null)
                return;

            var before = entries.Count;
            if (before <= HistoryCount)
            {
                GD.Print($"[Subaru] history unchanged: {before} entries (savepoint {HistoryCount})");
                return;
            }

            while (entries.Count > HistoryCount)
                entries.RemoveAt(entries.Count - 1);

            GD.Print($"[Subaru] history truncated: {before} -> {entries.Count} entries");
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] restore history failed: " + e.Message);
        }
    }

    /// <summary>反射拿 <c>CombatHistory._entries</c>（公开的 Entries 只是只读 IEnumerable）。</summary>
    private static System.Collections.IList RawHistoryEntries()
    {
        var history = CombatManager.Instance?.History;
        if (history == null)
            return null;

        var field = typeof(CombatHistory).GetField(
            "_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return field?.GetValue(history) as System.Collections.IList;
    }

    /// <summary>
    /// 存档点那一刻这只生物身上有哪些能力（按类型）。
    ///
    /// 用途：区分两类能力——「存档点之前就在的」（回合开始效果已经跑过，不能再跑）
    /// 和「存档点之后才拿到、但因为[腐蚀]被保留下来」的（需要补跑一次回合开始效果，
    /// 否则宫廷小丑这种「每回合四选一」在死归当回合就没有选卡环节）。
    /// </summary>
    public HashSet<Type> CapturedPowerTypes(Creature creature)
    {
        var cs = Creatures.FirstOrDefault(c =>
            ReferenceEquals(c.Creature, creature) || ReferenceEquals(c.Captured, creature));

        return cs == null
            ? new HashSet<Type>()
            : cs.Powers.Where(p => p.PowerType != null).Select(p => p.PowerType!).ToHashSet();
    }

    public void RestoreResources()
    {
        foreach (var ps in Players)
        {
            try
            {
                var pcs = ps.Player.PlayerCombatState;
                if (pcs == null)
                    continue;

                // 能量按**上限差**平移，不是直接覆盖：上限会随「跨死归保留的能力」变化
                // （强欲的契约 ModifyMaxEnergy +1），而快照记的是上限还没变那一瞬间的数值。
                // 平移能同时保住「超上限」的部分（古茶具套装战斗开始 +2 能量）。
                var maxDelta = pcs.MaxEnergy - ps.MaxEnergy;
                var energy = Math.Max(0, ps.Energy + maxDelta);

                pcs.Energy = energy;
                pcs.Stars = ps.Stars;

                if (maxDelta != 0)
                    GD.Print($"[Subaru] resources restored: energy={energy} (snapshot {ps.Energy}, max {ps.MaxEnergy}->{pcs.MaxEnergy}), stars={ps.Stars}");
                else
                    GD.Print($"[Subaru] resources restored: energy={energy}, stars={ps.Stars}");
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] restore resources failed: " + e);
            }
        }
    }

    /// <summary>
    /// 往牌堆里放一张牌。手牌有 10 张上限：塞不下时改放抽牌堆顶，
    /// 下次抽牌自然回到手上——绝不能直接丢弃，那会让整局少一张牌。
    /// </summary>
    public static async Task AddCardSafely(CardPile pile, CardModel card, PlayerCombatState pcs)
    {
        if (pcs != null && pile.Type == PileType.Hand && pcs.Hand.Cards.Count >= CardPile.MaxCardsInHand)
        {
            GD.Print($"[Subaru] hand full ({pcs.Hand.Cards.Count}), card pushed to draw: {card.Id}");
            await CardPileCmd.Add(card, pcs.DrawPile, CardPilePosition.Top, null, false);
            return;
        }

        await CardPileCmd.Add(card, pile, CardPilePosition.Top, null, false);
    }

    private static async Task RestorePile(
        CardPile pile, List<CardModel> target, HashSet<CardModel> alive, bool exactOrder,
        PlayerCombatState pcs = null, ICombatState state = null, Player player = null,
        Dictionary<CardModel, CardExtraState> states = null)
    {
        try
        {
            // 0) 快照里的牌可能在存档点之后被「变形/消耗」掉了（例如契约·贝蒂把打击变成迷尼亚）。
            //    那种实例的 HasBeenRemovedFromState 已经是 true，再 Add 进去也活不过来，
            //    所以这里换成同原型的新实例，牌数才不会少。
            var resolved = new List<CardModel>(target.Count);
            foreach (var card in target)
            {
                if (card == null)
                    continue;

                // 本场已经打过的[腐蚀]牌：**不管它有没有被销毁**，都不还原。
                //   · 能力牌打出后实例被销毁 → 不重建，手牌里不会冒出来
                //   · 技能/攻击牌打出后实例还活着（在弃牌堆）→ 不搬回手牌；稍后由 CorrodedCards.Purge 丢进消耗堆
                // 不拦这一层会让「一场只能打一次」失效（牌回到手上），
                // 也不能塞进牌组补救——牌组里原本那张还在，塞进去就成了复制。
                // 判据必须用**实例引用**（IsUsed）：按原型判会把同名的第二张合法副本一起吞掉。
                if (CorrodedCards.IsUsed(card))
                {
                    GD.Print($"[Subaru] corroded card used, not restored: {card.Id}");
                    continue;
                }

                if (!card.HasBeenRemovedFromState)
                {
                    resolved.Add(card);
                    continue;
                }

                // 这张牌在快照里的可变状态（费用覆盖、附着、关键词…）
                CardExtraState st = null;
                states?.TryGetValue(card, out st);

                var fresh = Rebuild(card, state, player, st);
                if (fresh != null)
                {
                    resolved.Add(fresh);
                    GD.Print($"[Subaru] rebuilt destroyed card: {card.Id}");
                }
                else
                {
                    GD.PushWarning($"[Subaru] cannot rebuild destroyed card: {card.Id}");
                }
            }

            // 1) 清掉「快照里不存在」的临时牌（战斗中生成的），只动这一类
            foreach (var card in pile.Cards.ToList())
            {
                if (alive.Contains(card))
                    continue;

                // 魔女瘴气必须跨死归保留：它是「死亡记账」，不是本轮战斗生成的一次性牌
                if (IsPersistentCard(card))
                {
                    GD.Print("[Subaru] kept persistent card: " + card.Id);
                    continue;
                }

                try
                {
                    await CardPileCmd.RemoveFromCombat(card, false);
                    GD.Print("[Subaru] removed temp card: " + card.Id);
                }
                catch (System.Exception e)
                {
                    GD.PushError("[Subaru] remove temp card failed: " + e);
                }
            }

            if (exactOrder)
            {
                // 倒序往栈顶插 → 最终顺序与快照一致，且全程走官方命令（归属/UI 同步）
                for (var i = resolved.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        await CardPileCmd.Add(resolved[i], pile, CardPilePosition.Top, null, false);
                    }
                    catch (System.Exception e)
                    {
                        GD.PushError("[Subaru] reorder add failed: " + e);
                    }
                }

                return;
            }

            // 2) 手牌 / 出牌堆：只补缺失的牌，不重排
            foreach (var card in resolved)
            {
                if (pile.Cards.Contains(card))
                    continue;

                try
                {
                    await AddCardSafely(pile, card, pcs);
                }
                catch (System.Exception e)
                {
                    GD.PushError("[Subaru] move card failed: " + e);
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] restore pile failed: " + e);
        }
    }

    /// <summary>
    /// 清掉玩家所有牌上的束缚标记（魂缚 / 缠绕这一类 affliction）。
    /// 直接用 CardCmd.ClearAffliction，和原版 Power 里 AfterRemoved 的做法一致。
    /// </summary>
    private static void ClearBoundCards(Creature creature)
    {
        try
        {
            var pcs = creature.Player?.PlayerCombatState;
            if (pcs == null)
                return;

            foreach (var card in pcs.AllCards)
            {
                try
                {
                    MegaCrit.Sts2.Core.Commands.CardCmd.ClearAffliction(card);
                }
                catch
                {
                    // 单张牌失败不影响其它牌
                }
            }

            GD.Print($"[Subaru] cleared card afflictions: {creature.Name}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] clear afflictions failed: " + e.Message);
        }
    }

    /// <summary>
    /// 按原型重建一张已经从状态里消失的牌（保留升级状态 + 「本场费用覆盖」）。
    ///
    /// 费用覆盖必须在这里补：像宝石面具那样「这张牌在本场战斗可以免费打出」的效果，
    /// 写在卡牌的 <c>CardEnergyCost</c> 上（LocalCostModifier，Expiration = EndOfCombat），
    /// <c>state.CreateCard(原型)</c> 拿不到它 —— 不补的话那张牌回档后就变回满费，
    /// 而面具只在第一回合发牌，整场都不会再补（2026-11-17 确诊）。
    /// </summary>
    private static CardModel Rebuild(CardModel dead, ICombatState state, Player player, CardExtraState st = null)
    {
        try
        {
            if (state == null || player == null || dead.CanonicalInstance == null)
                return null;

            var fresh = state.CreateCard(dead.CanonicalInstance, player);
            if (fresh != null && fresh.CurrentUpgradeLevel != dead.CurrentUpgradeLevel)
            {
                // 走引擎的升降级流程（CardCmd），顺手覆盖多级强化（劳安那种 MaxUpgradeLevel > 1）；
                // 直接调 UpgradeInternal() 还有跨版本签名风险，见 CardUpgradeSync 的注释
                Extensions.CardUpgradeSync.SetLevel(fresh, dead.CurrentUpgradeLevel);
            }

            ApplyCombatCostOverride(fresh, st);

            // 重建出来的新实例，卡自己的成长计数也要按快照写回
            if (fresh is IRewindCardState rewindState && st != null && st.HasRewindCounter)
            {
                try
                {
                    rewindState.RestoreRewindCounter(st.RewindCounter);
                }
                catch (System.Exception e)
                {
                    GD.PushWarning("[Subaru] rebuild card rewind counter failed: " + e.Message);
                }
            }

            return fresh;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] rebuild card failed: " + e);
            return null;
        }
    }

    /// <summary>把快照里记下的「本场费用覆盖」写回重建出来的牌。</summary>
    public static void ApplyCombatCostOverride(CardModel card, CardExtraState st)
    {
        if (card == null || st == null)
            return;

        try
        {
            if (st.CombatEnergyCost is int energy
                && card.EnergyCost != null
                && card.EnergyCost.GetResolved() != energy)
            {
                // reduceOnly: 只还原「变便宜」的覆盖，不把临时的涨价也固化下来
                card.EnergyCost.SetThisCombat(energy, true);
                GD.Print($"[Subaru] restored combat cost: {card.Id} -> {energy}");
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] restore combat energy cost failed: " + e.Message);
        }

        try
        {
            if (st.CombatStarCost is int stars && card.GetStarCostThisCombat() != stars)
            {
                card.SetStarCostThisCombat(stars);
                GD.Print($"[Subaru] restored combat star cost: {card.Id} -> {stars}");
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] restore combat star cost failed: " + e.Message);
        }
    }

    /// <summary>
    /// 按规范模型重建一瓶已经从状态里移除的药水（回档时用）。
    /// 直接用旧实例不行：喝药时它被 <c>RemoveBeforeUse</c> 摘出了玩家药水列表，
    /// 那个实例再塞回格子是个「半死」的药水，点开/使用会出问题。
    /// </summary>
    private static PotionModel? RebuildPotion(PotionModel dead)
    {
        try
        {
            var canonical = dead.CanonicalInstance;
            if (canonical == null)
                return null;

            var fresh = canonical.ToMutable();
            if (fresh == null)
                return null;

            fresh.CanonicalInstance = canonical;
            fresh.HasBeenRemovedFromState = false;
            return fresh;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] rebuild potion failed: " + e);
            return null;
        }
    }

    public async Task RestoreFull(PlayerChoiceContext ctx, ICombatState state)
    {
        try
        {
            state.RoundNumber = RoundNumber;
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] restore round number failed: {e}");
        }

        // 同上：先清掉存档点之后召唤出来的单位
        PurgeCreaturesNotInSnapshot(state);

        foreach (var cs in Creatures)
            await RestoreCreature(cs, ctx, state);

        foreach (var ps in Players)
        {
            try
            {
                var pcs = ps.Player.PlayerCombatState;
                if (pcs == null)
                    continue;

                pcs.TurnNumber = ps.TurnNumber;

                // 和 RestoreResources 同一套算法：能量按上限差平移（见 PlayerState.MaxEnergy 的注释）
                pcs.Energy = Math.Max(0, ps.Energy + (pcs.MaxEnergy - ps.MaxEnergy));
                pcs.Stars = ps.Stars;
            }
            catch (Exception e)
            {
                GD.PushError($"[Subaru] restore player failed: {e}");
            }
        }
    }


    /// <summary>让战斗房间重新计算所有单位的位置（修复复活的怪堆在中间）。</summary>
    private static void RepositionCreatures(Creature creature)
    {
        try
        {
            Godot.Node? node = creature.GetCreatureNode();
            if (node == null)
                return;

            Godot.Node? parent = node.GetParent();
            while (parent != null && parent.GetType().Name != "NCombatRoom")
                parent = parent.GetParent();

            if (parent == null)
            {
                GD.PushWarning("[Subaru] combat room node not found");
                return;
            }

            var method = parent.GetType().GetMethod("PositionCreaturesWithSlots");
            if (method == null)
            {
                GD.PushWarning("[Subaru] PositionCreaturesWithSlots not found");
                return;
            }

            var nodesProp = parent.GetType().GetProperty("CreatureNodes");
            var list = nodesProp?.GetValue(parent);
            if (list == null)
                return;

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(node.GetType());
            var typed = System.Activator.CreateInstance(listType);
            var add = listType.GetMethod("Add");
            foreach (var n in (System.Collections.IEnumerable)list)
                add!.Invoke(typed, new object[] { n });

            method.Invoke(parent, new object[] { typed! });
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] reposition failed: " + e);
        }
    }


    /// <summary>
    /// 仪式兽的背景演出（老板身后的花瓣 / 光晕 / 骷髅）。
    /// 它由 <c>NCeremonialBeastBgVfx.UpdateVfxAndMusic</c> 每帧按 <c>IsInSecondPhase</c> 决定，
    /// 但这个方法只在 CombatStateChanged 时才被调用，而且内部两个开关（_isGlowOn / _areSkullsOn）
    /// 一旦置真就永不复位。所以回档后要手动：清开关 + 主动刷新一次。
    /// </summary>
    private static void RefreshCeremonialBg(ICombatState state)
    {
        try
        {
            if (state is not MegaCrit.Sts2.Core.Combat.CombatState combatState)
                return;

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null)
                return;

            var node = FindByTypeName(room, "NCeremonialBeastBgVfx");
            if (node == null)
                return;

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            var type = node.GetType();
            type.GetField("_isGlowOn", Flags)?.SetValue(node, false);
            type.GetField("_areSkullsOn", Flags)?.SetValue(node, false);
            type.GetMethod("UpdateVfxAndMusic", Flags)?.Invoke(node, new object[] { combatState });

            GD.Print("[Subaru] ceremonial bg vfx refreshed");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ceremonial bg refresh failed: " + e.Message);
        }
    }

    private static Godot.Node? FindByTypeName(Godot.Node root, string typeName)
    {
        foreach (var child in root.GetChildren())
        {
            if (child.GetType().Name == typeName)
                return child;

            var hit = FindByTypeName(child, typeName);
            if (hit != null)
                return hit;
        }

        return null;
    }

    /// <summary>不随死归重置的能力：魔女遗香 / 癫狂 / 傲慢。</summary>
    private static bool IsPersistentPower(System.Type? t)
    {
        return t == typeof(WitchFragrancePower)
            || t == typeof(MadnessPower)
            || t == typeof(PridePower);
    }

    /// <summary>
    /// 不随死归重置的牌：
    ///   · 魔女瘴气（跨轮记账，攒够层数才会触发癫狂）
    ///   · 登记过的「一次性资源换来的牌」（虚饰魔女变出来的稀有牌等）
    /// </summary>
    private static bool IsPersistentCard(CardModel card)
    {
        return card is CurseWitchMiasma || RewindPersistentCards.IsMarked(card);
    }

    /// <summary>
    /// 清掉「存档点之后才出现」的单位：它们不在快照里，却还挂在战斗状态和场景树上。
    /// 典型情况是[巨斧机器人]被击杀时召唤的新本体 —— 不清就会出现
    /// 「复活的原体 + 多出来的一张立绘」。
    ///
    /// 只按快照里的实例引用比对：死归是同一进程内回档，引用一直有效。
    /// </summary>
    private void PurgeCreaturesNotInSnapshot(ICombatState state)
    {
        try
        {
            var known = new HashSet<Creature>();
            foreach (var cs in Creatures)
            {
                if (cs.Creature != null)
                    known.Add(cs.Creature);
            }

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;

            foreach (var live in state.Creatures.ToList())
            {
                if (live == null || live.IsPlayer || known.Contains(live))
                    continue;

                // 摘状态 + 把节点交还给引擎（RemoveCreatureNode 是引擎自己的入口）。
                // 只摘状态不退休节点 → 节点会留在房间里且仍然可见，于是每次死归都多一个
                // 立绘叠在同一个 slot 上（12:38 那次 live node 攒到 4 个）。
                var node = live.GetCreatureNode();

                if (state is MegaCrit.Sts2.Core.Combat.CombatState combatState)
                    combatState.RemoveCreature(live, true);

                RetireNode(node);

                GD.Print("[Subaru] purged post-savepoint creature: " + live.Name);
            }

        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] purge creatures failed: " + e.Message);
        }
    }


    /// <summary>
    /// 把一只单位的节点彻底退役。顺序很重要，这是照着引擎自己的死亡收尾写的：
    ///
    /// 1. <c>NCombatRoom.RemoveCreatureNode(node)</c>
    ///      从 _creatureNodes 移到 _removingCreatureNodes（不再参与导航/排序 → 不会再有
    ///      ObjectDisposedException），并 fire-and-forget 一个移除任务。
    /// 2. 如果节点**不在播死亡动画** → 它永远不会自己消失
    ///      （引擎是在 NCreature.&lt;AnimDie&gt; 结束时才 QueueFreeSafely 的），
    ///      所以这里必须自己释放，否则就是一张永远可见的幽灵立绘 —— 12:38 那种重叠。
    ///    如果**在播死亡动画** → 交给它自己播完释放，我们不动。
    ///
    /// 反面教材（都踩过）：
    ///   · 先 QueueFree 不先摘列表 → 列表里留 disposed 对象 → 下一次排序就崩（12:27）
    ///   · 只摘状态不处理节点       → 幽灵立绘永远可见（12:38、12:43）
    /// </summary>
    private static void RetireNode(MegaCrit.Sts2.Core.Nodes.Combat.NCreature? node)
    {
        try
        {
            if (node == null || !Godot.GodotObject.IsInstanceValid(node))
                return;

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null)
                return;

            // 先从房间的「活跃节点」列表里摘掉（不在 _creatureNodes 里就不会再参与
            // UpdateCreatureNavigation 的排序，不会出现 disposed 崩溃）
            room.RemoveCreatureNode(node);

            // 然后无条件释放。**不能**指望 AnimDie 自己收：
            // 千足虫的死亡动画由 NDecimillipedeSegmentDriver 驱动，
            // 回档打断后 IsPlayingDeathAnimation 会永久停在 true、AnimDie 再也不会跑完，
            // 节点就永远留在屏幕上（12:49 那条「已死亡的贴图没被消除」）。
            node.Visible = false;
            node.QueueFree();
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] retire node failed: " + e.Message);
        }
    }

    /// <summary>
    /// 严格回档的收尾：场上每一只怪物都必须是快照里某一条的「当前实例」。
    /// 多出来的（死亡召唤物、重建中途冒出来的、任何来源）一律清掉，
    /// 保证回档后的敌人集合和快照时刻完全一致。
    /// </summary>
    private void EnforceExactMonsterSet(ICombatState state)
    {
        try
        {
            var allowed = new HashSet<Creature>();
            foreach (var cs in Creatures)
            {
                if (cs.Creature != null)
                    allowed.Add(cs.Creature);
            }

            var extras = state.Creatures
                .Where(c => c.IsMonster && !allowed.Contains(c))
                .ToList();

            if (extras.Count == 0)
            {
                GD.Print($"[Subaru] strict restore: monster set matches snapshot ({allowed.Count - Players.Count} monster(s))");
                return;
            }

            foreach (var extra in extras)
            {
                var extraNode = extra.GetCreatureNode();

                if (state is MegaCrit.Sts2.Core.Combat.CombatState combatState)
                {
                    try
                    {
                        combatState.RemoveCreature(extra, true);
                    }
                    catch (Exception e)
                    {
                        GD.PushWarning("[Subaru] strict purge detach failed: " + e.Message);
                    }
                }

                RetireNode(extraNode);
                GD.Print("[Subaru] strict purge extra monster: " + extra.Name);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] strict restore failed: " + e.Message);
        }
    }




    /// <summary>
    /// 方案 A：怪物不再「复活旧实例」，改成按模型重建一只新的。
    ///
    /// 为什么：`StockPower.AfterDeath` 这类死亡流程会
    /// `ModelDb.Monster&lt;T&gt;().ToMutable()` 造新实例 → 进场 → 先 `SetNodeVisible(false)`
    /// → 延迟 `deathAnimLength` 再显示。整个过程留下「旧实例 + 隐藏节点 + 排队中的任务」，
    /// 只靠节点层修补永远会漏（12:16 立绘重复、12:18 怪消失都是这个来源）。
    ///
    /// 判定：只要这只怪不在场 / 已死 / 节点没了或不可见，就走重建。
    /// 重建走引擎自己的进场入口 <see cref="MegaCrit.Sts2.Core.Commands.CreatureCmd.Add(MonsterModel, ICombatState, CombatSide, string)"/>，
    /// 和 StockPower 用的完全同一条路径，招式状态机、节点、槽位都由引擎自己配好。
    /// </summary>
    /// <summary>
    /// 只重建「节点」，不动 Creature 身份。
    ///
    /// 用于：单位还活着（或被复活流程接管），但它的节点丢了 / 被隐藏 / 卡在死亡动画里。
    /// 千足虫的节被反复重新接续时就是这种状态 —— 只写数值会让屏幕上一直留着一具枯萎的贴图，
    /// 而重建 Creature 又会破坏分节顺序和接续关系。
    ///
    /// <c>NCombatRoom.AddCreature</c> 是 UI 层的「给这个 Creature 建节点」入口，
    /// 不会重跑 AfterAddedToRoom / 招式状态机，所以对活着的单位是安全的。
    /// </summary>
    private static void RebuildNodeOnly(Creature creature, MegaCrit.Sts2.Core.Nodes.Combat.NCreature? staleNode)
    {
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null)
                return;

            RetireNode(staleNode);
            room.AddCreature(creature);
            GD.Print("[Subaru] node rebuilt (creature kept): " + creature.Name);
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] rebuild node failed: " + e.Message);
        }
    }

    private async Task<bool> TryRebuildMonsterAsync(CreatureState cs, PlayerChoiceContext ctx, ICombatState state)
    {
        try
        {
            var old = cs.Creature;

            if (old == null || old.Monster == null)
                return false;

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            var liveNode = old.GetCreatureNode();

            var inCombat = state.Creatures.Contains(old);
            var nodeDying = liveNode != null && liveNode.IsPlayingDeathAnimation;
            var nodeBroken = liveNode == null || !liveNode.Visible || nodeDying;

            // ① 已经死掉、但**仍留在战斗里**的分节类单位（千足虫）：
            //    这类靠原版复活流程（ReattachPower / SetCurrentHp）救活，
            //    绝对不能重建 —— 重建会丢掉节的顺序、_deadState 和整条接续关系。
            //    当年验证过：ShouldFadeAfterDeath=false 的节死亡后仍然 IsDead 地留在场上。
            if (inCombat && old.IsDead)
            {
                if (nodeBroken)
                    RebuildNodeOnly(old, liveNode);

                return false;
            }

            // ② 活着但节点坏了：只换节点，保留 Creature 身份（千足虫节卡住就是这种）
            if (inCombat)
            {
                if (!nodeBroken)
                    return false;

                RebuildNodeOnly(old, liveNode);
                return false;
            }

            // ③ 真正被移出战斗（召唤物、彻底死掉的怪）：才重建实例

            // 必须从「规范模型」复制一份可变实例。
            // 直接 old.Monster.ToMutable() 会抛
            // InvalidOperationException: Mutable model of type ... used in incorrect place
            // —— 场上那只的 Monster 本身已经是 mutable 了（12:24 那次重建失败就是这个）。
            var sourceModel = (cs.Captured?.Monster ?? old.Monster)!;
            var canonical = ModelDb.Get(sourceModel.GetType()) as MonsterModel;
            var mutable = canonical?.ToMutable() as MonsterModel;
            if (mutable == null)
            {
                GD.PushWarning("[Subaru] rebuild: no canonical monster for " + sourceModel.GetType().Name);
                return false;
            }

            // 清掉旧实例：状态里摘掉、节点移出场景
            if (state is MegaCrit.Sts2.Core.Combat.CombatState combatState && inCombat)
            {
                try
                {
                    combatState.RemoveCreature(old, true);
                }
                catch (Exception e)
                {
                    GD.PushWarning("[Subaru] rebuild: detach old creature failed: " + e.Message);
                }
            }

            // 旧实例的节点也要退休，否则它会以可见状态留在房间列表里，
            // 和新重建的怪叠在同一个 slot 上。
            RetireNode(liveNode);

            var fresh = await MegaCrit.Sts2.Core.Commands.CreatureCmd.Add(
                mutable, state, MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, cs.SlotName ?? "");

            if (fresh == null)
            {
                GD.PushWarning("[Subaru] rebuild: CreatureCmd.Add returned null for " + old.Name);
                return false;
            }

            cs.Creature = fresh;

            GD.Print($"[Subaru] monster rebuilt: {fresh.Name} slot={cs.SlotName} "
                     + $"(old hp={old.CurrentHp} dead={old.IsDead})");

            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] rebuild monster failed: " + e.Message);
            return false;
        }
    }

    private async Task RestoreCreature(CreatureState cs, PlayerChoiceContext ctx, ICombatState state)
    {
        // 方案 A：怪物优先走「按模型重建」。死亡/召唤流程留下的旧实例、隐藏节点、
        // 排队中的延迟显示，在节点层是补不干净的，重建一只干净的新怪最省事。
        var rebuilt = false;
        if (cs.IsMonster)
            rebuilt = await TryRebuildMonsterAsync(cs, ctx, state);

        // 「死没死」和「在不在场」是两件事：
        // - 千足虫分节死掉后仍然留在战斗里（ShouldFadeAfterDeath=false），只是 IsDead；
        //   这种必须走原版复活流程，否则只是把血写回去——贴图还是枯萎的，而且不可选中。
        // - 真正被移出战斗的（召唤物之类）才需要重新 Add。
        var inCombat = rebuilt || state.Creatures.Contains(cs.Creature);
        var nodeRecreated = false;
        var wasDead = cs.Creature.IsDead || cs.Creature.CurrentHp <= 0m;

        if (!inCombat)
        {
            try
            {
                // 绝对不能用 CreatureCmd.Add 重新放回一只「已经打过」的怪：
                // 它会重跑 AfterAddedToRoom → 重建招式状态机 → 抛
                // InvalidOperationException: XXX's move state machine has already been set，
                // 于是那只怪既没回场也没血条。
                // CombatState.AttachCreature 是低层挂载：只写 CombatState 引用 + 分配 id，不动状态机。
                if (state is MegaCrit.Sts2.Core.Combat.CombatState combatState)
                {
                    combatState.AttachCreature(cs.Creature);
                    GD.Print("[Subaru] reattached creature: " + cs.Creature.Name);

                    // AttachCreature 只写 CombatState 引用 + CombatId，**不会**把怪放回 _enemies。
                    // 而 CombatState.Creatures = _allies.Concat(_enemies)，HittableEnemies 也是
                    // 从 _enemies 过滤出来的——不放回去就是「在场上但点不到、没血条」。
                    try
                    {
                        var fieldName = cs.IsMonster ? "_enemies" : "_allies";
                        var field = typeof(MegaCrit.Sts2.Core.Combat.CombatState).GetField(
                            fieldName,
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);

                        if (field?.GetValue(combatState) is System.Collections.IList list
                            && !list.Contains(cs.Creature))
                        {
                            list.Add(cs.Creature);
                            GD.Print($"[Subaru] force-added {cs.Creature.Name} to {fieldName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PushWarning("[Subaru] force-add failed: " + ex.Message);
                    }
                }
                else
                {
                    cs.Creature.CombatState = state;
                    await CreatureCmd.Add(cs.Creature);
                }

                if (!string.IsNullOrEmpty(cs.SlotName))
                    cs.Creature.SlotName = cs.SlotName;

                // 千足虫这类「预设槽位」的战斗不允许改回合顺序，直接抛
                // InvalidOperationException: Cannot modify turn order of a combat with pre-set slots
                // 顺序本来也就是固定的，跳过即可，别让它把整段复活流程带崩。
                try
                {
                    if (cs.EnemyIndex >= 0)
                        state.SetEnemyIndex(cs.Creature, cs.EnemyIndex);
                }
                catch (Exception ex)
                {
                    GD.PushWarning("[Subaru] SetEnemyIndex skipped: " + ex.Message);
                }

                try
                {
                    state.SortEnemiesBySlotName();
                }
                catch (Exception ex)
                {
                    GD.PushWarning("[Subaru] SortEnemiesBySlotName skipped: " + ex.Message);
                }

                try
                {
                    var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;

                    var revivedNode = cs.Creature.GetCreatureNode();

                    if (revivedNode == null)
                    {
                        // 离场时节点被销毁了（帝皇蟹的爪子就是这种），
                        // 必须按原版入口重建一个，否则模型回来了屏幕上看不见。
                        room?.AddCreature(cs.Creature);
                        revivedNode = cs.Creature.GetCreatureNode();
                        GD.Print($"[Subaru] creature node recreated: {cs.Creature.Name}, ok={revivedNode != null}");
                    }
                    else
                    {
                        // 节点还在，但可能被「死亡」流程藏起来了：
                        // StockPower.AfterDeath 会先 SetNodeVisible(false) 再延迟显示新本体。
                        // 我们等结算完才回档，此时旧本体的节点还在这个隐藏状态，
                        // 不显式打开就是「有单位、没立绘」（12:16 那次怪消失）。
                        revivedNode.Visible = true;
                        revivedNode.ProcessMode = Godot.Node.ProcessModeEnum.Inherit;
                        cs.Creature.SetNodeVisible(true);
                        GD.Print($"[Subaru] revived node re-shown: {cs.Creature.Name}");
                    }

                    if (revivedNode != null && cs.HasNodePosition)
                    {
                        revivedNode.GlobalPosition = cs.NodePosition;
                        GD.Print("[Subaru] revived node repositioned");
                    }

                    if (revivedNode != null)
                    {
                        if (cs.HasNodeScale)
                            revivedNode.Scale = cs.NodeScale;
                    }

                    nodeRecreated = true;
                }
                catch (System.Exception ex)
                {
                    GD.PushError("[Subaru] node reposition failed: " + ex);
                }

                inCombat = state.Creatures.Contains(cs.Creature);
                GD.Print($"[Subaru] revived creature: {cs.Creature.Name}, inCombat={inCombat}");
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] revive creature failed: " + e);
            }
        }

        try
        {
            cs.Creature.MaxHp = cs.MaxHp;
            cs.Creature.Block = cs.Block;
            cs.Creature.HpDisplay = cs.HpDisplay;

            if (wasDead && cs.Hp > 0m && inCombat)
            {
                // 原版复活路径：CreatureCmd.SetCurrentHp 会触发 StartReviveAnim（回贴图/复活动画）
                // 并清掉死亡标记，怪才重新可选中。直接写 CurrentHp 只改数字，动画和可选状态都不动。
                await CreatureCmd.SetCurrentHp(cs.Creature, cs.Hp);
                GD.Print("[Subaru] revived via SetCurrentHp: " + cs.Creature.Name);

                // 死亡时节点调了 DisableInteractionForDeath()：把 _interactionAllowedBecauseAlive
                // 置 false，并隐藏 _stateDisplay（血条 + 意图就在里面）。光改模型不会自动恢复，
                // 必须把这个私有标记翻回来再刷一次，否则就是「活着但没血条、点不到」。
                try
                {
                    if (cs.Creature.GetCreatureNode() is MegaCrit.Sts2.Core.Nodes.Combat.NCreature node)
                    {
                        typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature)
                            .GetField("_interactionAllowedBecauseAlive",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic)
                            ?.SetValue(node, true);

#if GAME_BETA
        #if GAME_BETA
                node.SetIsInteractable(true, true);
#else
                node.IsInteractable = true;
#endif
#else
                        node.IsInteractable = true;
#endif
                        GD.Print("[Subaru] node interactability restored: " + cs.Creature.Name);
                    }
                }
                catch (Exception ex)
                {
                    GD.PushWarning("[Subaru] interactability restore failed: " + ex.Message);
                }
            }
            else
            {
                cs.Creature.CurrentHp = cs.Hp;
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] restore hp failed: {e}");
        }

        // 血条 UI 不会跟着模型血量自动重算（瀑布巨兽蓄力时是 ∞），强制刷一次
        Subaru.SubaruCode.UI.RewindHealthMask.RefreshBar(cs.Creature);

        // 动画状态对所有怪都回放：节点重建的要等一帧才可用；
        // 没重建的（瀑布巨兽这种蓄力姿势）也必须按存档点那一帧重置回去，
        // 否则模型和血条都回去了，贴图还停在蓄力。
        if (nodeRecreated || !string.IsNullOrEmpty(cs.AnimName))
        {
            try
            {
                if (nodeRecreated)
                    await Task.Delay(80);

                if (cs.Creature.GetCreatureNode() is { } fresh)
                {
                    var spine = fresh.SpineAnimation;
                    GD.Print(
                        $"[Subaru] anim restore try: {cs.Creature.Name}, valid={spine.IsValid}, " +
                        $"saved={cs.AnimName ?? "<none>"}");

                    if (spine.IsValid && !string.IsNullOrEmpty(cs.AnimName))
                    {
                        spine.SetAnimation(cs.AnimName, cs.AnimLoop, 0);

                        var entry = spine.GetCurrentTrack(0);
                        if (entry != null)
                        {
                            if (cs.AnimTime >= 0f)
                                entry.SetTrackTime(cs.AnimTime);

                            foreach (var queued in cs.AnimQueue)
                                spine.AddAnimation(queued, 0f, cs.AnimLoop, 0);
                        }

                        GD.Print($"[Subaru] node anim restored: {cs.Creature.Name} <- {cs.AnimName}");
                    }

                    // 蓄力姿势不在 track 0：WaterfallGiant.IncrementBuildUpAnimationTrack 是
                    // `PressureBuildupIdx++` 然后 `SetAnimation("_tracks/buildupN", 1, 1)`。
                    // 所以 track 1 也必须按存档点回放，存档点没有就把它淡出。
                    if (spine.IsValid)
                    {
                        if (!string.IsNullOrEmpty(cs.AnimTrack1))
                        {
                            spine.SetAnimation(cs.AnimTrack1, true, 1);
                            GD.Print($"[Subaru] buildup track restored: {cs.Creature.Name} <- {cs.AnimTrack1}");
                        }
                        else
                        {
                            try
                            {
                                spine.GetAnimationState().AddEmptyAnimation(1);
                                GD.Print($"[Subaru] buildup track cleared: {cs.Creature.Name}");
                            }
                            catch (Exception e0)
                            {
                                GD.PushWarning("[Subaru] clear buildup track failed: " + e0.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] anim restore failed: " + e.Message);
            }
        }

        // 实验体：阶段计数器要跟着回档，再补一次动画触发。
        // 它的贴图不是「换一张图」，而是 GenerateAnimator 里那批 b__83_* 回调按 Respawns
        // 选 idle_loop1/2/3——只把血量写回去，动画器还停在阶段二那一套，就成了
        // 「阶段二的贴图、阶段一的状态」。
        try
        {
            if (cs.Creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.TestSubject subject)
            {
                subject.Respawns = cs.MonsterRespawns;
                subject.ExtraMultiClawCount = cs.MonsterExtraMultiClawCount;

                if (cs.Creature.CurrentHp > 0m && inCombat)
                {
                    await CreatureCmd.TriggerAnim(cs.Creature, "Hit", 0f);
                    GD.Print($"[Subaru] test subject phase restored: respawns={subject.Respawns}");
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] test subject phase restore failed: " + e);
        }

        // 单体 Boss 的计数器（火箭）：写回它自己的标量字段
        if (cs.MonsterScalars != null && cs.Creature.Monster != null)
        {
            RestoreScalars(cs.Creature.Monster, cs.MonsterScalars);
            GD.Print($"[Subaru] monster scalars restored: {cs.Creature.Name}, fields={cs.MonsterScalars.Count}");
        }

        // 睡着特效（Zzz）：它是节点引用，不在快照里，必须按 IsAwake 重建/移除。
        // 原版是在 Sleep() 里 NSleepingVfx.Create(marker.GlobalPosition) 挂到 %SleepVfxPos 上。
        try
        {
            RefreshSleepVfx(cs.Creature);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] sleep vfx refresh failed: " + e.Message);
        }

        // 瀑布巨兽：蓄力段位计数器
        if (cs.Creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant giant)
        {
            try
            {
                giant.PressureBuildupIdx = cs.MonsterPressureIdx;
                GD.Print($"[Subaru] pressure buildup restored: {giant.PressureBuildupIdx}");
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] pressure buildup restore failed: " + e.Message);
            }
        }

        // 活着就该能点：死亡时节点调过 DisableInteractionForDeath()，关掉 _stateDisplay
        // （血条 + 意图）并把 _interactionAllowedBecauseAlive 置 false。这里统一修一次。
        try
        {
            if (cs.Creature.CurrentHp > 0m
                && cs.Creature.GetCreatureNode() is MegaCrit.Sts2.Core.Nodes.Combat.NCreature node)
            {
                typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature)
                    .GetField("_interactionAllowedBecauseAlive",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(node, true);

#if GAME_BETA
                node.SetIsInteractable(true, true);
#else
                node.IsInteractable = true;
#endif
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning("[Subaru] interactability restore failed: " + ex.Message);
        }

        // 位置回档：沙坑吞人靠的是把节点补间到沙虫嘴边（global_position:x），
        // 死归如果只回血不回位置，人就会「活着但贴图藏在嘴后面」——看着就是被吃了。
        // 这里对**所有**单位无条件写回存档点的坐标（不只是复活的那几只）。
        try
        {
            if (cs.HasNodePosition
                && cs.Creature.GetCreatureNode() is MegaCrit.Sts2.Core.Nodes.Combat.NCreature placed)
            {
                placed.GlobalPosition = cs.NodePosition;
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning("[Subaru] node position restore failed: " + ex.Message);
        }

        // 仪式兽：二阶段的唯一开关就是这两个布尔，必须写回去，
        // 否则背景 VFX（NCeremonialBeastBgVfx 读 IsInSecondPhase）会一直停在二阶段。
        // 放在能力还原之前：随后的能力变动会触发 CombatStateChanged，背景就能立刻刷新。
        try
        {
            if (cs.Creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast beast2)
            {
                beast2.IsInSecondPhase = cs.BeastSecondPhase;
                beast2.IsStunnedByPlowRemoval = cs.BeastStunnedByPlow;

                GD.Print($"[Subaru] ceremonial beast phase restored: phase2={cs.BeastSecondPhase}, stunned={cs.BeastStunnedByPlow}");

                // 背景 VFX 自己缓存了「光晕已开 / 骷髅已开」两个开关，一旦置真永不复位。
                // 不回滚这两个开关，回档到一阶段后再次进二阶段就什么都不播。
                RefreshCeremonialBg(state);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] beast phase restore failed: " + e.Message);
        }

        // 精确还原能力：多出来的移除，缺的补回，数值改回
        GD.Print($"[Subaru] powers restore start: {cs.Creature.Name}");
        try
        {
            var savedTypes = cs.Powers.Where(p => p.PowerType != null).Select(p => p.PowerType!).ToHashSet();

            foreach (var live in cs.Creature.Powers.ToList())
            {
                if (savedTypes.Contains(live.GetType()))
                    continue;

                // 「记事本」登记过的能力（魔女遗香、傲慢、腐蚀系）本来就不随死归回归，
                // 删掉它们等于每次死归都把遗香清零——这里必须放过。
                if (RewindLedger.IsRegistered(live.GetType()))
                    continue;

                // 魂缚（女王的 ChainsOfBinding）：束缚标记挂在**卡**上，而这个 power 自己没有
                // AfterRemoved 清理（原版是在回合结束的 BeforeSideTurnEnd 里清的）。
                // 我们把它删掉但标记还在 → 死归后手牌依旧打不出去。删之前先手动解绑。
                if (live is MegaCrit.Sts2.Core.Models.Powers.ChainsOfBindingPower)
                {
                    ClearBoundCards(cs.Creature);
                }

                await PowerCmd.Remove(live);
            }

            foreach (var saved in cs.Powers)
            {
                if (saved.PowerType == null)
                    continue;


                var live = cs.Creature.Powers.FirstOrDefault(p => p.GetType() == saved.PowerType);
                if (live != null)
                {
                    live.Amount = saved.Amount;
                    live.AmountOnTurnStart = saved.AmountOnTurnStart;
                    RestorePowerVars(live, saved);
                    RestorePowerFields(live, saved);
                    continue;
                }

                if (ModelDb.Get(saved.PowerType) is not PowerModel canonical)
                {
                    GD.PushWarning($"[Subaru] no canonical power for {saved.PowerType.Name}");
                    continue;
                }

                // 必须用 ToMutable() 生成这场战斗的副本：直接 Apply 规范模型会抛
                // CanonicalModelException: Canonical model of type X used in incorrect place
                await PowerCmd.Apply(ctx, canonical.ToMutable(), cs.Creature, saved.Amount, null, null, false);

                // 重建出来的能力只带着规范值，动态变量必须再盖一次
                // （否则像[缓慢]这类把计数放在 DynamicVars 里的能力会从 0 开始，或停在规范值）
                var recreated = cs.Creature.Powers.FirstOrDefault(p => p.GetType() == saved.PowerType);
                if (recreated != null)
                {
                    RestorePowerVars(recreated, saved);
                    RestorePowerFields(recreated, saved);
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] restore powers failed: {e}");
        }

        // 回档后「活着但点不到」几乎都是复活类 Power 卡在「正在复活」状态：
        // ReattachPower.ShouldAllowHitting 在 IsReviving 时返回 false，而这个内部标记由
        // DoReattach 写入，中途被打断就不会归位。这里不能干脆摘掉它（村雨要保留「接续」的状态），
        // 而是换成一份全新副本：新副本的 isReviving = false，既恢复可击中，又把状态留着。
        try
        {
            if (cs.Creature.CurrentHp > 0m && !cs.Creature.IsHittable)
            {
                foreach (var power in cs.Creature.Powers
                             .OfType<MegaCrit.Sts2.Core.Models.Powers.ReattachPower>()
                             .ToList())
                {
                    var amount = power.Amount;
                    await PowerCmd.Remove(power);

                    if (ModelDb.Get(typeof(MegaCrit.Sts2.Core.Models.Powers.ReattachPower)) is PowerModel canonical)
                    {
                        await PowerCmd.Apply(
                            ctx, canonical.ToMutable(), cs.Creature, amount, null, null, false);
                    }

                    GD.Print($"[Subaru] refreshed stuck ReattachPower on {cs.Creature.Name}");
                }

                GD.Print(
                    $"[Subaru] hittable after repair: {cs.Creature.Name} = {cs.Creature.IsHittable}, " +
                    $"inCombat={state.Creatures.Contains(cs.Creature)}, hp={cs.Creature.CurrentHp}/{cs.Creature.MaxHp}");
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] hittable repair failed: {e}");
        }

        GD.Print($"[Subaru] before sandpit block: {cs.Creature.Name}");
        // 沙坑（无厌沙虫）：**多实例** Power，必须按快照真正重建。
        // 规则：多删 / 少补 / 数值改回，让层数严格等于存档点那一刻。
        // 删除会触发 SandpitPower.AfterRemoved 的收尾演出（拖到嘴边 + EatPlayerTrigger + Kill），
        // 这段在静默期内被 SandpitSilencePatch 整体跳过，所以删是安全的。
        try
        {
            var savedSandpit = cs.Powers
                .Where(p => p.PowerType == typeof(MegaCrit.Sts2.Core.Models.Powers.SandpitPower))
                .ToList();

            var liveSandpit = cs.Creature.Powers
                .OfType<MegaCrit.Sts2.Core.Models.Powers.SandpitPower>()
                .ToList();

            // ① 多出来的实例删掉（从尾部删，保住靠前的计时）
            while (liveSandpit.Count > savedSandpit.Count)
            {
                var extra = liveSandpit[^1];
                liveSandpit.RemoveAt(liveSandpit.Count - 1);
                await PowerCmd.Remove(extra);
                GD.Print($"[Subaru] sandpit extra removed: {cs.Creature.Name}");
            }

            // ② 存档点里有、场上没有的补回来
            for (var i = liveSandpit.Count; i < savedSandpit.Count; i++)
            {
                if (ModelDb.Get(typeof(MegaCrit.Sts2.Core.Models.Powers.SandpitPower)) is not PowerModel sandpitCanonical)
                    break;

                await PowerCmd.Apply(
                    ctx, sandpitCanonical.ToMutable(), cs.Creature, savedSandpit[i].Amount, null, null, false);

                GD.Print($"[Subaru] sandpit re-applied: {cs.Creature.Name}, amount={savedSandpit[i].Amount}");
            }

            // ③ 数量对齐之后逐条把层数/回合初层数改回
            var finalSandpit = cs.Creature.Powers
                .OfType<MegaCrit.Sts2.Core.Models.Powers.SandpitPower>()
                .ToList();

            for (var i = 0; i < finalSandpit.Count && i < savedSandpit.Count; i++)
            {
                finalSandpit[i].Amount = savedSandpit[i].Amount;
                finalSandpit[i].AmountOnTurnStart = savedSandpit[i].AmountOnTurnStart;
            }

            if (savedSandpit.Count > 0 || finalSandpit.Count > 0)
            {
                GD.Print($"[Subaru] sandpit restored: {cs.Creature.Name}, live={finalSandpit.Count}, saved={savedSandpit.Count}");
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] sandpit rebuild failed: " + e);
        }

        // 意图放在最后：图标上的数字由「当前能力/血量」算出来，能力没回档就刷新会显示旧数字
        RestoreIntent(cs);

        // 诊断：回档全部结束后，把这只怪身上的能力列出来。
        // 蓄力态（瀑布巨兽的 ∞ 血条 + 蓄力姿势）是由 SteamEruptionPower 驱动的，
        // 如果第一回合的存档点里没有它、这里却打出来了，就说明它是后面某一步补上的。
        try
        {
            if (cs.IsMonster && cs.Creature.Powers.Count > 0)
            {
                var names = string.Join(
                    ", ",
                    cs.Creature.Powers.Select(p => $"{p.GetType().Name}x{p.Amount}"));

                GD.Print($"[Subaru] powers after restore: {cs.Creature.Name} = [{names}]");
            }
        }
        catch
        {
            // 诊断日志失败无所谓
        }
    }

    /// <summary>
    /// 回档敌人意图。必须用 SetMoveImmediate，它一次做三件事：
    /// 1) 写 NextMove（UI 显示的那一招）
    /// 2) MoveStateMachine.ForceCurrentState —— 状态机「记忆」也回到存档点，下次 RollMove 才从正确位置往后接
    /// 3) 刷新意图图标
    /// 只写 NextMove 会出现「图标是一招、实际打的是另一招」。
    /// forceTransition 必须是 true：存档点的招式通常还没执行过（MustPerformOnceBeforeTransitioning），
    /// 传 false 时引擎会直接忽略这次设置，什么都不做。
    /// </summary>
    private static readonly System.Reflection.BindingFlags ScalarFieldFlags =
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic;

    /// <summary>
    /// 每只怪「会随战斗变化、且应该随死归回滚」的标量字段白名单。
    /// 从 sts2.dll 里把所有 Monsters.* 的实例字段扒出来后逐只筛的：
    /// 只收状态位/计数器，排除纯外观（音效强度、生成动画开关）、静态常量、节点引用。
    /// 不在表里的怪就是没有可变状态，不需要处理。
    /// 注：仪式兽 / 女王 / 实验体 / 瀑布巨兽 走各自的专用还原代码，这里不重复列。
    /// </summary>
    private static readonly Dictionary<string, string[]> MonsterScalarSpecs = new()
    {
        ["Aeonglass"] = ["_additionalStrength", "_witherUpgradeCount"],
        ["Axebot"] = ["_stockOverrideAmount", "_shouldPlaySpawnAnimation"],
        ["BowlbugRock"] = ["_isOffBalance"],
        ["Chomper"] = ["_screamFirst"],
        ["CorpseSlug"] = ["_isRavenous", "_starterMoveIdx"],
        ["CrossbowRubyRaider"] = ["_isCrossbowReloaded"],
        ["CubexConstruct"] = ["_isBurrowed", "_isCharging"],
        ["FatGremlin"] = ["_isAwake"],
        ["FrogKnight"] = ["_hasBeetleCharged"],
        ["FuzzyWurmCrawler"] = ["_isPuffed"],
        ["GasBomb"] = ["_hasExploded"],
        ["GremlinMerc"] = ["_hasSpoken"],
        ["Inklet"] = ["_middleInklet"],
        ["KinFollower"] = ["_index"],
        ["KinPriest"] = ["_speechUsed"],
        ["KnowledgeDemon"] = ["_curseOfKnowledgeCounter", "_isBurnt"],
        ["LagavulinMatriarch"] = ["_isAwake", "_isShellAwake"],
        ["LivingFog"] = ["_bloatAmount"],
        ["LouseProgenitor"] = ["_curled"],
        ["MechaKnight"] = ["_isWoundUp"],
        ["Nibbit"] = ["_isFront", "_isAlone"],
        ["OwlMagistrate"] = ["_isFlying"],
        ["PhantasmalGardener"] = ["_enlargeTriggers"],
        ["PunchConstruct"] = ["_startsWithFastPunch", "_startingHpReduction"],
        ["SlumberingBeetle"] = ["_isAwake"],
        ["SneakyGremlin"] = ["_isAwake"],
        ["SoulFysh"] = ["_isInvisible"],
        ["SpinyToad"] = ["_isSpiny"],
        ["TheInsatiable"] = ["_hasLiquified"],
        ["TheObscura"] = ["_hasSummoned"],
        ["ThievingHopper"] = ["_isHovering"],
        ["Toadpole"] = ["_isFront"],
        ["ToughEgg"] = ["_hatched", "_isHatched"],
        ["Tunneler"] = ["_isStunned"],
        ["TwoTailedRat"] = ["_starterMoveIndex", "_turnsUntilSummonable", "_callForBackupCount"],
        ["Wriggler"] = ["_startStunned"],
    };

    /// <summary>
    /// 怪物身上指向招式状态的字段：存 StateId，回档时按 id 从状态机里找回同一个状态。
    /// </summary>
    private static readonly Dictionary<string, string[]> MonsterStateSpecs = new()
    {
        ["CeremonialBeast"] = ["_beastCryState"],
        ["DecimillipedeSegment"] = ["_deadState"],
        ["TestSubject"] = ["_deadState"],
        ["TerrorEel"] = ["_terrorState"],
        ["ToughEgg"] = ["_afterHatchedState"],
        ["WaterfallGiant"] = ["_aboutToBlowState"],
    };

    private static Dictionary<string, object>? CaptureMonsterScalars(object monster)
    {
        if (!MonsterScalarSpecs.TryGetValue(monster.GetType().Name, out var names))
            return null;

        var dict = new Dictionary<string, object>();
        foreach (var name in names)
        {
            var field = monster.GetType().GetField(name, ScalarFieldFlags);
            if (field == null)
                continue;

            try
            {
                var value = field.GetValue(monster);
                if (value != null)
                    dict[name] = value;
            }
            catch
            {
                // 单个字段读不到就跳过
            }
        }

        GD.Print($"[Subaru] monster state captured: {monster.GetType().Name}, fields={dict.Count}");
        return dict;
    }

    private static void CaptureMonsterStateIds(object monster, Dictionary<string, string> into)
    {
        if (!MonsterStateSpecs.TryGetValue(monster.GetType().Name, out var names))
            return;

        foreach (var name in names)
        {
            var field = monster.GetType().GetField(name, ScalarFieldFlags);
            if (field == null)
                continue;

            try
            {
                if (field.GetValue(monster) is MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MonsterState state
                    && !string.IsNullOrEmpty(state.Id))
                {
                    into[name] = state.Id;
                }
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>
    /// 白名单：能力身上那些「把计数藏在私有 int 字段里」的。
    ///
    /// <para>动态变量那份由 <see cref="CapturePowerVars"/> 负责；这里只收**确认过**的直字段。
    /// <c>EncorePower</c> / <c>ChainsOfBindingPower</c> 的计数在它们内部的 <c>Data</c> 对象里
    /// （`GetInternalData&lt;Data&gt;()`），直接字段拿不到，暂时不收 —— 以后它们出问题要另写读法。</para>
    /// </summary>
    private static readonly (Type Type, string Field)[] PowerCounterFields =
    [
        (typeof(MegaCrit.Sts2.Core.Models.Powers.SlothPower), "_cardsPlayedThisTurn"),
    ];

    /// <summary>记下白名单能力的私有计数（见 <see cref="PowerState.Fields"/>）。</summary>
    private static void CapturePowerFields(PowerModel power, Dictionary<string, int> into)
    {
        try
        {
            var type = power.GetType();
            foreach (var (declared, fieldName) in PowerCounterFields)
            {
                if (!declared.IsAssignableFrom(type))
                    continue;

                var field = PowerCounterField(declared, fieldName);
                if (field?.GetValue(power) is int value)
                    into[fieldName] = value;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] capture power fields failed ({power.GetType().Name}): {e.Message}");
        }
    }

    /// <summary>把白名单里的私有计数盖回能力上（只写发生变化的那几个）。</summary>
    private static void RestorePowerFields(PowerModel power, PowerState saved)
    {
        if (saved.Fields.Count == 0)
            return;

        try
        {
            var type = power.GetType();
            foreach (var (declared, fieldName) in PowerCounterFields)
            {
                if (!declared.IsAssignableFrom(type))
                    continue;

                if (!saved.Fields.TryGetValue(fieldName, out var want))
                    continue;

                var field = PowerCounterField(declared, fieldName);
                if (field == null)
                    continue;

                var before = field.GetValue(power);
                if (before is int b && b == want)
                    continue;

                field.SetValue(power, want);
                GD.Print($"[Subaru] power field restored: {type.Name}.{fieldName} {before} -> {want}");
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] restore power fields failed ({power.GetType().Name}): {e.Message}");
        }
    }

    private static System.Reflection.FieldInfo? PowerCounterField(Type declared, string name) =>
        declared.GetField(name, System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Public);

    /// <summary>
    /// 记下能力身上所有 DynamicVars 的 BaseValue（见 <see cref="PowerState.Vars"/> 的注释）。
    /// 读不到的键直接跳过：不同能力的变量集合差别很大，不能因为一个怪变量失败就整体不存。
    /// </summary>
    private static void CapturePowerVars(PowerModel power, Dictionary<string, decimal> into)
    {
        try
        {
            foreach (var key in power.DynamicVars.Keys)
            {
                try
                {
                    into[key] = power.DynamicVars[key].BaseValue;
                }
                catch
                {
                    // 单个变量读不到就跳过
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] capture power vars failed ({power.GetType().Name}): {e.Message}");
        }
    }

    /// <summary>
    /// 把存档点记下的 DynamicVars 盖回能力上。
    ///
    /// 这是「计数器没被重置」那类问题的通用解法：原版有若干能力把计数放在动态变量里
    /// （旧日雕像给自己挂的[缓慢]就是 <c>SlowAmount</c>），只还原 Amount 不会让它归零。
    /// 只写 BaseValue：Value 会叠上钩子修正，写回去等于重复计算。
    /// </summary>
    private static void RestorePowerVars(PowerModel power, PowerState saved)
    {
        if (saved.Vars.Count == 0)
            return;

        try
        {
            var changed = 0;
            foreach (var pair in saved.Vars)
            {
                try
                {
                    var variable = power.DynamicVars[pair.Key];
                    if (variable.BaseValue == pair.Value)
                        continue;

                    variable.BaseValue = pair.Value;
                    changed++;
                }
                catch
                {
                    // 这个能力没有这个变量（例如重建后变量集合变了）就跳过
                }
            }

            if (changed > 0)
                GD.Print($"[Subaru] power vars restored: {power.GetType().Name}, {changed} var(s)");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] restore power vars failed ({power.GetType().Name}): {e.Message}");
        }
    }

    private static Dictionary<string, object> CaptureScalars(object model)
    {
        var dict = new Dictionary<string, object>();
        try
        {
            foreach (var f in model.GetType().GetFields(ScalarFieldFlags))
            {
                if (f.IsInitOnly)
                    continue;

                var t = f.FieldType;
                if (t != typeof(int) && t != typeof(float) && t != typeof(bool) && t != typeof(decimal))
                    continue;

                dict[f.Name] = f.GetValue(model);
            }

            GD.Print($"[Subaru] scalars captured: {model.GetType().Name}, fields={dict.Count}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] capture scalars failed: " + e.Message);
        }

        return dict;
    }

    private static void RestoreScalars(object model, Dictionary<string, object> dict)
    {
        try
        {
            foreach (var kv in dict)
            {
                var f = model.GetType().GetField(kv.Key, ScalarFieldFlags);
                if (f == null || f.IsInitOnly)
                    continue;

                try
                {
                    f.SetValue(model, kv.Value);
                }
                catch
                {
                    // 单个字段写不进去就跳过
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] restore scalars failed: " + e.Message);
        }
    }

    private static void RestoreIntent(CreatureState cs)
    {
        if (!cs.IsMonster || cs.NextMove == null)
            return;

        var monster = cs.Creature.Monster;
        if (monster == null)
            return;

        // 怪物被移出战斗再放回来时，MoveStateMachine 是 null（SetMoveImmediate 里 get_MoveStateMachine
        // 之后直接 ForceCurrentState，于是抛 NRE；表现就是「不叠力量、不换意图、每回合只会打一发」）。
        // 这里按原版工厂方法重建状态机，再把存档点那一招按 StateId 找回新机器里的同一个状态。
        try
        {
            if (monster.MoveStateMachine == null)
            {
                var generator = monster.GetType().GetMethod(
                    "GenerateMoveStateMachine",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                if (generator != null)
                {
                    monster.MoveStateMachine = generator.Invoke(monster, null)
                        as MonsterMoveStateMachine;
                    GD.Print($"[Subaru] move machine rebuilt: {cs.Creature.Name}");
                }
            }

            var machine = monster.MoveStateMachine;
            if (machine?.States != null && cs.NextMove != null
                && machine.States.TryGetValue(cs.NextMove.StateId, out var resolvedState)
                && resolvedState is MoveState resolvedMove
                && !ReferenceEquals(resolvedMove, cs.NextMove))
            {
                cs.NextMove = resolvedMove;
                GD.Print($"[Subaru] move state re-resolved: {cs.Creature.Name} -> {resolvedMove.StateId}");
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Subaru] move machine rebuild failed: {ex.Message}");
        }

        // 女王：HasAmalgamDied 不在能力里，是模型上的字段，必须单独回滚。
        // 两个 MoveState 字段（狂暴态 / 燃烧态）按 StateId 在新机器里找回同一个状态。
        try
        {
            if (cs.Creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.Queen queen)
            {
                queen.HasAmalgamDied = cs.QueenAmalgamDied;

                var queenMachine = queen.MoveStateMachine;
                if (queenMachine?.States != null)
                {
                    if (cs.QueenEnragedStateId != null
                        && queenMachine.States.TryGetValue(cs.QueenEnragedStateId, out var enraged)
                        && enraged is MoveState enragedMove)
                    {
                        queen.EnragedState = enragedMove;
                    }

                    if (cs.QueenBurnBrightStateId != null
                        && queenMachine.States.TryGetValue(cs.QueenBurnBrightStateId, out var burn)
                        && burn is MoveState burnMove)
                    {
                        queen.BurnBrightForMeState = burnMove;
                    }
                }

                GD.Print($"[Subaru] queen state restored: amalgamDied={cs.QueenAmalgamDied}, "
                         + $"enraged={cs.QueenEnragedStateId ?? "<null>"}, burn={cs.QueenBurnBrightStateId ?? "<null>"}");
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning("[Subaru] queen state restore failed: " + ex.Message);
        }

        // 其余怪「指向招式状态」的字段：按 StateId 找回同一个状态再赋值
        // （_terrorState / _deadState / _aboutToBlowState / _afterHatchedState / _beastCryState）
        if (cs.MonsterStateIds.Count > 0)
        {
            try
            {
                var stateMachine = monster.MoveStateMachine;
                if (stateMachine?.States != null)
                {
                    var restored = 0;
                    foreach (var pair in cs.MonsterStateIds)
                    {
                        var field = monster.GetType().GetField(pair.Key, ScalarFieldFlags);
                        if (field == null)
                            continue;

                        if (!stateMachine.States.TryGetValue(pair.Value, out var state))
                            continue;

                        try
                        {
                            field.SetValue(monster, state);
                            restored++;
                        }
                        catch
                        {
                            // 字段类型和状态类型对不上就跳过
                        }
                    }

                    if (restored > 0)
                        GD.Print($"[Subaru] monster move states restored: {cs.Creature.Name}, count={restored}");
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning("[Subaru] monster move state restore failed: " + ex.Message);
            }
        }

        try
        {
            monster.SetMoveImmediate(cs.NextMove, true);
            GD.Print($"[Subaru] intent restored: {cs.Creature.Name} -> {cs.NextMove.StateId}");
        }
        catch (Exception e)
        {
            // 刚复活的怪 NextMove 可能还是 null，SetMoveImmediate 里的空引用会抛：退化为直接压状态机
            GD.PushWarning($"[Subaru] SetMoveImmediate failed ({cs.Creature.Name}): {e.Message}");

            try
            {
                monster.MoveStateMachine?.ForceCurrentState(cs.NextMove);

                // 关键：SetMoveImmediate 开头就 `this.NextMove.CanTransitionAway`，没判空。
                // 刚放回战斗的怪（帝皇蟹的火箭/钳子）NextMove 是 null，所以必须直接写回去，
                // 否则下一回合 RollMove 也会乱，表现成「不叠力量、不换意图、每回合只打 3 点」。
                if (monster.NextMove == null)
                {
                    try
                    {
                        monster.NextMove = cs.NextMove;
                        GD.Print($"[Subaru] NextMove forced: {cs.Creature.Name} -> {cs.NextMove.StateId}");
                    }
                    catch (Exception e3)
                    {
                        GD.PushWarning($"[Subaru] NextMove assign failed: {e3.Message}");
                    }
                }

                RefreshIntentVisual(cs.Creature);
            }
            catch (Exception e2)
            {
                GD.PushError($"[Subaru] intent fallback failed: {e2}");
            }
        }

        // 招式状态机的「计数器」必须放在最后写：ForceCurrentState/SetCurrentState 会先跑
        // OnExitState 把 _performedAtLeastOnce 清掉，早写就白写了。
        RestoreMoveCounters(monster, cs);
    }

    /// <summary>
    /// 记下招式状态机的两个计数（都不是怪物身上的字段，所以 <see cref="MonsterScalarSpecs"/> 抓不到）：
    ///
    /// <list type="bullet">
    /// <item><c>MonsterMoveStateMachine._performedFirstMove</c>：为真时状态机才允许切换下一招。
    /// 不还原的话，死归到「第一招还没打」的位置时它已经能换招，于是跳过初始招式。</item>
    /// <item><c>MoveState._performedAtLeastOnce</c>：配合 <c>MustPerformOnceBeforeTransitioning</c>
    /// 决定这一招要不要重复。不还原的话，本该重复的招式会直接往下走。</item>
    /// </list>
    ///
    /// 典型表现：蜂群术士的「喷射信息素」每打一次自己 +1/+2 力量，招式链提前推进就会让它
    /// 多打或少打一次，玩家看到的就是「力量没有重置」（2026-11-18 反馈）。
    /// </summary>
    private static void CaptureMoveCounters(MonsterModel monster, CreatureState cs)
    {
        try
        {
            var machine = monster.MoveStateMachine;
            if (machine == null)
                return;

            var firstField = machine.GetType().GetField("_performedFirstMove", ScalarFieldFlags);
            if (firstField != null && firstField.GetValue(machine) is bool first)
                cs.MovePerformedFirst = first;

            var onceField = MoveStateOnceField;
            if (onceField == null || machine.States == null)
                return;

            foreach (var state in machine.States.Values)
            {
                if (state is not MoveState move)
                    continue;

                try
                {
                    if (onceField.GetValue(move) is bool once)
                        cs.MovePerformedOnce[move.StateId] = once;
                }
                catch
                {
                    // 单个状态读不到就跳过
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] capture move counters failed: {e.Message}");
        }
    }

    private static void RestoreMoveCounters(MonsterModel monster, CreatureState cs)
    {
        try
        {
            var machine = monster.MoveStateMachine;
            if (machine == null)
                return;

            var restored = 0;

            var firstField = machine.GetType().GetField("_performedFirstMove", ScalarFieldFlags);
            if (firstField != null)
            {
                firstField.SetValue(machine, cs.MovePerformedFirst);
                restored++;
            }

            var onceField = MoveStateOnceField;
            if (onceField != null && machine.States != null)
            {
                foreach (var pair in cs.MovePerformedOnce)
                {
                    if (!machine.States.TryGetValue(pair.Key, out var state) || state is not MoveState move)
                        continue;

                    try
                    {
                        onceField.SetValue(move, pair.Value);
                        restored++;
                    }
                    catch
                    {
                        // 跳过
                    }
                }
            }

            if (restored > 0)
                GD.Print($"[Subaru] move counters restored: {cs.Creature.Name}, "
                         + $"firstMove={cs.MovePerformedFirst}, states={cs.MovePerformedOnce.Count}");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Subaru] restore move counters failed: {e.Message}");
        }
    }

    /// <summary><c>MoveState._performedAtLeastOnce</c> 的反射句柄（类型固定，缓存一次即可）。</summary>
    private static readonly System.Reflection.FieldInfo? MoveStateOnceField =
        typeof(MoveState).GetField("_performedAtLeastOnce", ScalarFieldFlags);

    /// <summary>只刷新意图图标，不动状态机。</summary>
    private static void RefreshIntentVisual(Creature creature)
    {
        try
        {
            var node = creature.GetCreatureNode();
            if (node != null)
                _ = node.RefreshIntents();
        }
        catch (Exception e)
        {
            GD.PushError($"[Subaru] refresh intents failed: {e}");
        }
    }
}
