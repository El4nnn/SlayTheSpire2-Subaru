using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Subaru.SubaruCode.Character;

/// <summary>
/// 战斗立绘场景的根节点脚本。
///
/// 【重要】这个类必须是空的：一旦重写 _Ready / _Process 而不调用 base，
/// NCreatureVisuals 自己的初始化就会被完全替换掉，导致 NCreature 初始化异常
/// （实测：SetOrbManagerPosition 空引用 → 战斗布局崩、敌人不加载）。
/// 所以任何诊断/逻辑都放在子节点里（见 SubaruDiag），不碰根节点回调。
/// </summary>
public partial class SubaruVisualsRoot : NCreatureVisuals
{
}
