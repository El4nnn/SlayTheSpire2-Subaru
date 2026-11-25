using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 附魔之后把「伤害数字」立刻补进卡面显示值。
///
/// <para><b>背景</b>：原版 <c>DamageVar.UpdateCardPreview</c> 里其实是算附魔的
/// （<c>base += EnchantDamageAdditive; base *= EnchantDamageMultiplicative;
/// EnchantedValue = base</c>），但玩家实测「剑圣·莱茵哈鲁特 / 过路魔·塞蕾丝缇雅」
/// 被附上[锋利]后卡面数字不变。这里不赌引擎什么时候重算：
/// 在 <c>CardCmd.Enchant</c> 之后，直接把同样的算式写进 <c>EnchantedValue</c>。</para>
///
/// <para><b>只动显示</b>：不改 <c>BaseValue</c>，所以实际伤害仍然由
/// <c>Hook.ModifyDamage</c> 里的附魔分支加一次，不会重复结算。</para>
///
/// <para><b>过路魔那种没有 Damage 变量的牌</b>：本来就没有伤害数字可显示，
/// 卡面不变属于预期（它的总伤害 = N × 每份，附魔在结算时照加）。</para>
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Enchant),
    new[] { typeof(EnchantmentModel), typeof(CardModel), typeof(decimal) })]
public static class EnchantPreviewPatch
{
    [HarmonyPostfix]
    public static void Postfix(EnchantmentModel enchantment, CardModel card)
    {
        try
        {
            if (enchantment == null || card is not SubaruCard)
                return;

            var vars = card.DynamicVars;
            if (vars == null)
                return;

            foreach (var variable in vars.Values.OfType<DamageVar>())
            {
                var props = variable.Props;
                var value = variable.BaseValue;

                value += enchantment.EnchantDamageAdditive(value, props);
                value *= enchantment.EnchantDamageMultiplicative(value, props);

                if (value < 0m)
                    value = 0m;

                variable.EnchantedValue = value;
            }
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning("[Subaru] enchant preview patch failed: " + e.Message);
        }
    }
}
