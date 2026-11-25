using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Subaru.SubaruCode.Cards;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 「[腐蚀]牌不能在战斗中被复制」的统一防线。
///
/// 拦的是 <see cref="CardPileCmd.AddGeneratedCardToCombat"/>：
/// 全库所有「战斗中凭空多一张牌」的路径都汇到这里（双持、夜魇的力量、
/// 音乐盒/工具箱这类遗物、各种发现药水、怪物塞卡……共 63 处调用者）。
///
/// 为什么不是 <c>CombatState.CreateCard</c>：扒 IL 证实双持走的是
/// <c>CardModel.CreateClone()</c> + <c>CardPileCmd.AddGeneratedCardToCombat</c>，
/// 夜魇更是把复制延到下一回合由 <c>NightmarePower.BeforeHandDraw</c> 做，
/// 两者都不调用 CombatState.CreateCard —— 早先挂在 CreateCard 上的补丁从未触发过。
///
/// 为什么不挂在 CreateClone 上：它返回 CardModel 且调用方直接使用，
/// 返回 null 会让双持当场 NullReferenceException。
///
/// 我们自己的生成类效果（潘多拉、死者之书）走的是 <c>CardPileCmd.Add</c>，
/// 不经过这个入口，因此不受影响；仍保留 <see cref="Allow"/> 窗口作为保险。
/// 牌组层的复制（多利之镜）发生在战斗外，属于构筑，不受限制。
/// </summary>
public static class CardCreationPolicy
{
    private static int _allowDepth;

    public static bool AllowingCorroded => _allowDepth > 0;

    /// <summary>开一个「允许生成腐蚀牌」的窗口，返回 IDisposable 以便 using 收尾。</summary>
    public static System.IDisposable Allow()
    {
        _allowDepth++;
        return new Scope();
    }

    private sealed class Scope : System.IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;

            _done = true;
            if (_allowDepth > 0)
                _allowDepth--;
        }
    }

    public static bool IsCorroded(CardModel? card)
    {
        try
        {
            return card != null
                   && card.CanonicalKeywords.Contains(SubaruKeywords.Corrosion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 「不能被复制」的牌：
    ///   · [腐蚀]牌 —— 一场战斗只能打出一次，复制一张就是白送一次
    ///   · 契约·贝蒂 / 鲜血帝·文森特 —— 阵营契约牌：IsGenerateOnly，只能由开局的涅奥契约页发放、
    ///     [永恒]，本身不该进牌池，复制出来的第二张会凭空多一份「每回合产牌 / 转化打击」的收益，
    ///     还会绕过「整个流程只有一张」的设计
    ///
    /// 「发现新牌」（潘多拉、死者之书）不算复制，走它们自己的入口，不受这里限制。
    /// </summary>
    public static bool IsProtected(CardModel? card)
    {
        if (IsCorroded(card))
            return true;

        try
        {
            return card is BeatriceContract or VincentContract or SphinxContract;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 这个 <paramref name="source"/> 是不是「从手牌挑一张来复制」的那类效果。
    ///
    /// 只点名这几类来源，而不是全局过滤：我们自己的牌也大量用 FromHand 选牌
    /// （强欲的契约选保留、权威牌选消耗、牌堆牌选放回……），
    /// 全局过滤会把不该被踢掉的牌从这些合法的选择里也踢掉。
    /// </summary>
    public static bool IsCopySelector(AbstractModel? source)
    {
        return source is DualWield or Nightmare;
    }
}

/// <summary>
/// 让「不可被复制」的牌在复制类选择框里直接不出现。
///
/// 依据：<c>CardSelectCmd.FromHand</c> 对候选做的是 <c>Enumerable.Where(filter)</c>，
/// 不过滤的牌根本不会进入选择列表（不是置灰）。双持、夜魇都走这个入口。
///
/// 这是体验层，<see cref="CorrosionCreationGuardPatch"/> 那层兜底继续保留。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd))]
public static class CorrosionSelectFilterPatch
{
    [HarmonyPatch(nameof(CardSelectCmd.FromHand),
        [typeof(PlayerChoiceContext), typeof(Player), typeof(CardSelectorPrefs),
         typeof(System.Func<CardModel, bool>), typeof(AbstractModel)])]
    [HarmonyPrefix]
    private static void Prefix(AbstractModel source, ref System.Func<CardModel, bool> filter)
    {
        if (CardCreationPolicy.AllowingCorroded)
            return;

        if (!CardCreationPolicy.IsCopySelector(source))
            return;

        var original = filter;
        filter = card => (original == null || original(card)) && !CardCreationPolicy.IsProtected(card);
    }
}

[HarmonyPatch(typeof(CardPileCmd))]
public static class CorrosionCreationGuardPatch
{
    [HarmonyPatch(nameof(CardPileCmd.AddGeneratedCardToCombat),
        [typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition)])]
    [HarmonyPrefix]
    private static bool Prefix(CardModel card, ref Task<CardPileAddResult> __result)
    {
        if (CardCreationPolicy.AllowingCorroded || !CardCreationPolicy.IsProtected(card))
            return true;

        GD.Print($"[Subaru] blocked in-combat creation of protected card: {card?.Id}");

        // 调用方（双持、NightmarePower）都丢弃这个返回值，给个空结果即可。
        __result = Task.FromResult(default(CardPileAddResult));
        return false;
    }
}
