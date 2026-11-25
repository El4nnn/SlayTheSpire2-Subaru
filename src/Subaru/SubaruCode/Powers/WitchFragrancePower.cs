using System.Linq;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>魔女遗香：每层使受到的伤害 +1%（走原生伤害修正，意图数字同步变化）。</summary>
public sealed class WitchFragrancePower : CustomPowerModel
{
    private static int _lastLoggedStacks = int.MinValue;

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => true;

    public override decimal ModifyDamageMultiplicative(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? card, CardPlay cardPlay)
    {
        // 注意：这个钩子返回的是「倍率」，引擎会自己乘上去（Hook.ModifyDamageInternal 里是 damage *= 返回值）。
        // 以前这里返回的是算好的伤害，于是伤害被乘了两遍——12 点伤害打出 158 就是这么来的。
        if (target != Owner || Amount == 0)
            return 1m;

        var multiplier = 1m + Amount * 0.01m;

        // 每次层数变化后第一次结算打一行：层数 / 倍率 / **乘完还没取整的值**。
        // 以前是「最多打 15 行」，开局几场就用完了，后期 86 层那种情况永远抓不到；
        // 改成按层数变化去重后，日志量小且不会漏（2026-11-17 玩家反馈「倍率和层数对不上」时改的）。
        // instances>1 说明出现了重复实例：引擎按实例连乘，会变成 1.2×1.2。
        if (Amount != _lastLoggedStacks)
        {
            _lastLoggedStacks = Amount;
            var instances = Owner?.Powers?.Count(p => p is WitchFragrancePower) ?? 0;
            GD.Print(
                $"[Subaru] fragrance dmg: in={amount}, x{multiplier}, " +
                $"out={amount * multiplier}, stacks={Amount}, instances={instances}");
        }

        return multiplier;
    }

    public override string CustomPackedIconPath => "witch_fragrance_power.png".PowerImagePath();

    public override string CustomBigIconPath => "witch_fragrance_power.png".BigPowerImagePath();

    // 关键：只允许一个实例。默认的 Instanced 会为每次 Apply 新建一份，
    // 而引擎是**按实例连乘**（Hook.ModifyDamageInternal：damage *= 每个监听者的返回值），
    // 所以多一份实例就等于 1.2×1.2=1.44，而不是 1.4（2026-11-17 用 IL 更正了这里原来「求和」的旧注释）。
    public override PowerInstanceType InstanceType => PowerInstanceType.None;
}
