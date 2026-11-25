using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 狮子王所见之梦：<b>多人模式专属</b>，1 费，罕见，技能，[消耗]。
/// 选一名敌人看它的意图：是攻击 → 所有玩家 +14 格挡；不是攻击 → 所有玩家 +3 力量。
/// （升级 14→18，3→5）
///
/// <para><b>「是不是攻击」怎么判（2026-11-19 修）</b>：用游戏自带的
/// <c>MonsterModel.IntendsToAttack</c>，它的 IL 是
/// <c>NextMove.Intents.Any(i =&gt; i.IntentType == Attack || i.IntentType == DeathBlow)</c> ——
/// 读的是 <b>NextMove</b>（下一次行动），也就是头顶意图图标显示的那一个。
/// 单段攻击继承 <c>AttackIntent</c>（IntentType = Attack），多段是 <c>MultiAttackIntent</c>（同样继承），
/// 终结技是 <c>DeathBlowIntent</c>（= 13），三种都算攻击。</para>
///
/// <para><b>别用 <c>Monster.GetIntents()</c></b>：它遍历 <c>MoveStateMachine.States.Values</c>，
/// 把它<b>所有招式</b>的意图并集返回 —— 只要这只怪有任何攻击招式就恒为真。
/// 之前就是这么写的，导致「击晕之后仍然给格挡」，而且平时也几乎总是走格挡分支。</para>
///
/// <para><b>击晕要单独排掉</b>：我们自己的晕眩（<see cref="Powers.StunHelper"/>）是把
/// <c>__SUBARU_STUN</c> 这个空招塞进状态机，意图是 <c>StunIntent</c>，走 NextMove 判定已经是「非攻击」；
/// 游戏原生的晕眩是状态 id 为 <c>"STUNNED"</c> 的招式（<c>Creature.IsStunned</c> 就在比这个字符串），
/// 那种情况下 NextMove 可能还是攻击招，所以再显式判一次 <c>target.IsStunned</c>。</para>
///
/// <para><b>隐藏意图</b>：<c>HiddenIntent.IntentType</c> 直接是 Hidden（7），本身不带真实类型，
/// 所以「意图隐藏」的怪会被判成「不是攻击」→ 给力量。之前注释里写的「能读到真实意图」不成立。</para>
///
/// <para>「所有玩家」= <c>CombatState.Players</c> 里所有活着的玩家（含自己）。
/// 单人时这张牌也能用，但 <c>SubaruCardPool</c> 不把多人专属牌放进单人池，
/// 所以实际只有一个玩家可吃 —— 数值是按多人（2~4 人）调的，单人吃满就是超模。</para>
///
/// <para>格挡用 <c>CreatureCmd.GainBlock</c>、力量用 <c>PowerCmd.Apply&lt;StrengthPower&gt;</c>：
/// 都是引擎命令，多人由主机结算，不需要自己写同步。</para>
/// </summary>
public sealed class LionDream() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(14m, ValueProp.Move),
        new PowerVar<StrengthPower>(3m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(4m);
        DynamicVars["StrengthPower"].UpgradeValueBy(2m);
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target;
        if (target == null)
            return;

        var players = Owner?.Creature?.CombatState?.Players;
        if (players == null)
            return;

        var monster = target.Monster;
        var stunned = target.IsStunned;

        // 只读「下一次行动」的意图（Attack / DeathBlow），击晕一律算非攻击。
        // 不能用 Monster.GetIntents()：那是所有招式意图的并集，见类注释。
        var intendsToAttack = !stunned && monster != null && monster.IntendsToAttack;

        var living = players
            .Where(p => p?.Creature != null && p.Creature.CurrentHp > 0)
            .Select(p => p.Creature)
            .ToList();

        if (living.Count == 0)
            return;

        if (intendsToAttack)
        {
            var block = DynamicVars.Block.BaseValue;
            foreach (var creature in living)
                await CreatureCmd.GainBlock(creature, DynamicVars.Block, cardPlay);

            GD.Print($"[Subaru] mp: lion dream attack intent -> {block} block to {living.Count} players");
            return;
        }

        var strength = DynamicVars["StrengthPower"].BaseValue;
        foreach (var creature in living)
        {
            await PowerCmd.Apply<StrengthPower>(
                choiceContext, creature, strength, Owner.Creature, this, false);
        }

        GD.Print($"[Subaru] mp: lion dream non-attack intent ({target.Name}, "
                 + $"stunned={stunned}, nextMove={monster?.NextMove?.StateId ?? "-"}) "
                 + $"-> {strength} strength to {living.Count} players");
    }
}
