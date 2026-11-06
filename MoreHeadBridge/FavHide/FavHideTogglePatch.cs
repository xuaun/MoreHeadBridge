// Intercepts ToggleCosmetic: click → vanilla equip; modifier+click or gamepad shortcut → FAVORITE / HIDE / CUSTOMIZER (keys and buttons from the config). Locked buttons always fall through. Only the clicked button's badge updates (no RefreshScrollContent, no flicker); FAV/HIDE tabs reflect it on next open.

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

    private static int _lastCustomizerFrame = int.MinValue;

    // Action forced for one ToggleCosmetic call by a gamepad shortcut, which has no key to hold.
    private static CosmeticClickAction _shortcut = CosmeticClickAction.Equip;

    internal static void RunShortcut(MenuElementCosmeticButton button, CosmeticClickAction action)
    {
        _shortcut = action;
        try { button.ToggleCosmetic(); }
        finally { _shortcut = CosmeticClickAction.Equip; }
    }

    [HarmonyPrefix]
    private static bool Prefix(MenuElementCosmeticButton __instance)
    {
        // Cloned cell inside the variant picker: run the cell's own action and skip vanilla (the popup's page index would make native ToggleCosmetic a no-op anyway).
        var cell = __instance.GetComponent<VariantCell>();
        if (cell != null) { cell.OnClick?.Invoke(); return false; }

        var (favMod, hideMod, custMod) = MenuClickModifiers.Effective();
        bool fav     = _shortcut == CosmeticClickAction.Favorite || MenuClickModifiers.Held(favMod);
        bool hide    = _shortcut == CosmeticClickAction.Hide     || MenuClickModifiers.Held(hideMod);
        bool custNow = MenuClickModifiers.Held(custMod);

        if (custNow) _lastCustomizerFrame = Time.frameCount;
        bool cust = _shortcut == CosmeticClickAction.Customize || custNow
                    || (_lastCustomizerFrame >= 0 && Time.frameCount - _lastCustomizerFrame <= 3);

        // Plain click on a group representative opens the variant picker; modifier clicks fall through and act on the representative normally.
        if (!fav && !hide && !cust)
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
        if (!fav && !hide && !cust) return true;

        // Favorite/hide interactions require the extended menu (FAV/HIDE tabs must exist).
        if ((fav || hide) && !Plugin.EnableMenuEnhancements.Value) return true;

        // Locked cosmetics: let vanilla play its locked feedback.
        if (__instance.menuButton != null && __instance.menuButton.disabled) return true;

        var asset = __instance.cosmeticAsset;
        // Clear buttons (no asset) cannot be favorited or hidden.
        if (asset == null) return true;

        // Customizer gesture → open per-cosmetic override popup (bridge cosmetics only).
        if (cust && !fav && !hide)
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
            return true; // customizer gesture on a non-customizable cosmetic → let vanilla handle
        }

        BridgeFavoritesManager.EnsureLoaded();

        if (fav)
            BridgeFavoritesManager.ToggleFavorite(asset);
        else
            BridgeFavoritesManager.ToggleHidden(asset);

        // ── Sound ─────────────────────────────────────────────────────────────
        // Use MenuManager.instance.soundPosition to match vanilla's own click sound.
        SemiFunc.GamepadRumbleThockBigButton();
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
