using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Relics;

/// <summary>
/// 对话镜：普通遗物。每场战斗打出的第一张[技能]牌重放 1 次（打出两次）。
///
/// 计数和判定都放在这里，由 <see cref="Subaru.SubaruCode.Patches.MirrorPlayCountPatch"/>
/// 在出牌时问一次「这张牌能不能白送一次」。不自己再调 PlayCard。
/// </summary>
public class TalkingMirror : SubaruRelic, IRewindCounterRelic
{
    public override RelicRarity Rarity => RelicRarity.Common;

    /// <summary>本场战斗已经把这次重放用掉了。</summary>
    private bool _usedThisCombat;

    /// <summary>死归回档时这个「本场已用过」的标记要跟着回去。</summary>
    public int RewindCounter
    {
        get => _usedThisCombat ? 1 : 0;
        set => _usedThisCombat = value != 0;
    }

    public override Task BeforeCombatStart()
    {
        _usedThisCombat = false;
        return Task.CompletedTask;
    }

    /// <summary>这张技能牌要不要多打一次。返回 true 表示已经记账。</summary>
    public bool TryConsumeReplay(CardModel card)
    {
        if (_usedThisCombat || card == null || card.Type != CardType.Skill)
            return false;

        _usedThisCombat = true;
        Godot.GD.Print($"[Subaru] talking mirror: replay granted on {card.Id}");
        return true;
    }
}
