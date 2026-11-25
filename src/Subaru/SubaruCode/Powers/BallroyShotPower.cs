using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using Subaru.SubaruCode.Extensions;

namespace Subaru.SubaruCode.Powers;

/// <summary>
/// 「魔弹射手·巴尔罗伊」的两段延迟伤害队列。
///
/// 挂在玩家身上：每次打出魔弹登记一发，每发在之后两个玩家回合开始时各结算一段
/// （第一段 15，第二段 20）。**多发的延迟可以并存**：
/// 2026-11-19 修掉了「第二发把第一发还没结算的那段覆盖掉」的问题
/// （以前是单个 _stage/_target，重复打出会 Configure 归零、旧的那发直接丢失）。
///
/// 目标在中途死亡 → 只丢掉那一发，不会改打别人。
/// 不显示图标（它只是延迟结算的记账）。
/// </summary>
public sealed class BallroyShotPower : CustomPowerModel
{
    /// <summary>一发延迟伤害。</summary>
    private sealed class Shot
    {
        public Creature? Target;
        public decimal First;
        public decimal Second;
        public int Stage;
    }

    private readonly List<Shot> _shots = new();

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.None;

    protected override bool IsVisibleInternal => false;

    public override string CustomPackedIconPath => "madness_power.png".PowerImagePath();

    public override string CustomBigIconPath => "madness_power.png".BigPowerImagePath();

    /// <summary>登记一发新的延迟伤害（不会影响已经在队列里的那些）。</summary>
    public void AddShot(Creature target, decimal first, decimal second)
    {
        _shots.Add(new Shot
        {
            Target = target,
            First = first,
            Second = second,
            Stage = 0,
        });

        Godot.GD.Print($"[Subaru] ballroy: queued shot on {target.Name} ({first}/{second}), queue={_shots.Count}");
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        var owner = Owner;
        if (owner?.Player == null || player != owner.Player)
            return;

        if (_shots.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        // 倒序遍历：结算完的从列表里删掉不会影响还没结算的
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];

            if (shot.Target == null || shot.Target.IsDead)
            {
                Godot.GD.Print("[Subaru] ballroy echo: target gone, dropping that shot");
                _shots.RemoveAt(i);
                continue;
            }

            var damage = shot.Stage == 0 ? shot.First : shot.Second;

            // 数值在打出时就已经锁定（存在 First/Second 里），
            // 所以这里先减掉「当前力量」——引擎结算时会再按当前力量加一次，净额回到锁定值。
            // 但易伤/减伤这些倍率仍然由引擎正常乘上，所以「结算时吃易伤」成立。
            var strength = owner.GetPowerAmount<StrengthPower>();
            var payload = damage - strength;

            // 三段伤害三档光柱：本体（1 档）→ 第一发延迟（2 档）→ 第二发延迟（3 档）；
            // 终结必杀则把这一击的攻击动画换成终结动画（返回时正好是命中帧）
            if (Vfx.BallroyBeam.WillBeFinisherKill(owner.Player, shot.Target, payload, ValueProp.Move))
            {
                Godot.GD.Print("[Subaru] ballroy echo: finisher kill -> attack anim becomes 终结动画");

                // 延迟段造成终结时**也出立绘**（2026-11-23 用户追加）：
                // 和本体那条路同一个节奏 —— 立绘先出、返回到命中点，再放终结动画，
                // 所以「打到终结」的瞬间一定有巴尔罗伊的脸。
                if (Vfx.CutIn.MayShow("ballroy"))
                    await Vfx.CutIn.PlayToHitAsync(owner.Player, "ballroy");

                await Vfx.BallroyBeam.PlayFinisherAttackAsync(shot.Target);
            }
            else
            {
                await Vfx.BallroyBeam.PlayAsync(shot.Target, shot.Stage + 2);
            }

            shot.Stage++;

            Vfx.BallroyBeam.NoteDamage();

            // 这段演出有 300~500ms：目标可能已经在这半秒里死了（被本段前置伤害打死，
            // 或被别的来源打死）。对死目标结算时，引擎与我们/别人的伤害补丁都可能取到空数据；
            // 而这里一旦抛出，会直接打断 Hook.AfterPlayerTurnStart → 回合循环死掉、
            // 整场战斗卡住只能重开房间（2026-11-24 玩家实测：P3 的伤害批处理补丁在此 NRE）。
            if (shot.Target == null || shot.Target.IsDead)
            {
                Godot.GD.Print(
                    $"[Subaru] ballroy echo {shot.Stage}: target already dead, dropping that shot");
                _shots.RemoveAt(i);
                continue;
            }

            try
            {
                await CreatureCmd.Damage(
                    choiceContext, shot.Target, payload, ValueProp.Move, owner);
            }
            catch (System.Exception e)
            {
                // 只记日志，绝不把异常交回 hook：整场战斗卡死的代价远大于丢一段伤害
                Godot.GD.PushWarning(
                    $"[Subaru] ballroy echo {shot.Stage} damage skipped: {e.GetType().Name}: {e.Message}");
                _shots.RemoveAt(i);
                continue;
            }

            Godot.GD.Print($"[Subaru] ballroy echo {shot.Stage}: {damage} to {shot.Target.Name}"
                           + $" (payload {payload}, current str {strength})");

            if (shot.Stage >= 2)
                _shots.RemoveAt(i);
        }

        if (_shots.Count == 0)
            await PowerCmd.Remove(this);
    }
}
