// ============================================================================
// Intercepts MenuElementCosmeticButton.ToggleCosmetic.
//
// Normal click → vanilla equip/unequip logic (unchanged).
// Ctrl + click → toggle FAVORITE  (no equip; plays sound + animation).
// Alt  + click → toggle HIDE       (no equip; plays sound + animation).
//
// Locked cosmetics (menuButton.disabled == true) are always forwarded to
// vanilla so the locked feedback plays normally.
//
// Only the toggled button's marker badge is updated; RefreshScrollContent is
// NOT called so the page doesn't flicker. Items will appear/disappear from the
// FAV / HIDE tabs the next time those tabs are visited.
// ============================================================================

using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticButton), "ToggleCosmetic")]
[HarmonyPriority(Priority.Normal)]
internal static class FavHideTogglePatch
{
    private static MethodInfo? _triggerClickAnimations;
    private static bool        _triggerLookupDone;  // true once AccessTools has been called (even if null)

    private static int _lastShiftFrame = int.MinValue;

    [HarmonyPrefix]
    private static bool Prefix(MenuElementCosmeticButton __instance)
    {
        bool ctrl     = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt      = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
        bool shiftNow = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);

        if (shiftNow) _lastShiftFrame = Time.frameCount;
        bool shift = shiftNow || (_lastShiftFrame >= 0 && Time.frameCount - _lastShiftFrame <= 3);

        // Normal click — let vanilla handle it.
        if (!ctrl && !alt && !shift) return true;

        // Ctrl/alt interactions require the extended menu (FAV/HIDE tabs must exist).
        if ((ctrl || alt) && !Plugin.EnableMenuEnhancements.Value) return true;

        // Locked cosmetics: let vanilla play its locked feedback.
        if (__instance.menuButton != null && __instance.menuButton.disabled) return true;

        var asset = __instance.cosmeticAsset;
        // Clear buttons (no asset) cannot be favorited or hidden.
        if (asset == null) return true;

        // Shift+click → open per-cosmetic override popup (bridge cosmetics only).
        if (shift && !ctrl && !alt)
        {
            if (Plugin.EnableCosmeticOverrideUI.Value
                && Plugin.MenuLibAvailable
                && BridgeIds.IsBridgeAsset(asset))
            {
                CosmeticOverridePopup.Show(asset);
                return false; // skip equip
            }
            return true; // shift on vanilla cosmetic → let vanilla handle
        }

        BridgeFavoritesManager.EnsureLoaded();

        if (ctrl)
            BridgeFavoritesManager.ToggleFavorite(asset);
        else
            BridgeFavoritesManager.ToggleHidden(asset);

        // ── Sound ─────────────────────────────────────────────────────────────
        // Use MenuManager.instance.soundPosition to match vanilla's own click sound.
        try
        {
            __instance.soundClick.Play(MenuManager.instance.soundPosition);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"FavHideTogglePatch: sound skipped — {ex.Message}");
        }

        // ── Animation ─────────────────────────────────────────────────────────
        // TriggerClickAnimations() is private — call via reflection.
        // _triggerLookupDone caches a null result so AccessTools is not re-probed
        // on every subsequent click if the method is absent (B5).
        if (!_triggerLookupDone)
        {
            _triggerClickAnimations = AccessTools.Method(
                typeof(MenuElementCosmeticButton), "TriggerClickAnimations");
            _triggerLookupDone = true;
        }
        try
        {
            _triggerClickAnimations?.Invoke(__instance, null);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"FavHideTogglePatch: animation skipped — {ex.Message}");
        }

        // ── Marker update ──────────────────────────────────────────────────────
        // Update only this button's badge; no full page refresh.
        FavHideMarkerHelper.UpdateMarker(__instance);

        return false; // skip ToggleCosmetic — no equip/unequip
    }
}
