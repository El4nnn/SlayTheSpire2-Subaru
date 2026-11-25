using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 战虫将·卡夫马：普通，1 费，攻击（单体）。[帝国军]。
/// 造成 5 点伤害；每当这张牌进入手牌时，对所有敌人施加 1 层[易伤]，并使这张牌的伤害 +3。
/// 升级：5→6，1→2，3→4。
///
/// <para><b>「进入手牌」怎么抓</b>：<c>CardModel.Drawn</c> 事件只在
/// <c>CardPileCmd.DrawInternal</c> 里触发，被效果直接塞进手牌（文森特每 2 回合塞的那张）不会走它。
/// 所以改用 <c>AbstractModel.AfterCardChangedPiles</c>：<c>CombatState.IterateHookListeners</c>
/// 会把「所有牌堆里的每一张牌」也算进监听者，所以卡自己能收到这个钩子。
/// 判据 = 移动的牌就是自己、旧位置不是手牌、现在在手牌。</para>
///
/// <para><b>伤害怎么涨</b>：次数存私有字段 <c>_handEntries</c>，
/// 卡面用 <c>CalculatedDamageVar</c> + 乘数函数现算（基础 + 每次加成 × 次数），
/// 打出时用同一个算式——卡面与实际不会各算一套。
/// 注意乘数函数必须是 static（原版 <c>WithMultiplier</c> 会检查），所以里面按类型取回自己的计数。</para>
///
/// <para>⚠️ 不能用 <c>StringVar</c>（第三方 CombatSolver 会抛「未分类的字符串状态字段」把战斗卡死），
/// 这里用的全是数值变量。</para>
/// </summary>
public sealed class Kafma() : SubaruCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy),
    Subaru.SubaruCode.Rewind.IRewindCardState
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.NineGenerals];

    public override string? CutInId => "kafma";

    /// <summary>本场战斗里这张牌进入手牌的次数（每次 +<see cref="DynamicVars"/> 里的加成）。</summary>
    private int _handEntries;

    /// <summary>
    /// 这个计数属于哪一场战斗。牌实例跨战斗复用，不记住战斗 id 的话
    /// 「上把进过 5 次手牌」会带进下一场，卡面伤害直接从 5+15 起跳。
    /// </summary>
    private MegaCrit.Sts2.Core.Combat.CombatId? _entriesCombatId;

    /// <summary>换了一场战斗就把累计清零（同一次战斗里读多少次都不动）。</summary>
    private void ResetIfNewCombat()
    {
        try
        {
            var id = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.CurrentCombatId;
            if (id == null)
                return;

            if (!System.Nullable.Equals(_entriesCombatId, id))
            {
                _entriesCombatId = id;
                _handEntries = 0;
            }
        }
        catch
        {
            // 读不到就按「同一场战斗」处理，宁可不重置也不要每张牌进手都清零
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(5m),
        new ExtraDamageVar(3m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(EntryCount),
        new PowerVar<VulnerablePower>(1m),
    ];

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(1m);   // 5 → 6
        DynamicVars.ExtraDamage.UpgradeValueBy(1m);       // 3 → 4
        DynamicVars.Vulnerable.UpgradeValueBy(1m);        // 1 → 2
    }

    /// <summary>卡面乘数：这张牌进过几次手牌。必须 static。</summary>
    private static decimal EntryCount(CardModel card, Creature target) =>
        card is Kafma kafma ? kafma._handEntries : 0m;

    /// <summary>死归回档：把「进手牌次数」退回存档点的值（见 <see cref="Subaru.SubaruCode.Rewind.IRewindCardState"/>）。</summary>
    public int CaptureRewindCounter() => _handEntries;

    /// <inheritdoc />
    public void RestoreRewindCounter(int value)
    {
        if (_handEntries != value)
            Godot.GD.Print($"[Subaru] kafma rewind counter: {_handEntries} -> {value}");

        _handEntries = value;
    }

    /// <summary>进入手牌：给所有敌人上[易伤]，并让自身伤害涨一档。</summary>
    public override async Task AfterCardChangedPiles(
        CardModel card, PileType oldPileType, AbstractModel clonedBy)
    {
        if (!ReferenceEquals(card, this))
            return;

        if (oldPileType == PileType.Hand)
            return;

        if (Pile?.Type != PileType.Hand)
            return;

        var state = CombatState;
        var owner = Owner;
        if (state == null || owner?.Creature == null || owner.PlayerCombatState == null)
            return;

        ResetIfNewCombat();
        _handEntries++;

        var amount = DynamicVars.Vulnerable.BaseValue;
        var enemies = state.HittableEnemies.ToList();

        // 这个钩子拿不到 PlayerChoiceContext，而 PowerCmd.Apply 全都要一个。
        // 这里只是挂能力、不需要玩家做选择，用阻塞上下文最稳（和 Spike 里那几处同样的做法）。
        var ctx = new BlockingPlayerChoiceContext();

        foreach (var enemy in enemies)
        {
            try
            {
                await PowerCmd.Apply<VulnerablePower>(
                    ctx, enemy, amount, owner.Creature, this, false);
            }
            catch (System.Exception e)
            {
                Godot.GD.PushWarning("[Subaru] kafma vulnerable failed: " + e.Message);
            }
        }

        Godot.GD.Print(
            $"[Subaru] kafma entered hand (#{_handEntries}): "
            + $"{amount} vulnerable to {enemies.Count} enemy(ies)");
    }

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        System.ArgumentNullException.ThrowIfNull(cardPlay.Target);

        var damage = DynamicVars.CalculationBase.BaseValue
                     + DynamicVars.ExtraDamage.BaseValue * _handEntries;

        Godot.GD.Print($"[Subaru] kafma played: base {DynamicVars.CalculationBase.BaseValue} "
                       + $"+ {DynamicVars.ExtraDamage.BaseValue} x{_handEntries} => {damage}");

        await DamageCmd.Attack(damage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }
}
