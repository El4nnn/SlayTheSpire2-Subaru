using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「远古之牙」（<see cref="ArchaicTooth"/>）对菜月昴补一组映射：**基尔提之鞭 → 死者之书**。
///
/// <para>原版的 <c>TranscendenceUpgrades</c> 是硬编码的六组（打击→Break、中和→Suppress…），
/// 昴的初始牌（打击/防御/纱幕/基尔提之鞭）一个都不在表里，于是
/// <c>GetTranscendenceStarterCard</c> 返回 null → 遗物在 Orobas 事件里等于白拿。</para>
///
/// <para>这里只补两处返回值，**不改那张静态映射表**：
/// 因为 <c>ArchaicTooth.TranscendenceCards</c>（供「尘封之书」排除）也读同一张表，
/// 改表会让「尘封之书」把死者之书一起排除掉 —— 昴就再也刷不到先古卡了。</para>
/// </summary>
[HarmonyPatch(typeof(ArchaicTooth), "GetTranscendenceStarterCard")]
public static class ArchaicToothStarterCardPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, ref CardModel? __result)
    {
        if (__result != null)
            return;

        try
        {
            __result = player?.Deck?.Cards?.FirstOrDefault(card => card is Guiltywhip);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] archaic tooth starter probe failed: " + e.Message);
        }
    }
}

/// <summary>
/// 配套的另一半：把「基尔提之鞭」的转化结果换成 <see cref="BookOfTheDead"/>。
/// 保留原版语义 —— 原牌已升级则给升级版，附魔一并搬过去。
/// （不改的话原版兜底会把它变成一张「怀疑」诅咒。）
/// </summary>
[HarmonyPatch(typeof(ArchaicTooth), "GetTranscendenceTransformedCard")]
public static class ArchaicToothTransformPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CardModel starterCard, ref CardModel __result)
    {
        if (starterCard is not Guiltywhip)
            return true;

        try
        {
            var owner = starterCard.Owner;
            var card = owner.RunState.CreateCard(ModelDb.Card<BookOfTheDead>(), owner);

            if (starterCard.IsUpgraded)
                CardCmd.Upgrade(card);

            var enchantment = starterCard.Enchantment;
            if (enchantment != null)
            {
                var clone = (EnchantmentModel)enchantment.MutableClone();
                CardCmd.Enchant(clone, card, clone.Amount);
            }

            __result = card;
            GD.Print($"[Subaru] archaic tooth: {starterCard.Id} -> BOOK_OF_THE_DEAD");
            return false;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] archaic tooth transform failed, falling back to vanilla: " + e.Message);
            return true;
        }
    }
}

/// <summary>
/// 「尘封之书」（<see cref="DustyTome"/>）：从角色卡池里随机抽一张先古卡。
///
/// <para>我们有三张契约牌（贝蒂 / 文森特 / 斯芬克斯）也是 <c>CardRarity.Ancient</c>，
/// 于是会被它抽到并直接塞进牌组 —— 但契约是「涅奥页专属」，不该从别处发放：它决定整局的阵营路线，
/// 而且重复拿到同一张契约会就地改变牌池过滤。这里把契约排除后重新抽一次。</para>
/// </summary>
[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.SetupForPlayer))]
public static class DustyTomeContractGuardPatch
{
    [HarmonyPostfix]
    public static void Postfix(DustyTome __instance, Player player)
    {
        try
        {
            if (!IsForbidden(__instance.AncientCard))
                return;

            var pool = player.Character.CardPool
                .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                .Where(card => card.Rarity == CardRarity.Ancient
                               && !ArchaicTooth.TranscendenceCards.Contains(card)
                               && !IsForbidden(card.Id))
                .ToList();

            var replacement = player.PlayerRng.Rewards.NextItem(pool);
            __instance.AncientCard = replacement?.Id;

            GD.Print($"[Subaru] dusty tome: contract filtered, picked {(replacement == null ? "nothing" : replacement.Id.ToString())}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] dusty tome contract guard failed: " + e.Message);
        }
    }

    private static bool IsForbidden(ModelId? id)
    {
        if (id == null)
            return false;

        try
        {
            return IsForbidden(ModelDb.GetById<CardModel>(id));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForbidden(CardModel? card) =>
        card is BeatriceContract or VincentContract or SphinxContract;
}
