using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Subaru.SubaruCode.Powers;
using Subaru.SubaruCode.Rewind;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 在「结束回合」按钮正上方 200px 加一个「立即死归」按钮：
/// 直接套用结束回合按钮的贴图（同样大小）+ 紫色滤镜，点一下按一次死亡结算并回档。
///
/// 位置/尺寸不能在建按钮时算：那时结束回合按钮还没完成布局，取到的是 0 和错的位置。
/// 所以挂一个跟随节点，每帧同步「尺寸 = 结束回合按钮」「位置 = 它的正上方 200px」。
/// </summary>
[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.Initialize))]
public static class EndTurnButtonInitializePatch
{
    private static void Postfix(NEndTurnButton __instance, CombatState state)
    {
        ImmediateRewindButton.Ensure(__instance, state);

        // 创作者模式面板（开关在模组设置页）：只建一次，随战斗界面一起释放
        CreatorPanel.Ensure(__instance, state);
    }
}

public static class ImmediateRewindButton
{
    private const string NodeName = "SubaruImmediateRewind";
    private const string ButtonTexture = "res://images/packed/combat_ui/end_turn_button.png";

    /// <summary>相对结束回合按钮往上偏移的像素。</summary>
    private const float OffsetAbove = 100f;

    private static CombatState _state;

    /// <summary>结束回合按钮本体，用来同步可点击时段。</summary>
    internal static NEndTurnButton SourceButton;

    public static void Ensure(NEndTurnButton original, CombatState state)
    {
        try
        {
            _state = state;
            SourceButton = original;

            // 本 mod 会挂进所有角色的战斗（Harmony 补丁是全局的），
            // 但按钮只属于菜月昴：别的角色这一局没有死归，出现按钮纯属多余。
            if (!IsSubaruCombat(state))
                return;

            // 多人里没有死归：按钮不建（真按了也只会把两端状态搞分叉）。
            if (!MultiplayerSupport.RewindEnabled)
            {
                GD.Print("[Subaru] immediate rewind button skipped: multiplayer");
                return;
            }

            // 「立即死归」要等「死亡回归」节点解锁（累计 10 次死归）后才出现。
            // 没解锁时连节点都不建，战斗界面里看不到这个按钮。
            if (!RewindProgress.ImmediateRewindUnlocked)
            {
                GD.Print($"[Subaru] immediate rewind button locked (rewind total {RewindProgress.Total}/{RewindProgress.UnlockThreshold})");
                return;
            }

            if (original == null || !GodotObject.IsInstanceValid(original))
                return;

            var parent = original.GetParent();
            if (parent == null)
                return;

            if (parent.HasNode(NodeName))
                return;

            var texture = GD.Load<Texture2D>(ButtonTexture);

            var button = new TextureButton
            {
                Name = NodeName,
                TextureNormal = texture,
                TopLevel = true,
                Modulate = new Color(0.66f, 0.40f, 1.0f, 1.0f),
                MouseFilter = Control.MouseFilterEnum.Stop,
                // 关键：默认会按贴图原始尺寸渲染，忽略我们设的 Size
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
            };
            parent.AddChild(button);

            var label = new Label
            {
                Text = "立即死归",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.AddThemeColorOverride("font_color", new Color(0.96f, 0.90f, 1.0f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.05f, 0.18f));
            label.AddThemeConstantOverride("outline_size", 8);
            button.AddChild(label);

            button.Pressed += OnPressed;

            // 找「结束回合」按钮真正的贴图节点（Control 本身比贴图大，直接用会偏大错位）
            Control art = null;
            try
            {
                var path = NEndTurnButton.EndTurnButtonPath;
                if (!string.IsNullOrEmpty(path))
                    art = original.GetNodeOrNull<Control>(path);
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] locate end turn art failed: " + e.Message);
            }

            GD.Print($"[Subaru] end turn button: rect={original.Size}, art={(art != null ? art.Size.ToString() : "null")}");

            // 每帧跟随结束回合按钮的尺寸与位置
            var follow = new RewindButtonFollow
            {
                Name = NodeName + "Follow",
                Button = button,
                Original = original,
                Art = art,
                OffsetAbove = OffsetAbove,
                State = state,
            };
            parent.AddChild(follow);

            GD.Print("[Subaru] immediate rewind button created (follows end turn button)");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] add rewind button failed: " + e);
        }
    }

    /// <summary>这一场战斗是不是菜月昴的；不是就不建按钮。</summary>
    private static bool IsSubaruCombat(CombatState state)
    {
        try
        {
            var player = state?.Players?.FirstOrDefault();
            return player?.Character is Subaru.SubaruCode.Character.Subaru;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 按下瞬间再判一次「现在是不是玩家能操作的窗口」。
    /// 贴图/鼠标态的更新在 _Process 里，比点击晚一帧 —— 敌方回合刚切过去的那一帧，
    /// 按钮可能还是可点的样子，所以这里必须再挡一次。
    /// </summary>
    private static bool ActionsLocked()
    {
        try
        {
            var manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress)
                return true;

            return manager.PlayerActionsDisabled
                || manager.IsEnemyTurnStarted
                || manager.EndingPlayerTurnPhaseOne
                || manager.EndingPlayerTurnPhaseTwo;
        }
        catch
        {
            return false;
        }
    }

    private static void OnPressed()
    {
        try
        {
            if (_state == null)
                return;

            if (ActionsLocked())
            {
                GD.Print("[Subaru] immediate rewind button ignored: not player action window");
                return;
            }

            // 演出期间连点会被吞掉：真正上锁在 DeathReturnSpike 里，这里只是省一次无谓调用
            if (DeathReturnSpike.IsRewinding)
            {
                GD.Print("[Subaru] immediate rewind button ignored: rewinding");
                return;
            }

            GD.Print("[Subaru] immediate rewind button pressed");
            _ = DeathReturnSpike.ForceRewindAsync(_state);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] rewind button pressed failed: " + e);
        }
    }
}

/// <summary>
/// 每帧把自建按钮摆到「以屏幕中轴线为轴、把结束回合按钮镜像到左半边」的位置（再上移一段），并复制它的尺寸。
/// </summary>
public partial class RewindButtonFollow : Node
{
    public Control Button;
    public Control Original;
    public Control Art;
    public float OffsetAbove = 100f;

    /// <summary>镜像到左半边之后再往上抬的像素（2026-11-18 玩家定：中轴线对称 + 上移 50）。</summary>
    public float MirrorShiftUp = 50f;

    public CombatState State;

    /// <summary>
    /// 死归流程结束之后还要再按住多久（秒）。
    /// 释放点以「死归流程真正跑完」为准（DeathReturnSpike.IsRewinding 变 false），
    /// 这 1 秒只是防手滑的尾部冷却——流程本身有几秒演出，不需要在这里再堆时间。
    /// </summary>
    private const double CooldownAfterRewind = 1.0;

    private double _cooldownLeft;
    private bool _wasRewinding;

    // 选牌界面检测缓存（100 ms 一次，别每帧遍历整棵树）
    private double _selectionCheckCooldown;
    private bool _selectionOpen;

    private void Cleanup()
    {
        if (GodotObject.IsInstanceValid(Button))
            Button.QueueFree();

        QueueFree();
    }

    private bool IsMadnessActive()
    {
        try
        {
            var player = State?.Players?.FirstOrDefault();
            return player?.Creature != null && player.Creature.HasPower<MadnessPower>();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 现在这个按钮该不该存在。
    /// 只看「结束回合按钮还在不在场景树里」是不够的：战斗胜利后的结算、领奖励、
    /// 以及死亡结算界面，结束回合按钮都还留在树里（只是被上面的界面盖住），
    /// 于是清理条件永远不成立、按钮就浮在结算界面上。
    /// 所以改成看战斗本身：战斗结束、或玩家已经死了，都要收起来。
    /// </summary>
    private bool IsCombatUsable()
    {
        try
        {
            if (State == null)
                return false;

            // 真正的开关在 CombatManager 上（CombatState.IsLiveCombat() 是个恒返回 true 的桩，
            // 别拿它当判据）：
            //   IsInProgress  —— 战斗是否进行中
            //   IsOverOrEnding —— 胜利/失败后进入结算，这个比界面切换更早
            //   IsAboutToLose  —— 玩家已经要输了（死亡结算界面就是这一段）
            var manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress)
                return false;
            if (manager.IsOverOrEnding || manager.IsAboutToLose)
                return false;

            var creature = State.Players?.FirstOrDefault()?.Creature;
            if (creature == null)
                return false;

            // 死亡结算界面：人已经死了，按钮没有意义
            if (creature.IsDead || creature.CurrentHp <= 0)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 现在是否挂着「选牌」界面（玩家正在从若干张牌里选）。
    ///
    /// <para>为什么要在死归按钮上挡这一条：选牌是 <c>ICardSelectionScreen.CardsSelected()</c>
    /// 的 await，引擎<b>没有取消入口</b>（PlayerChoiceContext / PlayerChoiceSynchronizer 都只有
    /// 开始/结束信号）。选牌期间点死归，状态会被灌回存档点，但那个 await 还挂着 ——
    /// 玩家选完之后，药水 / 幼女使者照样把牌加进<b>还原后</b>的手牌，等于白赚一张
    /// （2026-11-21 实测：力量药水）。</para>
    ///
    /// <para>原版对「选牌时按结束回合」也是让选择优先：<c>CardSelectCmd</c> 的每个选牌入口
    /// 都会 <c>UndoEndTurnIfNecessary</c> 把已经按下的结束回合撤销。我们没法撤销死归
    /// （它已经改完状态了），所以在入口直接锁住。</para>
    /// </summary>
    public static bool IsCardSelectionOpen()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.Root;
            return root != null && HasVisibleSelection(root);
        }
        catch
        {
            return false;
        }

        static bool HasVisibleSelection(Node node)
        {
            if (node is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.ICardSelectionScreen
                && node is CanvasItem item
                && item.IsVisibleInTree())
                return true;

            foreach (var child in node.GetChildren())
            {
                if (HasVisibleSelection(child))
                    return true;
            }

            return false;
        }
    }

    /// <summary>带 100 ms 缓存的版本，给每帧的 _Process 用。</summary>
    private bool IsCardSelectionOpenCached(double delta)
    {
        _selectionCheckCooldown -= delta;
        if (_selectionCheckCooldown > 0)
            return _selectionOpen;

        _selectionCheckCooldown = 0.1;
        _selectionOpen = IsCardSelectionOpen();
        return _selectionOpen;
    }

    public override void _Process(double delta)
    {
        // 战斗结束（结束回合按钮离开场景树）或节点被释放 → 连同按钮一起销毁，
        // 否则 TopLevel 的按钮会一直浮在结算/地图界面上。
        if (!GodotObject.IsInstanceValid(Button) || !GodotObject.IsInstanceValid(Original))
        {
            Cleanup();
            return;
        }

        if (!Original.IsInsideTree())
        {
            GD.Print("[Subaru] combat ended: removing immediate rewind button");
            Cleanup();
            return;
        }

        // 战斗结算 / 领奖励 / 死亡结算时，结束回合按钮仍然在树里且 visible，
        // 但这时按钮不该出现——这里自己判定，不依赖树结构。
        var usable = IsCombatUsable();

        // TopLevel 不继承父节点可见性，战斗 UI 隐藏时要自己跟着隐藏
        Button.Visible = usable && Original.IsVisibleInTree();

        if (!usable)
        {
            // 藏起来的同时断掉点击：万一 Visible 被别的东西改回来，也不会误触
            if (Button is TextureButton hidden)
                hidden.Disabled = true;
            Button.MouseFilter = Control.MouseFilterEnum.Ignore;
            return;
        }

        // 结束回合按钮的 Control 矩形比贴图大得多（边缘留白），直接复制会偏大且错位，
        // 所以优先对齐它的贴图子节点。
        var anchor = Art != null && GodotObject.IsInstanceValid(Art) ? Art : Original;

        // 癫狂期间禁用：此时点死归会跳过回合
        var madness = IsMadnessActive();

        // 死归流程进行中禁用；流程结束后再冷却 CooldownAfterRewind 秒
        var rewinding = DeathReturnSpike.IsRewinding;
        if (rewinding && !_wasRewinding)
            _cooldownLeft = CooldownAfterRewind;
        _wasRewinding = rewinding;
        if (!rewinding && _cooldownLeft > 0)
            _cooldownLeft = Math.Max(0, _cooldownLeft - delta);

        // 和「结束回合」按钮同步：它此刻按下去没用的时候，死归按钮同样锁住。
        // 不能只看按钮自身的 MouseFilter / 可见性 —— 敌方回合里结束回合按钮并没有被隐藏，
        // 也没有设成 Ignore，只是「按了不算数」。真正的开关在 CombatManager 上：
        //   PlayerActionsDisabled —— 玩家回合开始时置 false，结束回合/敌方回合置 true
        //   IsEnemyTurnStarted / EndingPlayerTurnPhaseOne/Two —— 结算与敌方回合窗口
        var src = ImmediateRewindButton.SourceButton;
        var manager = CombatManager.Instance;
        var endTurnBlocked = manager == null
            || !manager.IsInProgress
            || manager.PlayerActionsDisabled
            || manager.IsEnemyTurnStarted
            || manager.EndingPlayerTurnPhaseOne
            || manager.EndingPlayerTurnPhaseTwo
            || src == null
            || !GodotObject.IsInstanceValid(src)
            || src.MouseFilter == Godot.Control.MouseFilterEnum.Ignore
            || !src.IsVisibleInTree();

        // 选牌界面开着时锁住：那条 await 引擎没法取消，选完会往还原后的手牌里加牌。
        var selectionOpen = IsCardSelectionOpenCached(delta);

        var locked = madness || rewinding || _cooldownLeft > 0 || endTurnBlocked || selectionOpen;

        if (Button is TextureButton tb)
            tb.Disabled = locked;
        Button.MouseFilter = locked ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        Button.Modulate = locked
            ? new Color(0.38f, 0.30f, 0.45f, 0.55f)
            : new Color(0.66f, 0.40f, 1.0f, 1.0f);

        var size = anchor.Size;
        if (size.X < 1f || size.Y < 1f)
            return;

        // 以屏幕竖直中轴线为轴，把「结束回合」按钮的位置镜像到左半边，再上移 MirrorShiftUp。
        // 视口是 canvas_items + expand 拉伸，Control 的坐标单位就是 GetViewportRect() 的单位，
        // 所以直接用视口宽度算镜像；窗口比例变化时也会自动跟着重新算。
        var viewportWidth = Button.GetViewportRect().Size.X;
        if (viewportWidth < 1f)
            return;

        Button.Size = size;
        Button.GlobalPosition = new Vector2(
            viewportWidth - anchor.GlobalPosition.X - size.X,
            anchor.GlobalPosition.Y - OffsetAbove - MirrorShiftUp);
    }
}
