using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Subaru.SubaruCode.Rewind;
using Subaru.SubaruCode.Settings;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 创作者模式面板（2026-11-21）：战斗左上角三个测试按钮。
///
/// <para>开关在模组设置页「创作者模式」，值存在 <c>user://subaru_visual.json</c>。
/// 面板在**战斗开始**时建（挂在「结束回合」按钮的父节点上，随战斗界面一起释放），
/// 所以开关切换后下一场战斗才生效。</para>
///
/// <para>三个按钮：
/// <list type="bullet">
///   <item>加一张牌：<c>ModelDb.AllCards</c> 全部实例化后开「选择一张牌」界面，选中的进手牌；</item>
///   <item>能量 +3：<c>PlayerCombatState.GainEnergy</c>；</item>
///   <item>生命上限 +10：<c>CreatureCmd.GainMaxHp</c>。</item>
/// </list></para>
///
/// <para>为什么按钮不用 CanvasLayer 很高的层级：120 足够压住战斗 HUD，
/// 又低于击杀特效（130/131/132）和切屏（133），不会把那些演出挡掉。</para>
/// </summary>
public static class CreatorPanel
{
    private const string NodeName = "SubaruCreatorPanel";
    // 必须比游戏自己的顶栏 UI 高，否则左上角那块会被顶栏控件吃掉鼠标事件
    // （2026-11-21 实测：最上面那个「加一张牌」按钮点不动，能量/生命按钮正常）。
    private const int LayerNumber = 250;

    private static bool _busy;

    /// <summary>战斗开始时由「结束回合按钮初始化」补丁调用。</summary>
    public static void Ensure(NEndTurnButton button, CombatState state)
    {
        try
        {
            if (!SubaruVisualSettings.CreatorModeEnabled)
                return;

            var player = state?.Players?.FirstOrDefault(p => p?.Character is Character.Subaru);
            if (player?.Creature == null)
                return;

            var parent = button?.GetParent();
            if (parent == null)
                return;

            if (parent.HasNode(NodeName))
                return;

            var layer = new CanvasLayer { Name = NodeName, Layer = LayerNumber };
            parent.AddChild(layer);

            var panel = new PanelContainer
            {
                Position = new Vector2(24f, 120f),
                CustomMinimumSize = new Vector2(168f, 0f),
            };
            layer.AddChild(panel);

            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            panel.AddChild(box);

            var title = new Label
            {
                Text = "创作者模式",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.45f));
            box.AddChild(title);

            var search = new LineEdit
            {
                PlaceholderText = "搜索卡名（可留空）",
                CustomMinimumSize = new Vector2(0f, 28f),
            };
            box.AddChild(search);

            var addCard = MakeButton(box, "加一张牌", "从全部卡牌里选一张，直接进手牌（先搜索可以缩小范围）");
            addCard.Pressed += () =>
            {
                GD.Print($"[Subaru] creator: add card pressed (filter='{search.Text}')");
                _ = AddCardAsync(state, player, search.Text);
            };

            var energy = MakeButton(box, "能量 +3", "本回合能量 +3");
            energy.Pressed += () => GainEnergy(player);

            var maxHp = MakeButton(box, "生命上限 +10", "最大生命 +10（同时回 10 点）");
            maxHp.Pressed += () => _ = GainMaxHpAsync(player);

            GD.Print($"[Subaru] creator panel shown (player={player.Character?.Id})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] creator panel failed: " + e.Message);
        }
    }

    private static Button MakeButton(Node parent, string text, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(0f, 30f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        parent.AddChild(button);
        return button;
    }

    // ─────────────────────────────────────────────────────────
    // 三个动作
    // ─────────────────────────────────────────────────────────

    private static async Task AddCardAsync(CombatState state, Player player, string filter)
    {
        if (_busy)
            return;

        _busy = true;
        var created = new List<CardModel>();
        CardModel? chosen = null;

        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            // 先按关键词过滤（空 = 全部）：938 张牌直接铺网格又慢又难找。
            var wanted = ModelDb.AllCards
                .Where(c => c != null && Matches(c, filter))
                .ToList();

            // 只实例化筛出来的那些：选择界面要的是「实例牌」（有 Owner、能正常显示），
            // 直接丢 canonical 模型进去引擎未必接得住（潘多拉也是这么做的，见 CreateCards.cs）。
            foreach (var canonical in wanted)
            {
                try
                {
                    var card = state.CreateCard(canonical, player);
                    if (card != null)
                        created.Add(card);
                }
                catch
                {
                    // 个别牌造不出来（只能在特定场合出现）就算了
                }
            }

            if (created.Count == 0)
            {
                GD.PushWarning($"[Subaru] creator: no card matched '{filter}'");
                return;
            }

            GD.Print($"[Subaru] creator: choose one of {created.Count} cards (filter='{filter}')");

            var ctx = new BlockingPlayerChoiceContext();

            if (created.Count <= 2)
            {
                // 三选一界面只吃 < 3 张（引擎限制，会抛 "Only works with less than 3 cards"）
                chosen = await CardSelectCmd.FromChooseACardScreen(ctx, created, player, true);
            }
            else
            {
                // 多张走「网格」选牌界面
                var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1);
                var picked = await CardSelectCmd.FromSimpleGrid(ctx, created, player, prefs);
                chosen = picked?.FirstOrDefault();
            }

            if (chosen != null)
            {
                await BattleSnapshot.AddCardSafely(pcs.Hand, chosen, pcs);
                DeathReturnSpike.RefreshHandUi(state);
                GD.Print($"[Subaru] creator: card added {chosen.Id} (hand={pcs.Hand.Cards.Count})");
            }
            else
            {
                GD.Print("[Subaru] creator: card choice cancelled");
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] creator add card failed: " + e.Message);
        }
        finally
        {
            // 没选中的候选要清掉，否则它们会一直挂在战斗状态里
            foreach (var card in created)
            {
                if (ReferenceEquals(card, chosen))
                    continue;

                try
                {
                    await CardPileCmd.RemoveFromCombat(card, false);
                }
                catch
                {
                    // 清理失败不影响主流程
                }
            }

            _busy = false;
        }
    }

    /// <summary>关键词匹配：卡名或 ID 里包含就算命中（大小写不敏感，空关键词全命中）。</summary>
    private static bool Matches(CardModel card, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var q = filter.Trim();

        try
        {
            if (!string.IsNullOrEmpty(card.Title) &&
                card.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            var entry = card.Id.Entry;
            if (!string.IsNullOrEmpty(entry) &&
                entry.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // 取不到名字就当不命中
        }

        return false;
    }

    private static void GainEnergy(Player player)
    {
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null)
                return;

            pcs.GainEnergy(3);
            GD.Print($"[Subaru] creator: energy +3 -> {pcs.Energy}");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] creator energy failed: " + e.Message);
        }
    }

    private static async Task GainMaxHpAsync(Player player)
    {
        try
        {
            var creature = player.Creature;
            if (creature == null)
                return;

            await CreatureCmd.GainMaxHp(creature, 10m);
            GD.Print($"[Subaru] creator: max hp +10 -> {creature.MaxHp} (hp={creature.CurrentHp})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] creator max hp failed: " + e.Message);
        }
    }
}
