using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Subaru.SubaruCode.Character;
using Subaru.SubaruCode.Extensions;
using Subaru.SubaruCode.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using Godot;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// This is the base class for your mod's cards, which is set up to load the card's images from your mod's resources.
/// When creating a card, right click the Cards folder and create a new file with the Custom Card template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
/// </summary>
[Pool(typeof(SubaruCardPool))]
public abstract class SubaruCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    CustomCardModel(cost, type, rarity, target)
{
    /// <summary>
    /// 单人模式里不该出现的「多人专属」牌：小国王 / 魔兽使·梅莉 / 狮子王所见之梦 / 陶德·方古
    /// （单人没有队友，这些牌的效果没人可指）。牌池过滤和 <see cref="MultiplayerConstraint"/> 共用这一份名单。
    /// </summary>
    internal static bool IsMultiplayerOnlyCard(CardModel card)
        => card is LittleKing or Meili or LionDream or ToddFang;

#if GAME_BETA
    /// <summary>
    /// 多人专属标记。游戏自带的「多人模式卡牌」筛选（百科大全的 <c>NCardLibrary.UpdateFilter</c>）
    /// 读的就是这个属性，<c>CardPoolModel.GetUnlockedCards</c> / <c>CardModel.IsAllowedInRun</c> 也按它过滤。
    ///
    /// <para>2026-11-20 玩家报「百科大全里取消勾选『多人模式卡牌』，还是能看到那四张」：
    /// 以前只在**我们自己**的牌池过滤（<see cref="Character.SubaruCardPool"/>）里手写了类型判断，
    /// 卡牌的这个属性一直是默认的 <c>None</c>，所以游戏原生筛选器根本不认识它们是多人牌。</para>
    /// </summary>
    public override CardMultiplayerConstraint MultiplayerConstraint
        => IsMultiplayerOnlyCard(this)
            ? CardMultiplayerConstraint.MultiplayerOnly
            : base.MultiplayerConstraint;
#endif

    /// <summary>
    /// 原创状态的黑话解释：卡面提到哪个状态，悬停时就附带哪条说明，
    /// 需要的话再拉出关联牌的卡面预览（幼女使者 → E·M·T / E·M·M，魔女瘴气 → 瘴气本体）。
    ///
    /// 判定方式是「描述文本里出现这个名字」——中英各匹配一次，
    /// 所以不需要给每张卡手写映射，以后新卡只要在描述里写到它，提示自动出现。
    /// 权能 / 腐蚀 / 阴魔法 走的是关键词表（card_keywords.json），已经有悬停，这里不重复加。
    /// </summary>
    private static readonly (string Key, string[] Match, System.Func<MegaCrit.Sts2.Core.Models.CardModel>[] Cards, bool Upgraded)[] StatusTips =
    [
        ("SUBARU-STATUS_LOLIMANCER", ["幼女使者", "Lolimancer"],
            [() => MegaCrit.Sts2.Core.Models.ModelDb.Card<Emt>(), () => MegaCrit.Sts2.Core.Models.ModelDb.Card<Emm>()], true),
        ("SUBARU-STATUS_MADNESS", ["癫狂", "Madness"], [], false),
        ("SUBARU-STATUS_FRAGRANCE", ["魔女遗香", "Witch's Scent"], [], false),
        ("SUBARU-STATUS_FLOAT", ["浮空", "Float"], [], false),
        ("SUBARU-STATUS_MIASMA", ["魔女瘴气", "Witch Miasma"],
            [() => MegaCrit.Sts2.Core.Models.ModelDb.Card<CurseWitchMiasma>()], false),
        ("SUBARU-STATUS_PRIDE", ["傲慢", "Pride"], [], false),
        ("SUBARU-STATUS_SLOTH_FACTOR", ["怠惰因子", "Sloth Factor"],
            [() => MegaCrit.Sts2.Core.Models.ModelDb.Card<TrueInvisibleProvidence>()], false),
        ("SUBARU-STATUS_GREED_CONTRACT", ["强欲的契约", "Greed Contract"], [], false),
        ("SUBARU-STATUS_BEATRICE", ["贝蒂", "Beatrice"], [], false),
        // 2026-11-16 补：这三个词在卡面里是金色高亮，之前没有侧边栏说明
        ("SUBARU-STATUS_REWIND", ["死归", "Return by Death"], [], false),
        ("SUBARU-STATUS_BASIC_MAGIC", ["基础魔法", "Basic Magic"], [], false),
        // 2026-11-19：官方「晕眩」是状态牌名，让敌人停一回合的词是「击晕」，
        // 所以这里改成匹配「击晕」，卡面文案也一起改了。
        ("SUBARU-STATUS_STUN", ["击晕", "Stun", "スタン"], [], false),
        // 2026-11-19：不可被抵挡的伤害（ValueProp.Unblockable）不是官方卡面词，
        // 自己补一条悬停说明，四张用到它的牌都有。
        ("SUBARU-STATUS_UNBLOCKABLE", ["不可被抵挡", "unblockable", "ブロックできない"], [], false),
    ];

    /// <summary>
    /// 纯关联牌预览：没有状态说明，只是「卡面提到它 → 顺手给张图」。
    /// 例：埃尔·迷尼亚的描述里写着「将 2 张迷尼亚加入手牌」，悬停时直接把迷尼亚拉出来，
    /// 玩家不用先打出去才知道它长什么样。
    /// </summary>
    private static readonly (string[] Match, System.Func<string, MegaCrit.Sts2.Core.Models.CardModel>[] Cards, bool Upgraded)[] LinkedCardTips =
    [
        // 埃尔·迷尼亚升级后加入的也是升级版 → 预览跟着显示「临时迷尼亚+」
        (["迷尼亚", "Minya"], [t => t.Contains("临时迷尼亚") || t.Contains("Minya (Temporary)")
            ? MegaCrit.Sts2.Core.Models.ModelDb.Card<MinyaTemp>()
            : MegaCrit.Sts2.Core.Models.ModelDb.Card<Minya>()], true),
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            var tips = new List<IHoverTip>();

            // 同一张牌不重复挂预览（状态条目和卡名匹配可能命中同一个模型）
            var seenCards = new System.Collections.Generic.HashSet<System.Type>();

            try
            {
                var text = Description?.GetRawText();
                if (string.IsNullOrEmpty(text))
                    return tips;

                foreach (var (key, match, cards, upgradeAware) in StatusTips)
                {
                    var hit = false;
                    foreach (var m in match)
                    {
                        if (text.Contains(m, System.StringComparison.Ordinal))
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (!hit)
                        continue;

                    var loc = new MegaCrit.Sts2.Core.Localization.LocString("static_hover_tips", key);
                    if (loc.Exists())
                        tips.Add(new MegaCrit.Sts2.Core.HoverTips.HoverTip(loc, null));

                    // 关联牌预览：卡面里已经有名字了，再给一张图，玩家不用去查
                    foreach (var factory in cards)
                    {
                        try
                        {
                            var model = factory();
                            if (model == null)
                                continue;

                            // 自己不上自己的预览（例如悬停魔女瘴气本身）
                            if (model.GetType() == GetType())
                                continue;

                            if (!seenCards.Add(model.GetType()))
                                continue;

                            tips.Add(new MegaCrit.Sts2.Core.HoverTips.CardHoverTip(
                                PreviewModel(model, upgradeAware && IsUpgraded)));
                        }
                        catch (System.Exception e)
                        {
                            GD.PushWarning("[Subaru] linked card preview failed: " + e.Message);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] build status hover tips failed: " + e.Message);
            }

            // 纯关联牌预览（没有说明文字，只给卡面）
            try
            {
                var text = Description?.GetRawText();
                if (!string.IsNullOrEmpty(text))
                {
                    foreach (var (match, cards, upgradeAware) in LinkedCardTips)
                    {
                        var hit = false;
                        foreach (var m in match)
                        {
                            if (text.Contains(m, System.StringComparison.Ordinal))
                            {
                                hit = true;
                                break;
                            }
                        }

                        if (!hit)
                            continue;

                        foreach (var factory in cards)
                        {
                            try
                            {
                                var model = factory(text);
                                if (model == null || model.GetType() == GetType())
                                    continue;

                                if (!seenCards.Add(model.GetType()))
                                    continue;

                                tips.Add(new MegaCrit.Sts2.Core.HoverTips.CardHoverTip(
                                    PreviewModel(model, upgradeAware && IsUpgraded)));
                            }
                            catch (System.Exception e)
                            {
                                GD.PushWarning("[Subaru] linked card tip failed: " + e.Message);
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] linked card tips failed: " + e.Message);
            }

            // 卡面里写到别的牌的卡名（金色高亮）→ 给那张牌的卡面预览。
            // 只扫本 mod 的卡：ModelDb 里还挂着原版几百张，全扫太亏。
            try
            {
                var text = Description?.GetRawText();
                if (!string.IsNullOrEmpty(text))
                {
                    foreach (var (title, model) in ModCardsByTitle())
                    {
                        if (!text.Contains(title, System.StringComparison.Ordinal))
                            continue;

                        if (model.GetType() == GetType())
                            continue;

                        // 卡名互相包含时只给最长的那条：
                        // 「临时迷尼亚」里含「迷尼亚」，不跳过就会同时挂出两张图（2026-11-18 修）。
                        if (HasLongerTitleHit(title, text))
                            continue;

                        if (!seenCards.Add(model.GetType()))
                            continue;

                        tips.Add(new MegaCrit.Sts2.Core.HoverTips.CardHoverTip(model));
                    }
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning("[Subaru] card name tips failed: " + e.Message);
            }

            return tips;
        }
    }

    /// <summary>
    /// 关联牌预览要不要换成「升级版」。
    ///
    /// <para><c>CardHoverTip</c> 的构造函数只看**传进去那个实例**的 <c>IsUpgraded</c>：
    /// 升级了就渲染成「名字+」并显示升级后的数值，不升级就是原版。
    /// 所以悬停的这张牌是升级版、而它生成/关联的牌也会是升级版时，
    /// 必须现造一份真的升过级的可变副本（<c>ToMutable()</c> + <c>UpgradeInternal()</c>）传进去 ——
    /// 直接传 <c>ModelDb.Card&lt;T&gt;()</c> 的规范模型永远显示原版（2026-11-18 玩家报的现象）。</para>
    /// </summary>
    private static MegaCrit.Sts2.Core.Models.CardModel PreviewModel(
        MegaCrit.Sts2.Core.Models.CardModel canonical, bool upgraded)
    {
        if (!upgraded || canonical == null)
            return canonical;

        try
        {
            var copy = canonical.ToMutable();
            if (!copy.IsUpgraded)
                Extensions.CardUpgradeSync.UpgradeNow(copy);
            return copy;
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] upgraded preview failed: " + e.Message);
            return canonical;
        }
    }

    /// <summary>text 里是否还命中了「包含 title 的更长卡名」（例如 临时迷尼亚 ⊃ 迷尼亚）。</summary>
    private static bool HasLongerTitleHit(string title, string text)
    {
        foreach (var (other, _) in ModCardsByTitle())
        {
            if (other.Length <= title.Length)
                continue;

            if (!other.Contains(title, System.StringComparison.Ordinal))
                continue;

            if (text.Contains(other, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>本 mod 所有卡的「标题 → 原型」，首次用到时建一次（卡名悬停预览用）。</summary>
    private static List<(string Title, MegaCrit.Sts2.Core.Models.CardModel Model)> _modCardsByTitle;

    private static List<(string Title, MegaCrit.Sts2.Core.Models.CardModel Model)> ModCardsByTitle()
    {
        if (_modCardsByTitle != null)
            return _modCardsByTitle;

        var list = new List<(string, MegaCrit.Sts2.Core.Models.CardModel)>();
        try
        {
            foreach (var card in MegaCrit.Sts2.Core.Models.ModelDb.AllCards)
            {
                if (card is not SubaruCard)
                    continue;

                var title = card.Title;
                if (string.IsNullOrEmpty(title))
                    continue;

                title = title.TrimEnd('+').Trim();
                if (title.Length < 2)
                    continue;

                list.Add((title, card));
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] build card title cache failed: " + e.Message);
        }

        _modCardsByTitle = list;
        return list;
    }

    //Image size:
    //Normal art: 1000x760 (Using 500x380 should also work, it will simply be scaled.)
    //Full art: 606x852
    public override string CustomPortraitPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigCardImagePath();
    
    //Smaller variants of card images for efficiency:
    //Smaller variant of fullart: 250x350
    //Smaller variant of normalart: 250x190
    
    //Uses card_portraits/card_name.png as image path. These should be smaller images.
    public override string PortraitPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".CardImagePath();
    public override string BetaPortraitPath => $"beta/{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".CardImagePath();

    /// <summary>
    /// 权能 N：打出后增加 N 层[魔女遗香]。0 表示这张牌没有权能。
    /// 卡面文字里的「权能 N」由各卡的描述负责，这里只管实际效果——
    /// 所以改了数值，务必同步改 cards.json 里那行文字。
    /// </summary>
    protected virtual int Authority => 0;

    /// <summary>
    /// 多人：<b>所有带[腐蚀]词条的牌费用 -1</b>（用户 2026-11-19 定的方案）。
    ///
    /// <para>为什么挂在 <see cref="CardModel.AfterCreated"/>：这是引擎在
    /// <c>RunState.CreateCard</c> / <c>CombatState.CreateCard</c> 之后统一调的生命周期钩子，
    /// 每张**实例牌**都会走一次；卡池里那张 canonical 不动，所以牌组、奖励、生成出来的牌
    /// 都会带上折扣，而图鉴/池子里的原始数据不受影响。</para>
    ///
    /// <para>为什么用 <c>SetCustomBaseCost</c>：<c>CanonicalEnergyCost</c> 是只读属性（背后是私有字段），
    /// 而 <c>CardEnergyCost</c> 提供了 <c>SetCustomBaseCost(int)</c> —— 它写 <c>_base</c> 并
    /// <c>InvokeEnergyCostChanged</c>，卡面费用数字会立刻跟着变。X 费牌（<c>CostsX</c>）跳过，
    /// canonical 已经是 0 的也不再减，避免出现负费。</para>
    /// </summary>
    public override void AfterCreated()
    {
        base.AfterCreated();

        try
        {
            if (!MultiplayerSupport.IsMultiplayer)
                return;

            if (!CanonicalKeywords.Contains(SubaruKeywords.Corrosion))
                return;

            var cost = EnergyCost;
            if (cost == null || cost.CostsX)
                return;

            var canonical = cost.Canonical;
            if (canonical <= 0)
                return;

            cost.SetCustomBaseCost(canonical - 1);

            if (_mpDiscountLogged.Add(GetType()))
            {
                Godot.GD.Print($"[Subaru] mp: corrosion discount {GetType().Name} "
                               + $"{canonical} -> {canonical - 1}");
            }
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] mp corrosion discount failed: " + e.Message);
        }
    }

    /// <summary>同一个类型只打一行折扣日志，免得牌组里十张牌刷十行。</summary>
    private static readonly HashSet<System.Type> _mpDiscountLogged = [];

    /// <summary>
    /// 只能由别的牌生成（如 E·M·T / E·M·M），不进卡牌奖励池和商店。
    /// </summary>
    public virtual bool IsGenerateOnly => false;

    /// <summary>
    /// 硬切：本局走了哪条契约，另一条线的牌就不再进入牌池。
    /// 路线从「牌组里那张契约牌」反推（见 <see cref="Runs.SubaruRunPath"/>），
    /// 所以存档、读档、死亡回归、联机都不用额外存状态。
    ///
    /// <para><b>入口用的是全局路线</b>（队里第一个扫到的契约）。多人一队两个昴、各选不同契约时，
    /// 全局路线只能有一条，所以「按玩家切」的判定不在这里做 —— 见
    /// <see cref="Character.SubaruCardPool.AllowedForPlayer"/>，奖励 / 商店 / 战斗内生成都走那条。
    /// 卡池出口拿不到 player，只好取并集。</para>
    ///
    /// <para>注意 <c>CardModel.IsAllowedInRun</c> 在这两个版本里都没有（2026-11-17 删掉了
    /// <c>SubaruPathFilterPatch</c>），所以没有牌级别的兜底，拦截点全在上面这两处。</para>
    /// </summary>
    public bool IsAllowedOnPath(MegaCrit.Sts2.Core.Runs.IRunState? runState)
        => IsAllowedOnPath(Runs.SubaruRunPath.Current(runState));

    /// <summary>
    /// 同上，但路线由调用方给（多人里 = 那个玩家自己的契约）。子类要按路线特判就覆写这个。
    /// </summary>
    public virtual bool IsAllowedOnPath(Runs.SubaruPath path)
    {
        if (path == Runs.SubaruPath.None)
            return true;

        var isNineGenerals = CanonicalKeywords.Contains(SubaruKeywords.NineGenerals);
        var isYinMagic = CanonicalKeywords.Contains(SubaruKeywords.YinMagic);
        var isCorpseSoldier = CanonicalKeywords.Contains(SubaruKeywords.CorpseSoldier);

        // 大灾（尸兵）线：只出尸兵，帝国军/阴魔法都不出
        if (path == Runs.SubaruPath.Calamity)
            return !isNineGenerals && !isYinMagic;

        // 另外两条线不出尸兵
        if (isCorpseSoldier)
            return false;

        if (path == Runs.SubaruPath.Beatrice && isNineGenerals)
            return false;

        if (path == Runs.SubaruPath.Vincent && isYinMagic)
            return false;

        return true;
    }

    /// <summary>贝蒂在场时这张牌费用为 0（由 BeatricePower 读取）。</summary>
    public virtual bool FreeWithBeatrice => false;

    /// <summary>
    /// 战斗第一回合开始时直接进手牌（不占起手抽牌位）。
    /// 先古契约（贝蒂 / 文森特 / 大灾）把这条件为 true —— 它们不再带[固有]，
    /// 由 <see cref="AncientContractHand"/> 在回合开始后把牌从抽牌堆移进手牌。
    /// </summary>
    public virtual bool EntersHandAtCombatStart => false;

    /// <summary>权能是否已经结算过（避免「迷失」提前结算后又算一遍）。</summary>
    private bool _authorityApplied;

    protected override bool IsPlayable
    {
        get
        {
            // [腐蚀]：整场战斗只能打出一次
            if (Rewind.CorrodedCards.IsUsed(this))
                return false;

            // [贝蒂]词条已取消：贝蒂每局开局必打，限制没有意义
            return base.IsPlayable;
        }
    }

    /// <summary>
    /// 自动打出的放行判定（幻景 / 轰击 / 刀刃陷阱这一类都走这个钩子）。
    ///
    /// 引擎的自动打出只问 <c>ShouldPlay</c>，**不问 <c>IsPlayable</c>**，
    /// 所以「腐蚀牌整场只能用一次」这一条光靠 IsPlayable 挡不住自动打出。
    /// 这里补上：已经被用掉的腐蚀牌，任何自动打出都拒绝。
    /// （现版本没有能碰到腐蚀牌的自打出效果，这层是防以后新增。）
    /// </summary>
    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card != null && ReferenceEquals(card, this) && Rewind.CorrodedCards.IsUsed(this))
            return false;

        return base.ShouldPlay(card, autoPlayType);
    }

    /// <summary>
    /// 模板方法：先结算卡牌自己的效果，再结算权能。
    /// 顺序无所谓（遗香是「你受到的伤害 +%」，不会影响本张牌），但先效果更符合直觉。
    /// </summary>
    protected sealed override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 出牌动作：只给攻击牌播，魔法攻击走 cast、其余走 attack；技能/能力不接动画。
        PlayCardAnimation();

        // 立绘 cut-in（2026-11-21 用户定）：只有覆写了 CutInId 的牌才播。
        // 立绘没出来（本回合名额用掉了）但专属攻击特效照播时，抬手补在这里 ——
        // 不然剑圣第二刀就是「人站着不动、刀光自己飞出去」（2026-11-22 用户定）。
        var cutInPlayed = await TryPlayCutIn();
        if (!cutInPlayed && HasCustomAttackVfx)
            PlayRaiseHand();

        // 对单攻击牌统一接弹幕动画；返回的 Task 在**第一发命中**时完成，
        // 下面会等它再走 OnPlayCard，于是伤害结算落在第一发命中的那一刻。
        // 少数牌（WaitsForVolleyHit = false）不等：动画照播，伤害立刻结算。
        var firstHit = TryPlaySpellVolley(cardPlay);
        var waitHit = WaitsForVolleyHit && firstHit != null;

        // [腐蚀]记账：整场战斗只能打出一次，死归后这张牌会被移出战斗
        Rewind.CorrodedCards.MarkUsed(this);

        // 会等弹幕结算的牌：等待期间把自己调成 70% 透明度（打完/命中后还原）
        var willWait = waitHit || (HandlesOwnVolley && Vfx.SpellVolley.Enabled);
        if (willWait)
            Vfx.CardVisuals.DimWhileWaiting(this);

        try
        {
            if (waitHit)
                await Task.WhenAny(firstHit!, Task.Delay(4000));

            await OnPlayCard(choiceContext, cardPlay);
        }
        finally
        {
            if (willWait)
                Vfx.CardVisuals.Restore(this);
        }

        // [阴魔法]记账：封魔石按「本场打出过的张数」削力量。
        // 放在 OnPlayCard 之后 → 封魔石自己那张不计入本次结算。
        Rewind.YinLedger.MarkPlayed(this);

        // [九神将]：第一张打出去就挂上连锁折扣能力，本回合后续的九神将费用 -1
        if (CanonicalKeywords.Contains(SubaruKeywords.NineGenerals))
            await EnsureNineGeneralsAsync(choiceContext);

        // 能力牌打完就从战斗牌里消失了：记一笔，死归时补回手牌
        Rewind.PlayedPowerCards.Record(CombatState, this);

        // 「本场打出的技能牌数」（阿尔·纱幕用）**不在这里记**：
        // 这里只有本 mod 的牌，原版/其它 mod 的技能会漏。
        // 统一由 Patches.SkillPlayCountPatch 挂 Hook.AfterCardPlayed 统计全量。

        if (Authority <= 0)
            return;

        await ApplyAuthorityNow(choiceContext);
    }

    /// <summary>
    /// [九神将]：第一次打出后挂一个隐藏能力，用来给本回合后续的九神将打折。
    /// 能力每回合开始清标记，所以只有「本回合」有效。
    /// </summary>
    private async Task EnsureNineGeneralsAsync(PlayerChoiceContext choiceContext)
    {
        try
        {
            var creature = Owner?.Creature;
            if (creature == null || creature.HasPower<NineGeneralsPower>())
                return;

            await PowerCmd.Apply<NineGeneralsPower>(
                choiceContext, creature, 1m, creature, this, false);

            GD.Print("[Subaru] nine generals: discount power applied");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] nine generals power failed: " + e);
        }
    }

    /// <summary>
    /// 结算权能 N（增加 N 层[魔女遗香]）。
    /// 默认在 OnPlayCard 之后由 OnPlay 调用；「迷失」这类要在抓死归快照之前结算的牌
    /// 可以自己在 OnPlayCard 里提前调一次，重复调用只生效一次。
    /// </summary>
    protected async Task ApplyAuthorityNow(PlayerChoiceContext choiceContext)
    {
        if (_authorityApplied || Authority <= 0)
            return;

        _authorityApplied = true;

        try
        {
            await PowerCmd.Apply<WitchFragrancePower>(
                choiceContext, Owner.Creature, Authority, Owner.Creature, this, false);

            GD.Print($"[Subaru] authority +{Authority} (fragrance)");
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] authority failed: " + e);
        }
    }

    /// <summary>卡牌本体效果。带权能的牌也只需要实现这个。</summary>
    protected abstract Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay);

    /// <summary>
    /// 是不是「魔法攻击」：带[阴魔法]词条的攻击牌。
    /// 打出时播 <c>cast</c> 动作而不是 <c>attack</c>；个别牌想特事特办，在自己类里 override 即可。
    /// </summary>
    public virtual bool IsMagicAttack =>
        Type == CardType.Attack && CanonicalKeywords.Contains(SubaruKeywords.YinMagic);

    /// <summary>
    /// 出牌动作（只管攻击牌）：
    /// 魔法攻击 → cast，其余攻击 → attack，技能牌 / 能力牌不播。
    /// 原版只会在攻击牌上发 <c>Attack</c>，而且不看牌型；那条触发由
    /// <see cref="Patches.CardAnimationPatch"/> 拦掉，改由这里决定播哪一条。
    /// </summary>
    /// <summary>这张牌是否自己接管弹幕+结算（如 Celestia），接管了就不再播通用动画。</summary>
    public virtual bool HandlesOwnVolley => false;

    /// <summary>
    /// 这张牌有**专属攻击演出**（斩线 / 切屏 / 原子斩…）→ 不播角色自己的攻击 / 施法动作。
    ///
    /// <para>2026-11-22 用户定：**有专属动画就把原动画停了**，以后新做特效的牌一律覆写成 true。
    /// 原版那条 <c>Attack</c> 触发已经被 <see cref="Patches.CardAnimationPatch"/> 拦掉，
    /// 这里再挡掉我们自己补的 <c>attack</c> / <c>cast</c>。</para>
    /// </summary>
    public virtual bool HasCustomAttackVfx => false;

    /// <summary>
    /// 这张牌有**专属命中音**（出刀 / 斩击 / 收刀是自己配的）→ 不再播通用的每段命中音
    /// （<c>single_hit</c> / <c>multi_hit</c> / 对群那几条）。
    ///
    /// <para>2026-11-22 用户定（剑圣）：打击感（顿帧 / 抖动 / 音效）里**只关音效**，
    /// 顿帧和抖动照旧 —— 所以这里和 <see cref="HandlesOwnVolley"/> 分开，不能复用那个标记
    /// （那个会让 <c>AttackSegmentPatch</c> 整张牌跳过）。</para>
    /// </summary>
    public virtual bool HasCustomHitSfx => false;

    /// <summary>
    /// 这张牌打出时播哪条 cut-in（<c>data/cutin.json</c> 的 id；null = 不播）。
    ///
    /// <para>2026-11-21 用户定：斯芬克斯系列只在打出这四张时出场 →
    /// 覆写 <c>CutInId = "sphinx"</c> 即可，接线在 <see cref="TryPlayCutIn"/>。</para>
    ///
    /// <para>策略：带[契约]词条的牌走「招牌演出」（不占每回合名额），其余走普通名额
    /// （同一角色每回合一次 / 每回合最多 N 个不同角色）。</para>
    /// </summary>
    public virtual string? CutInId => null;

    /// <summary>
    /// 打出时播立绘；返回时正好是命中点，后面的结算接着跑。
    /// <para>返回值 = 立绘**真的播了**（没有 CutInId / 名额用掉 / 图加载失败都返回 false），
    /// 调用方据此决定要不要补抬手。</para>
    /// </summary>
    private async Task<bool> TryPlayCutIn()
    {
        var id = CutInId;
        if (string.IsNullOrEmpty(id))
            return false;

        try
        {
            var isContract = CanonicalKeywords?.Contains(SubaruKeywords.Contract) == true;
            var allowed = isContract ? Vfx.CutIn.MayShowSignature(id) : Vfx.CutIn.MayShow(id);
            if (!allowed)
                return false;

            await Vfx.CutIn.PlayToHitAsync(Owner, id);
            return true;
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] cutin hook failed: " + e.Message);
            return false;
        }
    }

    /// <summary>通用弹幕的发数（对单攻击牌）。多段牌在这里 override。</summary>
    public virtual int SpellVolleyHits => 1;

    /// <summary>
    /// 要不要**等弹幕第一发命中**再结算伤害。默认 true（对单攻击牌统一这么做，
    /// 伤害落在第一发命中的那一刻，打击感和弹幕对得上）。
    ///
    /// <para>2026-11-20 玩家反馈「迷尼亚有点拖沓」：这两张牌是低费高频的小攻击，
    /// 等弹幕飞到目标身上才掉血，手感就是卡一拍。override 成 false → 动画照播，
    /// 伤害立刻结算（弹幕变成纯演出）。</para>
    /// </summary>
    public virtual bool WaitsForVolleyHit => true;

    /// <summary>对单攻击牌：播一段弹幕动画（不结算伤害）。</summary>
    private Task? TryPlaySpellVolley(CardPlay cardPlay)
    {
        try
        {
            if (Type != CardType.Attack || TargetType != TargetType.AnyEnemy)
                return null;
            if (HandlesOwnVolley)
                return null;

            // 有专属攻击演出的牌不再叠通用弹幕（2026-11-22 用户定：有专属动画就停掉默认的）。
            // 这里不能改用 HandlesOwnVolley 表达：那个标记会让 AttackSegmentPatch 整张牌跳过
            // （打击音效 + 顿帧一起没）。所以判定加在「要不要发弹幕」这一层。
            if (HasCustomAttackVfx)
            {
                Godot.GD.Print("[Subaru] spell volley skipped (custom attack vfx): " + GetType().Name);
                return null;
            }

            var target = cardPlay?.Target;
            if (target == null || Owner == null)
                return null;

            decimal perHit = 0m;
            try { perHit = DynamicVars.Damage.BaseValue; }
            catch { /* 牌没有 Damage 变量就当 0，只是显示用 */ }

            var hits = Math.Max(1, SpellVolleyHits);
            return Vfx.SpellVolley.PlayVisualAndFirstHit(Owner, target, hits, perHit);
        }
        catch (System.Exception e)
        {
            Godot.GD.PushWarning("[Subaru] spell volley hook failed: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 抬手（<c>Cast</c>）：有专属攻击演出的牌不播默认攻击动作，但**出手时人得抬手**。
    ///
    /// <para>平时这条动作由立绘 cut-in 触发（<see cref="Vfx.CutIn"/> 的 <c>Start</c>）；
    /// 本回合名额用掉、立绘不出来的那次，走这里补上（2026-11-22 用户定：
    /// 「第二次不出立绘，但是攻击特效还在；此时抬手」）。</para>
    /// </summary>
    private void PlayRaiseHand()
    {
        try
        {
            var creature = Owner == null ? null : Owner.Creature;
            var node = creature?.GetCreatureNode();
            if (node == null)
                return;

            node.SetAnimationTrigger("Cast");
            GD.Print("[Subaru] raise hand (no cutin this time): " + GetType().Name);
        }
        catch (System.Exception e)
        {
            GD.Print("[Subaru] raise hand failed: " + e.Message);
        }
    }

    private void PlayCardAnimation()
    {
        if (Type != CardType.Attack)
            return;

        // 有专属演出的牌：不播角色自己的攻击 / 施法动作（2026-11-22 用户定）
        if (HasCustomAttackVfx)
        {
            Godot.GD.Print("[Subaru] card anim skipped (custom vfx): " + GetType().Name);
            return;
        }

        try
        {
            var creature = Owner == null ? null : Owner.Creature;
            if (creature == null)
                return;

            var node = creature.GetCreatureNode();
            if (node == null)
                return;

            node.SetAnimationTrigger(IsMagicAttack ? "Cast" : "Attack");
        }
        catch (System.Exception e)
        {
            GD.Print("[Subaru] card anim failed: " + e.Message);
        }
    }
}
