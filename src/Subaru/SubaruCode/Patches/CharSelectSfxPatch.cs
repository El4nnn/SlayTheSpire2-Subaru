using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using Subaru.SubaruCode.Character;

namespace Subaru.SubaruCode.Patches;

// 2026-11-22 用户定：选人界面选中昴不再播「死归」那声（rewind.ogg），
// 改由背景场景 CharSelectBgRoot 播选人曲 char_select.ogg。
// 原来那个 CharSelectSfxPatch（SelectCharacter postfix 播 RewindSfx）已删除。

/// <summary>
/// 点角色时重播右侧开场动画（2026-11-22 用户定：点击角色时播放，播完停住）。
///
/// 背景场景本身是「选中昴」时创建的，_Ready 里已经会播一次；
/// 这里负责「已经选中过、再点一次也重播」的情况。
/// </summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
public static class CharSelectIntroPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCharacterSelectButton charSelectButton)
    {
        try
        {
            var character = charSelectButton == null ? null : charSelectButton.Character;
            if (character is not Subaru.SubaruCode.Character.Subaru)
                return;

            var tree = charSelectButton.GetTree();
            var bg = tree?.Root?.FindChild("SubaruCharSelectBg", true, false) as CharSelectBgRoot;
            bg?.ReplayIntro();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] char select intro hook failed: " + e.Message);
        }
    }
}

/// <summary>
/// 掐掉原版给菜月昴播的选中音效（FMOD 事件 <c>event:/sfx/characters/subaru/...</c>）。
/// 只拦这一个前缀，其它角色的选中音不受影响。
///
/// 保留的原因：新背景已经在放选人曲，原版那声「点击音」叠上来会脏。
/// </summary>
[HarmonyPatch(typeof(SfxCmd), "Play", [typeof(string), typeof(float)])]
public static class SuppressVanillaCharSelectSfxPatch
{
    [HarmonyPrefix]
    public static bool Prefix(string __0)
    {
        try
        {
            return __0 == null
                || !__0.Contains("/characters/subaru", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
}
