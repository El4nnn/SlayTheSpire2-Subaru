using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Subaru.SubaruCode.Cards;

/// <summary>
/// 荆棘帝·尤加尔德：稀有，2 费，能力。[尸兵]。
/// 获得 7 点[棘刺]。升级：7→12（2026-11-19 削弱：基础 10 → 7，升级增量仍是 +5）。
///
/// <para>数值走 <c>PowerVar&lt;ThornsPower&gt;</c>，和「纱幕」的虚弱/易伤同一套写法：
/// 卡面用 <c>{ThornsPower:diff()}</c>，升级只动 <c>DynamicVars.Thorns</c>，
/// 卡面数字与实际授予的层数永远是同一个来源。</para>
/// </summary>
public sealed class Yugard() : SubaruCard(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override HashSet<CardKeyword> CanonicalKeywords =>
        [.. base.CanonicalKeywords, SubaruKeywords.CorpseSoldier];

    public override string? CutInId => "yugard";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new PowerVar<ThornsPower>(7m),
    ];

    protected override void OnUpgrade() => DynamicVars["ThornsPower"].UpgradeValueBy(5m);   // 7 → 12

    protected override async Task OnPlayCard(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner;
        if (owner?.Creature == null)
            return;

        // PowerVar<T> 的键名是 typeof(T).Name（这里是 "ThornsPower"），
        // 和卡面占位符 {ThornsPower:diff()} 同名；荆棘不在 DynamicVarSet 的 29 个强类型属性里，
        // 所以只能走索引器，不能用 DynamicVars.Thorns。
        var amount = DynamicVars["ThornsPower"].BaseValue;

        Godot.GD.Print($"[Subaru] yugard played: +{amount} thorns");

        await PowerCmd.Apply<ThornsPower>(
            choiceContext, owner.Creature, amount, owner.Creature, this, false);
    }
}
