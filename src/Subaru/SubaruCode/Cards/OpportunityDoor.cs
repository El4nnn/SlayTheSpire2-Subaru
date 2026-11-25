using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 机遇门：罕见，1 费，技能。[阴魔法]。
/// 打出你[抽牌堆]顶部的 1 张牌（升级：2 张）。
///
/// 实现：先抽到手上再自动打出 —— 引擎的抽牌指令只有「抽」，没有「直接打出牌堆顶」，
/// 抽起来再 AutoPlay 观感和效果都对得上（抽到的牌会先亮一下）。
/// </summary>
public sealed class OpportunityDoor() : SubaruCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.YinMagic];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Plays", 1m)];

    protected override void OnUpgrade() => DynamicVars["Plays"].UpgradeValueBy(1m); // 1 → 2

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var state = CombatState;
        if (player?.Creature == null || state == null)
            return;

        var plays = (int)DynamicVars["Plays"].BaseValue;

        for (var i = 0; i < plays; i++)
        {
            // 手动「立即死归」可以在两张牌之间的间隙按下：演出 + 回档要好几秒，
            // 而这段循环还在跑。回档一开始就停掉剩余次数，
            // 否则第二张会被打在「还原后」的牌堆上（效果/腐蚀记账都会不一致）。
            if (Rewind.DeathReturnSpike.IsRewinding)
            {
                GD.Print("[Subaru] opportunity door: rewind started, stop remaining plays");
                return;
            }

            CardModel? drawn;
            try
            {
                drawn = await CardPileCmd.Draw(choiceContext, player);
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] opportunity door draw failed: " + e);
                return;
            }

            if (drawn == null)
            {
                GD.Print("[Subaru] opportunity door: draw pile empty");
                return;
            }

            // 攻击牌需要一个目标：随机挑一个能打的敌人
            var target = drawn.TargetType == TargetType.AnyEnemy
                ? state.HittableEnemies.FirstOrDefault()
                : null;

            if (drawn.TargetType == TargetType.AnyEnemy && target == null)
            {
                GD.Print($"[Subaru] opportunity door: no target for {drawn.Id}, skip");
                continue;
            }

            try
            {
                await CardCmd.AutoPlay(choiceContext, drawn, target, AutoPlayType.Default, false, false);
                GD.Print($"[Subaru] opportunity door: auto played {drawn.Id}");
            }
            catch (System.Exception e)
            {
                GD.PushError("[Subaru] opportunity door autoplay failed: " + e);
            }
        }
    }
}
