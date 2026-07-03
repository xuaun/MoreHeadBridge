// Postfix on MenuPageCosmetics.Update: while SEARCH is active, keeps the input field focused and suppresses leaking game inputs (A/D rotation, B mute).

using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "Update")]
internal static class CosmeticsMenuUpdatePatch
{
    // Inputs that remain active while the search field is focused.
    private static readonly List<InputKey> SearchAllowedKeys = new()
    {
        InputKey.Back,   // close menu
        InputKey.Menu,   // open/close menu
        InputKey.Scroll, // scroll the cosmetics list
        InputKey.Grab,   // click-drag to rotate the avatar preview
    };

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        // Close our Tools dropdown on an outside click — vanilla's own outside-mouse-down check doesn't include our popup.
        var toolsPopup = CosmeticsMenuStartPatch._toolsPopupRef;
        if (toolsPopup != null && toolsPopup.dropdownActive
            && Input.GetMouseButtonDown(0) && !ToolsPopupHovered(toolsPopup))
        {
            toolsPopup.SetState(_state: false);
        }

        // Stops avatar eye-tracking during batch generation — same 0.1 s look-at-disable pattern vanilla uses for the randomize popup.
        if (BatchIconGenerator.IsGenerating)
            __instance.menuPage?.playerAvatarMenu?.playerVisuals?.playerEyes
                ?.OverrideDisableMenuLookAt();

        if (!CosmeticsMenuState.SearchMode) return;
        if (CosmeticsMenuState.ActivePage != __instance) return;

        if (CosmeticsMenuState.SearchField != null)
        {
            if (!CosmeticsMenuState.SearchField.isFocused)
                CosmeticsMenuState.SearchField.ActivateInputField();

            // Renewing every frame keeps the block continuous.
            if (InputManager.instance != null)
                InputManager.instance.DisableControlsExcept(0.1f, SearchAllowedKeys);

            return;
        }

        // Fallback: manual character capture when no TMP_InputField is present.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CosmeticsMenuState.ClearSearch();
            __instance.RefreshScrollContent();
            return;
        }

        bool changed = false;
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (CosmeticsMenuState.SearchText.Length > 0)
                {
                    CosmeticsMenuState.SetSearchText(CosmeticsMenuState.SearchText[..^1]);
                    changed = true;
                }
            }
            else if (c == '\r' || c == '\n')
            {
                CosmeticsMenuState.SetSearchMode(false);
                break;
            }
            else if (c >= ' ')
            {
                CosmeticsMenuState.SetSearchText(CosmeticsMenuState.SearchText + c);
                changed = true;
            }
        }

        if (changed)
            __instance.RefreshScrollContent();
    }

    // True when the cursor is over the Tools toggle button or any of its dropdown buttons.
    private static bool ToolsPopupHovered(MenuElementCosmeticButtonPopup popup)
    {
        if (popup.toggleButton != null && popup.toggleButton.hovering) return true;
        foreach (var b in popup.dropdownButtons)
            if (b != null && b.hovering) return true;
        return false;
    }
}
