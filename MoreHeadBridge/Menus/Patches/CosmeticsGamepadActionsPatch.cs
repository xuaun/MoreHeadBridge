// Postfix on MenuPageCosmetics.Update: gamepad buttons for favorite / hide / customize on the selected cosmetic. Runs through ToggleCosmetic so both input paths share FavHideTogglePatch.

using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "Update")]
internal static class CosmeticsGamepadActionsPatch
{
    private const float ShortcutCooldown = 0.25f;   // same as MenuButton's click cooldown: one touch never toggles twice

    private static float _nextShortcutTime;

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (SemiFunc.InputMode() != InputModes.Gamepad || BatchIconGenerator.IsGenerating) return;

        var pad = Gamepad.current;
        if (pad == null || !pad.enabled) return;

        var page = __instance.menuPage;
        if (MenuManager.instance.currentMenuPage != page || !page.isInteractable) return;
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics || !SemiFunc.NoTextInputsActive()) return;

        var button = __instance.hoveredCosmeticButton;
        if (button == null || button.cosmeticAsset == null) return;
        if (button.menuButton != null && button.menuButton.disabled) return;

        var (fav, hide, cust) = MenuGamepadButtons.Effective();
        var action = MenuGamepadButtons.Pressed(pad, cust) ? CosmeticClickAction.Customize
            : MenuGamepadButtons.Pressed(pad, fav) ? CosmeticClickAction.Favorite
            : MenuGamepadButtons.Pressed(pad, hide) ? CosmeticClickAction.Hide
            : CosmeticClickAction.Equip;
        if (action == CosmeticClickAction.Equip || Time.unscaledTime < _nextShortcutTime) return;
        _nextShortcutTime = Time.unscaledTime + ShortcutCooldown;

        // Customize only where a popup can open; FavHideTogglePatch would fall through to a plain equip otherwise.
        bool allowed = action == CosmeticClickAction.Customize
            ? Plugin.EnableCosmeticCustomizer.Value && Plugin.MenuLibAvailable && BridgeIds.IsCustomizable(button.cosmeticAsset)
            : Plugin.EnableMenuEnhancements.Value;
        if (allowed) FavHideTogglePatch.RunShortcut(button, action);
    }
}
