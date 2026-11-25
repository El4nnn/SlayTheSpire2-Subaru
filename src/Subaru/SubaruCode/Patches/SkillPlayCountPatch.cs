using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 统计「玩家本场战斗打出的技能牌数」（阿尔·纱幕的斩杀线用）。
///
/// <para><b>为什么不再写在 <c>SubaruCard.OnPlay</c> 里</b>：那里只覆盖本 mod 的牌，
/// 原版技能（连<strong>防御</strong>都是技能）和其它 mod 的技能一张都不计，
/// 而卡面写的是「本场已打出技能牌数」——全量口径。改成挂 <c>Hook.AfterCardPlayed</c>，
/// 所有出牌都会过这里。</para>
///
/// <para>判据：<c>CardType.Skill</c> 且出牌者是玩家（怪物塞的牌没有 Owner）。
/// 计数本身在 <see cref="CombatStats.OnSkillPlayed"/> 里做了主线程保护 ——
/// 第三方求解器在线程池里模拟战斗时不能记账。</para>
///
/// <para>参数按<strong>位置</strong>取，不按名字（游戏小版本会改形参名）。</para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
public static class SkillPlayCountPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var card = cardPlay?.Card;
            if (card == null || combatState == null)
                return;

            if (card.Type != CardType.Skill)
                return;

            // 只算玩家的牌：怪物塞进来的牌 Owner 为 null
            if (card.Owner == null)
                return;

            CombatStats.OnSkillPlayed();
        }
        catch (Exception e)
        {
            Godot.GD.PushError("[Subaru] skill play count patch failed: " + e);
        }
    }
}
