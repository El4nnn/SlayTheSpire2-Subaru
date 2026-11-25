using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using Subaru.SubaruCode.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 魔女遗香的减法（大贤者 / 从容共用）。
/// 层数减到 0 或以下就把能力整个移除，避免留一个 0 层的图标。
/// </summary>
public static class FragranceReducer
{
    public static async Task Reduce(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Players.Player player, decimal amount)
    {
        try
        {
            var fragrance = player.Creature.Powers.FirstOrDefault(p => p is WitchFragrancePower);
            if (fragrance == null)
                return;

            var left = fragrance.Amount - (int)amount;
            GD.Print($"[Subaru] fragrance reduced: {fragrance.Amount} -> {left}");

            if (left <= 0)
                await PowerCmd.Remove(fragrance);
            else
                fragrance.Amount = left;
        }
        catch (System.Exception e)
        {
            GD.PushError("[Subaru] fragrance reduce failed: " + e);
        }
    }
}

/// <summary>
/// 大贤者：罕见，2 费，能力。减少 25 层[魔女遗香]。升级：25→35。
///
/// <para>2026-11-21 用户新增：同时获得 <see cref="GreatSagePower"/> ——
/// **每次死归时，死归加成额外触发一次**（升级不影响这一条，只提高减少遗香的量）。</para>
/// </summary>
public sealed class GreatSage() : SubaruCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    // 【腐蚀】：遗香不随死归清空、但牌堆会回档，不加这条就能靠反复死归把遗香刷归零
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Reduce", 25m)];

    protected override void OnUpgrade() => DynamicVars["Reduce"].UpgradeValueBy(10m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await FragranceReducer.Reduce(choiceContext, Owner, DynamicVars["Reduce"].BaseValue);

        await PowerCmd.Apply<GreatSagePower>(
            choiceContext, Owner.Creature, 1m, Owner.Creature, this, false);

        GD.Print("[Subaru] great sage: rewind bonus +1 extra trigger");
    }
}

/// <summary>冷静思考：普通，0 费，能力（一次性结算）。减少 10 层[魔女遗香]。升级：10→15。</summary>
public sealed class Composure() : SubaruCard(0, CardType.Power, CardRarity.Common, TargetType.Self)
{
    // 【腐蚀】：同上，防止死归刷遗香
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.Corrosion];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Reduce", 10m)];

    protected override void OnUpgrade() => DynamicVars["Reduce"].UpgradeValueBy(5m);

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await FragranceReducer.Reduce(choiceContext, Owner, DynamicVars["Reduce"].BaseValue);
    }
}
