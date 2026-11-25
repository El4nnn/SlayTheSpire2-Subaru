using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace Subaru.SubaruCode.Runs;

/// <summary>本局走的契约路线。</summary>
public enum SubaruPath
{
    /// <summary>还没选（涅奥那一步之前）。</summary>
    None = 0,

    /// <summary>契约·贝蒂：阴魔法线。</summary>
    Beatrice = 1,

    /// <summary>鲜血帝·文森特：帝国军（原「九神将」）线。</summary>
    Vincent = 2,

    /// <summary>大灾·斯芬克斯：尸兵线。</summary>
    Calamity = 3,
}

/// <summary>
/// 路线从「牌组里那张契约牌」反推，不另存状态：
/// 存档 / 读档 / 死亡回归 / 联机都不会丢，也不用管 static 什么时候被重置。
///
/// <para><b>多人里「一队多条路线」</b>（2026-11-19 玩家实测：一个昴选大灾、一个选贝蒂）：
/// 路线不是全局唯一的 —— 每个昴的牌组里各有一张契约。所以这里给三种口径：</para>
/// <list type="bullet">
/// <item><see cref="Current(IRunState)"/>：队里第一个扫到的契约（老口径，只用于兜底）</item>
/// <item><see cref="ForPlayer"/>：**这个玩家自己**的契约（奖励 / 商店 / 战斗内生成用这个）</item>
/// <item><see cref="All"/>：队里出现过的全部契约（卡池出口这种拿不到 player 的地方，取并集）</item>
/// </list>
/// </summary>
public static class SubaruRunPath
{
    /// <summary>队里第一个扫到的契约（兜底用；多人里可能不是「本玩家」的）。</summary>
    public static SubaruPath Current(IRunState? runState)
    {
        try
        {
            if (runState?.Players == null)
                return SubaruPath.None;

            foreach (var player in runState.Players)
            {
                var path = ForPlayer(player);
                if (path != SubaruPath.None)
                    return path;
            }
        }
        catch (Exception)
        {
            // 读不到就当没选：宁可不切，也不要让发牌直接崩
        }

        return SubaruPath.None;
    }

    /// <summary>只看这一个玩家的牌组。没契约（不是昴 / 还没在涅奥处选）返回 <see cref="SubaruPath.None"/>。</summary>
    public static SubaruPath ForPlayer(Player? player)
    {
        try
        {
            var cards = player?.Deck?.Cards;
            if (cards == null)
                return SubaruPath.None;

            foreach (var card in cards)
            {
                if (card is Cards.BeatriceContract)
                    return SubaruPath.Beatrice;

                if (card is Cards.VincentContract)
                    return SubaruPath.Vincent;

                if (card is Cards.SphinxContract)
                    return SubaruPath.Calamity;
            }
        }
        catch (Exception)
        {
        }

        return SubaruPath.None;
    }

    /// <summary>队里出现过的全部契约（没人选就是空集 —— 空集表示「不按路线切」）。</summary>
    public static HashSet<SubaruPath> All(IRunState? runState)
    {
        var set = new HashSet<SubaruPath>();
        try
        {
            if (runState?.Players == null)
                return set;

            foreach (var player in runState.Players)
            {
                var path = ForPlayer(player);
                if (path != SubaruPath.None)
                    set.Add(path);
            }
        }
        catch (Exception)
        {
        }

        return set;
    }
}
