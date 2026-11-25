using System.Collections.Generic;
using System.Linq;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Timeline;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using Subaru.SubaruCode.Extensions;
using Godot;

namespace Subaru.SubaruCode.Character;

public class SubaruCardPool : CustomCardPoolModel
{
    public override string Title => Subaru.CharacterId; //This is not a display name.

    /// <summary>
    /// 关键：默认是 false，BaseLib 不会把本池的牌写进「已发现卡牌」，
    /// 于是百科大全里全显示成「未知」。打开它，卡池里的牌一开局就算已见。
    /// </summary>
    public override bool SeenByDefault => true;

    /// <summary>
    /// 需要「通关 / 累计死归」才解锁的牌 → 解锁它的节点 id（任意一个揭示即可）。
    /// 不在表里的牌一律视为已解锁 —— 否则模组牌会在图鉴里全显示成「未知」。
    /// </summary>
    private static readonly Dictionary<Type, string[]> CardGates = new()
    {
        [typeof(Volcanica)] = [SubaruEpochGate.WitchEndEpochId],
        [typeof(GreatSage)] = [SubaruEpochGate.WitchEndEpochId, SubaruEpochGate.LoveEpochId],
        [typeof(Reid)] = [SubaruEpochGate.WitchEndEpochId],
        [typeof(SatellaWitch)] = [SubaruEpochGate.LoveEpochId],
        [typeof(Lost)] = [SubaruEpochGate.LoveEpochId],
        [typeof(SpiritEater)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(BlueLightning)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(SoulMarriage)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Orbart)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Gurobi)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Madeleine)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Moguro)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Gotz)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Ballroy)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Kishia)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Kafma)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Kulgan)] = [SubaruEpochGate.SwordWolfEpochId],
        [typeof(Luan)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(Yugard)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(Lamia)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(MultiForm)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(CorpseBallroy)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(CorpseSphinx)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(Valgren)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(CalamityForm)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(Librey)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(UndyingKingBaptism)] = [SubaruEpochGate.CalamityEpochId],
        [typeof(PandoraWitch)] = [SubaruEpochGate.CollapseEpochId],
        [typeof(Hector)] = [SubaruEpochGate.CollapseEpochId],
        [typeof(RoswaalFriend)] = [SubaruEpochGate.CollapseEpochId],
        [typeof(Otogi)] = [SubaruEpochGate.GluttonyEpochId],
        [typeof(Kurohebi)] = [SubaruEpochGate.GluttonyEpochId],
        [typeof(Hakugei)] = [SubaruEpochGate.GluttonyEpochId],
    };

    /// <summary>
    /// 供「战斗中动态生成牌」用：这张牌所属的 epoch 是否已经解锁（没在解锁表里的牌一律算已解锁）。
    /// 和 <see cref=FilterThroughEpochs/> 用同一张 <see cref=CardGates/> 表、同一个 UnlockState 口径，
    /// 避免随机生成把没解锁的阵营牌提前发出来。
    /// </summary>
    internal static bool IsUnlockedForGeneration(CardModel card)
    {
        if (card == null)
            return true;

        // 多人：契约页三个选项恒定发放，卡池也跟着放宽（不然会出现「选了契约却没牌」）；
        // 莎缇拉在多人里不出现（它的伤害按死归次数结算，多人没有死归）。
        // 小国王/魔兽使·梅莉/狮子王所见之梦/陶德·方古都是多人专属（单人没有队友），单人池子里不出现。
        if (!MultiplayerSupport.IsMultiplayer && SubaruCard.IsMultiplayerOnlyCard(card))
            return false;

        if (MultiplayerSupport.IsMultiplayer)
        {
            if (card is SatellaWitch)
                return false;

            // 多人只放宽「解锁门槛」（涅奥恒定给三个契约），**阵营路线照样硬切**。
            // 2026-11-19 玩家报「多人能拿到阵营外的牌」：之前这里直接 return true，
            // 把 IsAllowedOnPath 也一起跳过了 —— 选了贝蒂契约还能刷到帝国军 / 尸兵的牌。
            return AllowedOnCurrentPath(card);
        }

        if (!CardGates.TryGetValue(card.GetType(), out var gates))
            return true;

        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            if (progress == null)
                return true;

            var state = new MegaCrit.Sts2.Core.Unlocks.UnlockState(progress);
            return SubaruEpochGate.AnyRevealed(state, gates);
        }
        catch
        {
            // 读不到解锁状态时放行，宁可多给也别把生成卡死
            return true;
        }
    }

    protected override IEnumerable<CardModel> FilterThroughEpochs(UnlockState state, IEnumerable<CardModel> cards)
    {
        // 派系硬切放在牌池里做，而不是拦 CardModel 的某个方法：
        // 0.107（默认分支）根本没有 CardModel.IsAllowedInRun，牌池过滤是两边都有的公共入口。
        var runState = CurrentRunState();

        return cards.Where(card =>
        {
            // 小国王/梅莉/狮子王所见之梦/陶德·方古：多人专属，单人牌池里不出现。
            if (!MultiplayerSupport.IsMultiplayer && SubaruCard.IsMultiplayerOnlyCard(card))
                return false;

            // 多人：只放宽「解锁门槛」（涅奥三个契约恒定发放），莎缇拉不出现；
            // 但**阵营路线仍然硬切** —— 否则选了契约还能刷到别家的牌（2026-11-19 玩家报的 bug）。
            if (MultiplayerSupport.IsMultiplayer)
            {
                if (card is SatellaWitch)
                    return false;

                return AllowedOnCurrentPath(card);
            }

            if (CardGates.TryGetValue(card.GetType(), out var gates)
                && !SubaruEpochGate.AnyRevealed(state, gates))
            {
                return false;
            }

            // 签约后对方阵营的牌不再进入奖励 / 商店 / 随机生成
            if (runState != null
                && card is SubaruCard subaru
                && !subaru.IsAllowedOnPath(runState))
            {
                return false;
            }

            return true;
        });
    }

    /// <summary>
    /// 卡池出口用：只看「契约路线」，不看解锁/纪元门槛。
    ///
    /// <para>多人里涅奥恒定给三个契约，如果还按纪元门槛过滤，会出现「选了契约却没牌」，
    /// 所以门槛放宽；但<see cref="SubaruCard.IsAllowedOnPath(Subaru.SubaruCode.Runs.SubaruPath)"/>
    /// 这条阵营硬切必须保留。</para>
    ///
    /// <para><b>这里取「并集」</b>：一人一个卡池，而卡池出口拿不到 player —— 一队两个昴各选不同契约时，
    /// 用任何单条路线都会把另一个人的牌全剔光。所以队里出现过的契约只要有一条允许就放行，
    /// 真正按人切的那一刀在 <see cref="AllowedForPlayer"/>
    /// （奖励 / 商店 / 战斗内生成都带 player，见 <c>RewardExclusionPatch</c>）。</para>
    /// </summary>
    private static bool AllowedOnCurrentPath(CardModel card)
    {
        if (card is not SubaruCard subaru)
            return true;

        var runState = CurrentRunState();
        if (runState == null)
            return true;

        try
        {
            var paths = Runs.SubaruRunPath.All(runState);
            if (paths.Count == 0)
                return true;                 // 谁都没选契约 → 不切

            foreach (var path in paths)
            {
                if (subaru.IsAllowedOnPath(path))
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 按**这个玩家自己**的契约路线判定。多人一队两个昴、各选不同契约时，
    /// 只有这条口径能给出「你只看到自己阵营的牌」。
    ///
    /// <para>player 为空、或这个人牌组里没有契约（不是昴 / 还没在涅奥处选）时，
    /// 退回队级并集 —— 宁可多给一张，也不要在选牌界面把列表清空。</para>
    /// </summary>
    internal static bool AllowedForPlayer(MegaCrit.Sts2.Core.Entities.Players.Player? player, CardModel card)
    {
        if (card is not SubaruCard subaru)
            return true;

        try
        {
            var path = Runs.SubaruRunPath.ForPlayer(player);
            if (path == Runs.SubaruPath.None)
                return AllowedOnCurrentPath(card);

            return subaru.IsAllowedOnPath(path);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>当前这局的 RunState（不在局内时返回 null）。</summary>
    private static MegaCrit.Sts2.Core.Runs.IRunState? CurrentRunState()
    {
        try
        {
            return MegaCrit.Sts2.Core.Runs.RunManager.Instance?.State;
        }
        catch
        {
            return null;
        }
    }
    
    public override string BigEnergyIconPath => "charui/big_energy.png".ImagePath();
    public override string TextEnergyIconPath => "charui/text_energy.png".ImagePath();


    /* These HSV values will determine the color of your card back.
    They are applied as a shader onto an already colored image,
    so it may take some experimentation to find a color you like.
    Generally they should be values between 0 and 1. */
    public override float H => 0.78f; //Hue; 紫（#A966FF 附近）
    public override float S => 1f; //Saturation
    public override float V => 1f; //Brightness
    
    //Alternatively, leave these values at 1 and provide a custom frame image.
    /*public override Texture2D CustomFrame(CustomCardModel card)
    {
        //This will attempt to load Subaru/images/cards/frame.png
        return PreloadManager.Cache.GetTexture2D("cards/frame.png".ImagePath());
    }*/

    //Color of small card icons
    public override Color DeckEntryCardColor => Subaru.Color;
    
    public override bool IsColorless => false;
}
