using System;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Timeline.Scaffolding;

namespace Subaru.SubaruCode.Timeline;

/// <summary>Subaru 的剧情线。</summary>
public sealed class SubaruStory : ModStoryTemplate
{
    protected override string StoryKey => "SUBARU";
}

/// <summary>
/// 角色解锁节点：用「愚者」完成一局游戏 → 解锁「菜月昴」。
/// CharacterUnlockEpochTemplate&lt;T&gt; 的 T 是要解锁的角色模型类型，
/// 它的 QueueUnlocks 会自动把这个角色加进可选中。
/// </summary>
public sealed class SubaruCallEpoch : CharacterUnlockEpochTemplate<Character.Subaru>
{
    public override string Id => "SUBARU_EPOCH_CALL";

    /// <summary>这一条解锁后，二级节点（死亡回归 / 魔女时代的终结 / 暴食）才出现在时间线上。</summary>
    public override EpochModel[] GetTimelineExpansion() =>
        SubaruEpochChain.Expand(
            SubaruEpochGate.RewindEpochId,
            SubaruEpochGate.WitchEndEpochId,
            SubaruEpochGate.GluttonyEpochId);
}

public static class SubaruTimeline
{
    /// <summary>「帝国的剑狼」需要累计击败的首领数（以菜月昴身份）。</summary>
    public const int SwordWolfBossWins = 10;

    public static void Register()
    {
        try
        {
            const EpochEra callEra = EpochEra.Flourish0;
            const EpochEra rewindEra = EpochEra.Flourish2;
            const EpochEra witchEra = EpochEra.Blight0;
            const EpochEra loveEra = EpochEra.Invitation3;
            const EpochEra swordWolfEra = EpochEra.Invitation6;
            const EpochEra collapseEra = EpochEra.Seeds2;
            const EpochEra gluttonyEra = EpochEra.Flourish3;
            const EpochEra calamityEra = EpochEra.Invitation7;

            // 只用列（EpochEra），列内序位交给 RitsuLib 的 AutoTimelineSlot：
            // 它在 content pack flush 时按 mod 顺序占「该列第一个空位」。
            // 以前这里自己扫 EpochModel.AllEpochIds 抢坐标，但那次扫描发生在 mod 初始化早期，
            // 别的 mod 的时间线节点还没落位 —— 于是和它们撞格（0.2.80 与酒狐一起启动时
            // SubaruCollapseEpoch 撞 Seeds2/2，ModelDb.Init 校验失败，游戏启动直接终止）。
            ModContentPackBuilder.For("Subaru")
                .Story<SubaruStory>()
                .Epoch<SubaruCallEpoch>()
                .ModEpochAutoTimelineSlot<SubaruCallEpoch>(callEra)
#if GAME_BETA
                .UnlockEpochAfterRunAs<Fool, SubaruCallEpoch>()
#endif
                .Epoch<SubaruRewindEpoch>()
                .ModEpochAutoTimelineSlot<SubaruRewindEpoch>(rewindEra)
                .Epoch<SubaruWitchEndEpoch>()
                .ModEpochAutoTimelineSlot<SubaruWitchEndEpoch>(witchEra)
                .UnlockEpochAfterWinAs<Character.Subaru, SubaruWitchEndEpoch>()
                .Epoch<SubaruLoveEpoch>()
                .ModEpochAutoTimelineSlot<SubaruLoveEpoch>(loveEra)
                .Epoch<SubaruSwordWolfEpoch>()
                .ModEpochAutoTimelineSlot<SubaruSwordWolfEpoch>(swordWolfEra)
                // 「帝国的剑狼」：累计以菜月昴击败 10 个首领后揭示
                .UnlockEpochAfterBossVictories<Character.Subaru, SubaruSwordWolfEpoch>(SwordWolfBossWins)
                .Epoch<SubaruCollapseEpoch>()
                .ModEpochAutoTimelineSlot<SubaruCollapseEpoch>(collapseEra)
                .Epoch<SubaruGluttonyEpoch>()
                .ModEpochAutoTimelineSlot<SubaruGluttonyEpoch>(gluttonyEra)
                .Epoch<SubaruCalamityEpoch>()
                .ModEpochAutoTimelineSlot<SubaruCalamityEpoch>(calamityEra)
                // 「大灾」：前置是「以菜月昴通关一次」（WitchEnd），
                // 还要求其中有一次是走帝国军（文森特契约）通关 —— 这条运行时条件
                // 由 CalamityEpochImperialWinPatch 在 RunManager.WinRun 里判定，
                // 所以这里不再挂 UnlockEpochAfterWinAs（那会在任意一次通关就发放）。
                .Apply();

            MainFile.Logger.Info(
                $"[Subaru] timeline registered via RitsuLib (auto slots: "
                + $"call={callEra}, rewind={rewindEra}, witchEnd={witchEra}, love={loveEra}, "
                + $"swordWolf={swordWolfEra}, collapse={collapseEra}, gluttony={gluttonyEra}, "
                + $"calamity={calamityEra})");
        }
        catch (Exception e)
        {
            MainFile.Logger.Info("[Subaru] timeline register failed: " + e);
        }
    }
}
