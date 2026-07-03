// Intercepts ToggleCosmetic: click → vanilla equip; Ctrl+click → FAVORITE; Alt+click → HIDE. Locked buttons always fall through. Only the clicked button's badge updates (no RefreshScrollContent, no flicker); FAV/HIDE tabs reflect it on next open.

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
        // Cloned cell inside the variant picker: run the cell's own action and skip vanilla (the popup's page index would make native ToggleCosmetic a no-op anyway).
        var cell = __instance.GetComponent<VariantCell>();
        if (cell != null) { cell.OnClick?.Invoke(); return false; }

        bool ctrl     = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt      = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
        bool shiftNow = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift);

        if (shiftNow) _lastShiftFrame = Time.frameCount;
        bool shift = shiftNow || (_lastShiftFrame >= 0 && Time.frameCount - _lastShiftFrame <= 3);

        // Plain click on a group representative opens the variant picker; modifier clicks fall through and act on the representative normally.
        if (!ctrl && !alt && !shift)
        {
            var group = __instance.GetComponent<CosmeticGroupButton>();
            if (group != null && group.IsActive && Plugin.MenuLibAvailable)
            {
                // Open after mouse-release: REPO buttons fire on mouse-DOWN, so a button under the cursor in the fresh popup would otherwise be triggered immediately.
                PopupUI.AfterMouseRelease(Plugin.Instance, () => CosmeticVariantPopup.Show(__instance, group));
                return false;
            }
        }

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
            if (Plugin.EnableCosmeticCustomizer.Value
                && Plugin.MenuLibAvailable
                && BridgeIds.IsCustomizable(asset))
            {
                // Open after mouse-release: fires on mouse-DOWN and the popup's first slider would be scrubbed by the held cursor (same fix as the offset edit popup). Equip first so it shows while customizing — but only equip, never unequip if already equipped.
                bool alreadyEquipped = __instance.IsEquipped();
                PopupUI.AfterMouseRelease(Plugin.Instance, () => CosmeticOverridePopup.Show(asset));
                return !alreadyEquipped; // not equipped → let vanilla ToggleCosmetic equip it
            }
            return true; // shift on a non-customizable cosmetic → let vanilla handle
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
            BridgeLog.Trace($"FavHideTogglePatch: sound skipped — {ex.Message}");
        }

        // ── Animation ─────────────────────────────────────────────────────────
        // TriggerClickAnimations() is private → reflection; a null lookup is cached so AccessTools isn't re-probed on every click.
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
            BridgeLog.Trace($"FavHideTogglePatch: animation skipped — {ex.Message}");
        }

        // ── Marker update ──────────────────────────────────────────────────────
        FavHideMarkerHelper.UpdateMarker(__instance);

        return false; // skip ToggleCosmetic — no equip/unequip
    }
}
