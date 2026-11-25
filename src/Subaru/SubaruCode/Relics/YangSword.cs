using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 阳剑：罕见遗物。每场战斗的第 3 个玩家回合开始时，对所有敌人造成 15 点伤害。
/// 计数在战斗开始时归零，跨战斗不累计。
/// </summary>
public class YangSword : SubaruRelic, IRewindCounterRelic
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    private const int TriggerTurn = 3;
    private const decimal Damage = 15m;

    private int _turn;

    /// <summary>死归回档时这个计数要跟着回去（见 RelicCounterSnapshot）。</summary>
    public int RewindCounter
    {
        get => _turn;
        set
        {
            _turn = value;
            SyncStatus();
        }
    }

    /// <summary>
    /// 触发过就把图标变灰。原版机制：RelicStatus.Disabled 会让
    /// NRelicInventoryHolder.RefreshStatus 把 modulate 设成 #808080，
    /// RelicModel.UpdateTexture 再把 shader 的 _isUsed 打开。纯外观，不影响任何逻辑。
    /// </summary>
    private void SyncStatus()
    {
        try
        {
            Status = _turn >= TriggerTurn
                ? MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Disabled
                : MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Normal;
        }
        catch
        {
            // 规范模型（图鉴）不可变，忽略
        }
    }

    /// <summary>
    /// 右下角计数器，规则照历石（StoneCalendar）：
    ///   战斗中显示「当前是第几回合」，到打出爆发那一回合就不再显示。
    ///   -1 = 不显示，ShowCounter 靠它判断。
    /// </summary>
    public override int DisplayAmount
    {
        get
        {
            try
            {
                var manager = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
                if (manager == null || !manager.IsInProgress)
                    return -1;

                // 图鉴/卡池里的规范模型不显示数字
                if (IsCanonical)
                    return -1;

                if (_turn <= 0 || _turn >= TriggerTurn)
                    return -1;

                return _turn;
            }
            catch
            {
                return -1;
            }
        }
    }

    public override bool ShowCounter => DisplayAmount > -1;

    /// <summary>
    /// 战斗结束把图标恢复原色。
    /// 必须两头都做：Status 回 Normal（触发 StatusChanged → RefreshStatus 把 modulate 设回白色），
    /// 再调一次 NRelic.Reload()（→ UpdateTexture，把 shader 的 _isUsed 清 0）。
    /// 只改 Status 不够：战斗外 RefreshStatus 会提前 return，压根不会调 UpdateTexture。
    /// </summary>
    public override Task AfterCombatEnd(MegaCrit.Sts2.Core.Rooms.CombatRoom room)
    {
        _turn = 0;
        SyncStatus();
        InvokeDisplayAmountChanged();
        RefreshRelicIconNodes();
        return Task.CompletedTask;
    }

    private void RefreshRelicIconNodes()
    {
        try
        {
            if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree || tree.Root == null)
                return;

            Walk(tree.Root);
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning("[Subaru] yang sword icon refresh failed: " + e.Message);
        }

        void Walk(Godot.Node node)
        {
            if (node.GetType().Name == "NRelic")
            {
                var model = node.GetType()
                    .GetProperty("Model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    ?.GetValue(node);

                if (ReferenceEquals(model, this))
                {
                    node.GetType()
                        .GetMethod("Reload",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic)
                        ?.Invoke(node, null);
                }
            }

            foreach (var child in node.GetChildren())
                Walk(child);
        }
    }

    public override Task BeforeCombatStart()
    {
        _turn = 0;
        SyncStatus();
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        // 多人：钩子对每个玩家的回合开始都触发；回合计数必须只算自己的，
        // 否则队友的回合会把计数器推快，第 3 回合的爆发会提前/重复打出。
        if (!ReferenceEquals(player, Owner))
            return;

        _turn++;
        SyncStatus();
        InvokeDisplayAmountChanged();

        if (_turn != TriggerTurn)
            return;

        var state = player?.Creature?.CombatState;
        if (state == null || player.Creature == null)
            return;

        foreach (var enemy in state.HittableEnemies)
        {
            // Unpowered：遗物伤害是固定值，不吃玩家的力量（只有 Move 会被 StrengthPower 加成）
            await CreatureCmd.Damage(
                choiceContext, enemy, Damage, ValueProp.Move | ValueProp.Unpowered, player.Creature);
        }

        Godot.GD.Print("[Subaru] yang sword: turn 3 burst fired");
    }
}
