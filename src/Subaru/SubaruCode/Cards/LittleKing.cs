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

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 小国王：<b>多人模式专属</b>，1 费，稀有，能力。
/// <b>一次性</b>：打出时若至少一名队友存活，获得 1 层[缓冲]（升级 2 层）——和「龙血」完全同一个写法
/// （<see cref="DragonBlood"/>：能力牌 + 打出即施加 <see cref="BufferPower"/>，不挂自己的常驻能力）。
///
/// <para>没有活着的队友时不发缓冲，卡照样消耗（多人里队友全灭的残局要注意）。</para>
///
/// <para>为什么不进单人牌池：单人没有队友，这张牌永远是个空效果。
/// 卡池过滤见 <c>SubaruCardPool</c>（<c>!IsMultiplayer &amp;&amp; card is LittleKing</c> 直接剔掉）。</para>
///
/// <para>卡面数字用 <c>PowerVar&lt;BufferPower&gt;</c>（和 E·M·M 同一个写法），
/// 文案里写 <c>{BufferPower:diff()}</c>，升级自动 1 → 2。</para>
/// </summary>
public sealed class LittleKing() : SubaruCard(1, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    /// <summary>强欲司教的立绘 cut-in（2026-11-23 接入）。</summary>
    public override string? CutInId => "regulus";

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<BufferPower>(1m)];

    protected override void OnUpgrade() => DynamicVars["BufferPower"].UpgradeValueBy(1m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!HasLivingTeammate())
        {
            GD.Print("[Subaru] mp: little king no living teammate, nothing happens");
            return;
        }

        var amount = DynamicVars["BufferPower"].BaseValue;
        await PowerCmd.Apply<BufferPower>(choiceContext, Owner.Creature, amount, Owner.Creature, this, false);
        GD.Print($"[Subaru] mp: little king grants {amount} buffer (teammate alive)");
    }

    /// <summary>除自己以外，是否还有活着的玩家角色（召唤物/宠物不算）。</summary>
    private bool HasLivingTeammate()
    {
        var players = Owner?.Creature?.CombatState?.Players;
        if (players == null)
            return false;

        return players.Any(p => p != null
                                && p.Creature != null
                                && !ReferenceEquals(p.Creature, Owner.Creature)
                                && p.Creature.CurrentHp > 0);
    }
}
