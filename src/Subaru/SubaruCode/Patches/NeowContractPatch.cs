using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Subaru.SubaruCode.Cards;
using Subaru.SubaruCode.Timeline;

namespace Subaru.SubaruCode.Patches;

/// <summary>
/// 涅奥开场多一个「回合」：先选契约（走哪条线），选完再回到涅奥原本的祝福选项。
///
/// 为什么不用 RitsuLib 的 <c>ModAncientOptionRegistry</c>：它只能往初始选项里**追加**一条，
/// 没法把「契约」做成必须经过的第一页。所以这里直接给
/// <see cref="AncientEventModel.SetInitialEventState"/> 挂 postfix，
/// 抢先换成契约页，选完再把涅奥原来的 Description + 选项原样放回去。
///
/// 不能重跑 <c>SetInitialEventState</c> 来「回去」：那会重摇祝福，
/// 而且 RitsuLib 也会把额外选项再追加一次，出现重复项。
/// </summary>
[HarmonyPatch(typeof(AncientEventModel), "SetInitialEventState")]
public static class NeowContractPatch
{
    private sealed record Snapshot(LocString Description, List<EventOption> Options);

    /// <summary>涅奥原本的页面，按实例存（弱表，事件实例释放后自动清）。</summary>
    private static readonly ConditionalWeakTable<AncientEventModel, Snapshot> Saved = new();

    /// <summary><c>EventModel.SetEventState</c> 是 protected，只能反射调用。</summary>
    private static readonly MethodInfo? SetEventStateMethod = typeof(EventModel).GetMethod(
        "SetEventState",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
        null,
        [typeof(LocString), typeof(IEnumerable<EventOption>)],
        null);

    [HarmonyPostfix]
    public static void Postfix(AncientEventModel __instance)
    {
        if (__instance is not Neow neow)
            return;

        // 只给菜月昴插这一步
        if (neow.Owner?.Character is not Character.Subaru)
            return;

        // 一个涅奥实例只插一次
        if (Saved.TryGetValue(neow, out _))
            return;

        try
        {
            var snapshot = new Snapshot(
                neow.Description,
                neow.CurrentOptions?.ToList() ?? []);
            Saved.Add(neow, snapshot);

            ShowChoosePage(neow);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract page failed: " + e);
        }
    }

    private static void ShowChoosePage(Neow neow)
    {
        var options = new List<EventOption>
        {
            MakeOption(neow, "BEATRICE", () => ChooseAsync(neow, ContractKind.Beatrice)),
        };

        // 帝国军 / 大灾：单人下要「帝国的剑狼」节点（累计击败 10 个首领）解锁。
        //
        // 多人下**三个选项必须恒定存在**：事件选项是按数组下标同步的
        // （`OptionIndexChosenMessage` 只带 optionIndex，接收端取 `CurrentOptions[index]`，
        // 越界直接 throw），而解锁进度是本机存档 —— 房主和队友几乎不可能一致，
        // 只要两端选项数不同就会在点选项时炸掉。所以多人里绕开节点判断，三个都给；
        // 各选项的实际效果在 handler 里照常结算。
        if (MultiplayerSupport.IsMultiplayer || SubaruEpochGate.IsObtained(SubaruEpochGate.SwordWolfEpochId))
        {
            options.Add(MakeOption(neow, "VINCENT", () => ChooseAsync(neow, ContractKind.Vincent)));

            // 大灾（尸兵线）：和帝国军同一条门槛 —— 剑狼一解锁，涅奥这里就多出「契约·大灾」。
            // 牌池里的尸兵仍然由「大灾」节点控制（见 SubaruCardPool.CardGates）；
            // 没解锁时靠契约牌斯芬克斯自己生成尸兵，路线照样能打。
            options.Add(MakeOption(neow, "SPHINX", () => ChooseAsync(neow, ContractKind.Sphinx)));
        }

        SetState(neow, PageText(), options);
    }

    private static EventOption MakeOption(Neow neow, string id, Func<Task> onChosen) =>
        new(
            neow,
            onChosen,
            new LocString("events", $"SUBARU-NEOW_CONTRACT.options.{id}.title"),
            new LocString("events", $"SUBARU-NEOW_CONTRACT.options.{id}.description"),
            $"SUBARU_NEOW_{id}",
            Array.Empty<IHoverTip>());

    /// <summary>涅奥契约的三条线。</summary>
    private enum ContractKind
    {
        Beatrice,
        Vincent,
        Sphinx,
    }

    private static async Task ChooseAsync(Neow neow, ContractKind kind)
    {
        try
        {
            var player = neow.Owner;
            if (player == null)
                return;

            CardModel? card = kind switch
            {
                ContractKind.Vincent => player.RunState.CreateCard<VincentContract>(player),
                ContractKind.Sphinx => player.RunState.CreateCard<SphinxContract>(player),
                _ => player.RunState.CreateCard<BeatriceContract>(player),
            };

            if (card != null)
            {
                var added = await CardPileCmd.Add(card, PileType.Deck, CardPilePosition.Top, null, true);
                GD.Print($"[Subaru] neow contract: {kind} granted, success={added.success}");
                CardCmd.PreviewCardPileAdd(added, 0.6f, CardPreviewStyle.EventLayout);
            }

            // 阴魔法阵营专属遗物：贝蒂的守护。走先古事件的正常发放入口（RelicCmd.Obtain），
            // 不再用 AddRelicInternal —— 那条路不播「新获得」动画，遗物栏节点会在
            // 「先设 Model、后进树」的时机建出来，图标纹理加载被 IsNodeReady 挡掉（格子全空）。
            if (kind == ContractKind.Beatrice)
                await GrantBeatriceGuard(player);

            // 大灾阵营专属遗物：进化（每场战斗开始获得 最大生命÷10 的格挡）。
            // 和贝蒂的守护走同一个发放入口（RelicCmd.Obtain），理由见上面那段注释。
            if (kind == ContractKind.Sphinx)
                await GrantEvolution(player);

            // 帝国军阵营专属遗物：帝王匕首（每回合开始 +2 活力）。
            if (kind == ContractKind.Vincent)
                await GrantEmperorsDagger(player);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract grant failed: " + e);
        }
        finally
        {
            Restore(neow);
        }
    }

    /// <summary>
    /// 阴魔法阵营的专属遗物「贝蒂的守护」：走契约·贝蒂时发一个。
    /// 已经有（重进事件 / 读档）就不重复发。
    ///
    /// <para><b>2026-11-19</b>：改用 <c>RelicCmd.Obtain</c>（先古事件发遗物的正常入口），
    /// 不再直接 <c>AddRelicInternal</c> —— 后者绕过 <c>GlobalUi.RelicInventory.AnimateRelic</c>，
    /// 遗物栏节点会在「先设 Model、后进树」的时机建出来，纹理加载被 IsNodeReady 挡掉，格子是空的。
    /// 走 Obtain 之后由引擎自己播「新获得」动画并补齐图标。</para>
    /// </summary>
    private static async Task GrantBeatriceGuard(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            if (player.Relics.Any(r => r is Relics.BeatriceGuard))
            {
                GD.Print("[Subaru] neow contract: beatrice guard already owned");
                return;
            }

            if (ModelDb.Relic<Relics.BeatriceGuard>() is not RelicModel canonical)
            {
                GD.PushWarning("[Subaru] neow contract: beatrice guard model missing");
                return;
            }

            var relic = canonical.ToMutable();
            await RelicCmd.Obtain(relic, player, -1);

            GD.Print($"[Subaru] neow contract: beatrice guard granted via Obtain "
                     + $"(path={relic.PackedIconPath}, textureNull={relic.Icon == null})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract: grant beatrice guard failed: " + e);
        }
    }

    /// <summary>
    /// 大灾阵营的专属遗物「进化」：签「契约·斯芬克斯」时发一个。
    /// 已经有（重进事件 / 读档）就不重复发。
    /// </summary>
    private static async Task GrantEvolution(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            if (player.Relics.Any(r => r is Relics.Evolution))
            {
                GD.Print("[Subaru] neow contract: evolution already owned");
                return;
            }

            if (ModelDb.Relic<Relics.Evolution>() is not RelicModel canonical)
            {
                GD.PushWarning("[Subaru] neow contract: evolution model missing");
                return;
            }

            var relic = canonical.ToMutable();
            await RelicCmd.Obtain(relic, player, -1);

            GD.Print($"[Subaru] neow contract: evolution granted "
                     + $"(path={relic.PackedIconPath}, textureNull={relic.Icon == null})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract: grant evolution failed: " + e);
        }
    }

    /// <summary>
    /// 帝国军阵营的专属遗物「帝王匕首」：签「契约·文森特」时发一个。
    /// 已经有（重进事件 / 读档）就不重复发。
    /// </summary>
    private static async Task GrantEmperorsDagger(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            if (player.Relics.Any(r => r is Relics.EmperorsDagger))
            {
                GD.Print("[Subaru] neow contract: emperors dagger already owned");
                return;
            }

            if (ModelDb.Relic<Relics.EmperorsDagger>() is not RelicModel canonical)
            {
                GD.PushWarning("[Subaru] neow contract: emperors dagger model missing");
                return;
            }

            var relic = canonical.ToMutable();
            await RelicCmd.Obtain(relic, player, -1);

            GD.Print($"[Subaru] neow contract: emperors dagger granted "
                     + $"(path={relic.PackedIconPath}, textureNull={relic.Icon == null})");
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract: grant emperors dagger failed: " + e);
        }
    }

    /// <summary>把涅奥原本的祝福页放回去。</summary>
    private static void Restore(Neow neow)
    {
        try
        {
            if (Saved.TryGetValue(neow, out var snapshot))
                SetState(neow, snapshot.Description, snapshot.Options);
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] neow contract restore failed: " + e);
        }
    }

    private static void SetState(EventModel model, LocString description, IEnumerable<EventOption> options)
    {
        if (SetEventStateMethod == null)
        {
            GD.PushError("[Subaru] neow contract: SetEventState not found");
            return;
        }

        SetEventStateMethod.Invoke(model, [description, options]);
    }

    private static LocString PageText() => new("events", "SUBARU-NEOW_CONTRACT.description");
}
