using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Per-cosmetic tintable button: track which asset is being colored (page opens normally; we intercept CosmeticColorSet below).
// Also fixes a crash: the World section's synthetic subCategory (999) flows into colorKey, so MenuButtonColor.LateStart() throws IndexOutOfRange on colorsEquipped[999] — clamp to the asset's own type (or 0).
[HarmonyPatch(typeof(MenuElementCosmeticButton), "ChangeColorButton")]
internal static class PerCosmeticColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticButton __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        PerCosmeticColors.PendingAsset = __instance.cosmeticAsset;
        if (__instance.cosmeticAsset != null)
            PerCosmeticColors.TemporarilyShowForColorPage(__instance.cosmeticAsset);
    }

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticButton __instance)
    {
        if (MetaManager.instance?.colorsEquipped == null) return;

        // ChangeColorButton sets colorKey = (int)subCategory; a synthetic value (999) is out of colorsEquipped's bounds and crashes every MenuButtonColor.
        var colorPage = Object.FindObjectOfType<MenuPageColor>();
        if (colorPage == null) return;

        int len = MetaManager.instance.colorsEquipped.Length;
        if (colorPage.colorKey >= 0 && colorPage.colorKey < len) return; // already valid

        // Fall back to the cosmetic's own vanilla type index, or 0 (Hat) if that is also out of range.
        int fallback = __instance.cosmeticAsset != null
            ? (int)__instance.cosmeticAsset.type
            : 0;
        if (fallback < 0 || fallback >= len) fallback = 0;
        colorPage.colorKey = fallback;
    }
}

// Section color button: clear PendingAsset so CosmeticColorSet treats this as a type-wide paint (not per-cosmetic); its postfix wipes overrides for that type.
[HarmonyPatch(typeof(MenuElementCosmeticSection), "ChangeColorButton")]
internal static class SectionColorButtonPatch
{
    // True while a colour page opened from the synthetic World section (subCategory = 999) is active; cleared on close (ColorPageClosePatch).
    internal static bool PendingWorldSection;

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticSection __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        PerCosmeticColors.PendingAsset = null;
        PendingWorldSection = __instance.subCategory == CosmeticsFilterPatch.WorldSubCategory;
    }

    // World section (999): clamp colorKey to 0 (Hat). Vanilla CosmeticColorSet(0, id) is then blocked by the Prefix below; the Postfix paints world bridge cosmetics instead.
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!PendingWorldSection) return;
        if (MetaManager.instance?.colorsEquipped == null) return;
        var colorPage = Object.FindObjectOfType<MenuPageColor>();
        if (colorPage == null) return;
        int len = MetaManager.instance.colorsEquipped.Length;
        if (colorPage.colorKey >= 0 && colorPage.colorKey < len) return; // already valid
        colorPage.colorKey = 0; // Hat (0) — safe, prevents MenuButtonColor crash
    }
}

// Paint All / Body / Cosmetics open directly on MenuPageCosmetics — SectionColorButtonPatch.Prefix never fires, so a stale PendingAsset would make CosmeticColorSetPatch block vanilla for every chip click. Clear PendingAsset and PendingWorldSection before each opens.
[HarmonyPatch(typeof(MenuPageCosmetics), "ChangeAllColorButton")]
internal static class ChangeAllColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PerCosmeticColors.PendingAsset = null;
        SectionColorButtonPatch.PendingWorldSection = false;
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "ChangeBodyColorButton")]
internal static class ChangeBodyColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PerCosmeticColors.PendingAsset = null;
        SectionColorButtonPatch.PendingWorldSection = false;
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "ChangeCosmeticsColorButton")]
internal static class ChangeCosmeticsColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PerCosmeticColors.PendingAsset = null;
        SectionColorButtonPatch.PendingWorldSection = false;
    }
}

// Cosmetics menu opened → open the browse gate: colour AND mini-outfit changes apply locally but only
// reach remotes at the menu confirm (vanilla parity — nothing syncs while the menu is open).
[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class CosmeticsMenuOpenGatePatch
{
    [HarmonyPostfix]
    private static void Postfix() => PerCosmeticColorNetworkSync.OpenGate();
}

[HarmonyPatch(typeof(MenuPageColor), "OnDestroy")]
internal static class ColorPageClosePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PerCosmeticColors.RestoreTypeColor();
        PerCosmeticColors.ActiveSlot = -1; // reset slot selector to ALL
        SectionColorButtonPatch.PendingWorldSection = false; // clear world-section flag
    }

    [HarmonyPostfix]
    private static void Postfix()
        => PerCosmeticColors.PendingAsset = null;
}

// Safety net: cosmetics menu closed without the synced confirm firing — still close the gate and flush.
[HarmonyPatch(typeof(MenuPageCosmetics), "OnDestroy")]
internal static class CosmeticsMenuCloseFlushPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (PerCosmeticColorNetworkSync.CloseGate() && SemiFunc.IsMultiplayer())
            BridgeNetMux.BroadcastSnapshot();
    }
}

// Intercepts CosmeticColorSet (every colour click). PendingAsset set → save per-cosmetic colour and skip vanilla (SetupColorsLogic postfix re-applies). PendingAsset null → vanilla runs (type colour) and the Postfix clears per-cosmetic overrides for that type so the type colour wins.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticColorSet))]
internal static class CosmeticColorSetPatch
{
    // Section-level paint (PendingAsset == null, colour picker open): bridge cosmetics get a per-cosmetic override (slot overrides cleared so the whole-asset colour applies uniformly); vanilla per-cosmetic overrides are cleared so the type colour wins — MUST also run for pure-vanilla setups, where sibling pinning leaves auto-pinned overrides that would otherwise beat section paint. Guarded by MenuPageColor presence to exclude Randomize and Reset.
    [HarmonyPostfix]
    private static void Postfix(int _index, int _colorID)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (PerCosmeticColors.PendingAsset != null) return; // per-cosmetic handled in Prefix

        // Skip Randomize and Reset — no colour picker is open during those flows.
        var colorPage = Object.FindObjectOfType<MenuPageColor>(true);
        if (colorPage == null) return;

        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;

        bool any = false;
        bool anySlots = false;
        bool anyCustom = false;
        bool anyAnim = false;

        // ── World section paint (bridge-only; vanilla was blocked by the Prefix) ─────────────
        // PendingWorldSection = colour page opened from the synthetic World section (999, colorKey clamped to 0). Only world bridge cosmetics are painted; vanilla was blocked.
        if (SectionColorButtonPatch.PendingWorldSection)
        {
            foreach (int idx in meta.cosmeticEquipped)
            {
                if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
                var asset = meta.cosmeticAssets[idx];
                if (!BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset)) continue;
                if (!HhhCosmeticLoader.IsWorldAsset(asset)) continue;
                PerCosmeticColors.SetNoSave(asset.assetId, _colorID);
                anySlots  |= PerCosmeticColors.RemoveSlotsNoSave(asset.assetId);
                anyCustom |= PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId);
                anyCustom |= PerCosmeticColors.RemoveCustomSlotsNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveAnimationNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveSlotAnimationsNoSave(asset.assetId);
                any = true;
            }
            if (!any) return;
            PerCosmeticColors.Save();
            if (anySlots)  PerCosmeticColors.SaveSlots();
            if (anyCustom) { PerCosmeticColors.SaveCustom(); PerCosmeticColors.SaveCustomSlots(); }
            if (anyAnim)
            {
                PerCosmeticColors.SaveAnimations();
                PerCosmeticColors.SaveSlotAnimations();
                ColorAnimatorRefresher.RefreshLocal();
            }
            return;
        }

        // The bridge-paint loop only runs when an eligible bridge cosmetic exists; the vanilla-override-clearing loop below always runs (see comment above).
        bool hasBridgeInSection = OriginalColorButtonPatch.HasEligibleBridgeForSection(colorPage);

        // ── Bridge cosmetics ──────────────────────────────────────────────────
        // Worlds are included in All/Cosmetics multi-type paint (colorKey < 0, non-Body), excluded from specific-subcategory (colorKey >= 0) and Body paint.
        bool isMultiMode = colorPage.colorKey < 0;
        bool isBodyMode  = colorPage.pageMode == MenuPageColor.ColorPageType.Body;
        if (hasBridgeInSection)
        {
            foreach (int idx in meta.cosmeticEquipped)
            {
                if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
                var asset = meta.cosmeticAssets[idx];
                if (!BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset)) continue;
                bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
                // Include worlds in All/Cosmetics modes; exclude from Body and specific subcategories.
                if (assetIsWorld && (!isMultiMode || isBodyMode)) continue;
                if ((int)asset.type != _index) continue;
                PerCosmeticColors.SetNoSave(asset.assetId, _colorID);
                anySlots  |= PerCosmeticColors.RemoveSlotsNoSave(asset.assetId);
                // A flat palette paint must also wipe any custom RGB or animation, or those keep winning over the new colour.
                anyCustom |= PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId);
                anyCustom |= PerCosmeticColors.RemoveCustomSlotsNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveAnimationNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveSlotAnimationsNoSave(asset.assetId);
                any = true;
            }
        }

        // ── Vanilla cosmetics: clear per-cosmetic overrides so type colour wins ─
        // A section palette paint must clear BOTH the palette-index override AND any custom RGB, or ApplyOverrides re-applies the custom colour on top.
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || BridgeIds.IsBridgeAsset(asset)) continue;
            if ((int)asset.type != _index) continue;
            bool hadPalette = PerCosmeticColors.RemoveColorNoSave(asset.assetId);
            bool hadCustom  = PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId);
            bool hadCSlots  = PerCosmeticColors.RemoveCustomSlotsNoSave(asset.assetId);
            if (hadPalette || hadCustom || hadCSlots)
            {
                any       = true;
                anyCustom |= hadCustom || hadCSlots;
            }
        }

        // Clear base mesh custom colour for this type — the "Default" mesh has no CosmeticAsset, so it's never in cosmeticEquipped and must be handled separately.
        if (Plugin.EnableVanillaCustomColors.Value)
        {
            string baseId = VanillaTintHelper.BaseMeshAssetId(_index);
            if (PerCosmeticColors.RemoveCustomColorNoSave(baseId)) { any = true; anyCustom = true; }
        }

        if (!any) return;
        PerCosmeticColors.Save();
        if (anySlots)  PerCosmeticColors.SaveSlots();
        if (anyCustom) { PerCosmeticColors.SaveCustom(); PerCosmeticColors.SaveCustomSlots(); }
        if (anyAnim)
        {
            PerCosmeticColors.SaveAnimations();
            PerCosmeticColors.SaveSlotAnimations();
            ColorAnimatorRefresher.RefreshLocal();   // strip the now-removed animators immediately
        }
        // Visual update is handled by the vanilla click handler's CosmeticPlayerUpdateLocal right after CosmeticColorSet (→ SetupColorsLogic → ApplyOverrides).
    }

    [HarmonyPrefix]
    private static bool Prefix(int _index, int _colorID)
    {
        if (!PerCosmeticColors.FeatureEnabled) return true;
        // World section is bridge-only: no vanilla cosmetics painted (the Postfix paints world bridge cosmetics instead).
        if (SectionColorButtonPatch.PendingWorldSection) return false;
        if (PerCosmeticColors.PendingAsset == null) return true; // section paint → let vanilla run

        var asset = PerCosmeticColors.PendingAsset;

        // Bridge cosmetics and modded cosmetics with a per-part slot layout (e.g. YoshiCarry) both carry BridgeTintMaterials with slot ids, so both take the per-slot / whole-asset BTM paint path.
        bool slotCapable = BridgeIds.IsBridgeAsset(asset) || ModdedSlotLayout.Handles(asset);

        // An explicit colour stops the active animation — static colour and animation are mutually exclusive (the animator would overwrite the pick). Slot mode stops only the active slot's; ALL stops all.
        ColorAnimatorRefresher.StopAnimation(asset.assetId, PerCosmeticColors.ActiveSlot);

        // ── Slot-specific paint: slot-capable cosmetic + specific slot selected ─────
        // Store only the per-slot colour; leave _colors (whole-asset) unchanged.
        if (PerCosmeticColors.ActiveSlot >= 0 && slotCapable)
        {
            PerCosmeticColors.SetSlotColor(asset.assetId, PerCosmeticColors.ActiveSlot, _colorID);
            BridgeTintHelper.ApplySlotColorToLiveInstances(
                asset, PerCosmeticColors.ActiveSlot, _colorID);

            // Keep colorsEquipped[type] in sync for the picker indicator — bridge only.
            // NOT for World cosmetics (they share the Hat type and would tint the player's real hats), and NOT for modded slot cosmetics: their un-painted slots fall through to colorsEquipped[type] in ApplyTypeColors, so overwriting it with this slot's colour would paint every OTHER slot the same colour (then only ApplyOverrides repaints the one slot). Leaving the real type colour lets the other slots keep the default.
            if (BridgeIds.IsBridgeAsset(asset)
                && !HhhCosmeticLoader.IsWorldAsset(asset)
                && MetaManager.instance != null
                && _index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
                MetaManager.instance.colorsEquipped[_index] = _colorID;
            // Re-bind animators so a still-running whole-asset animation leaves this now-static slot fixed.
            ColorAnimatorRefresher.RefreshLocal();
            BridgeSlotSelectorRow.Active?.Refresh();
            return false;
        }

        // ── Whole-asset paint (ALL slot, or cosmetic with no slot layout) ──────────────
        // Painting ALL for a slot-capable cosmetic discards per-slot overrides so the new whole-asset colour applies uniformly across every renderer.
        if (slotCapable)
            PerCosmeticColors.ClearSlotsForAsset(asset.assetId);

        // Pin the current type colour onto unpinned same-type siblings so they keep it after this cosmetic gets its own. Skipped for World cosmetics (internally Hat — must never touch real hats).
        if (!HhhCosmeticLoader.IsWorldAsset(asset)
            && MetaManager.instance != null
            && _index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
        {
            int realTypeColor = PerCosmeticColors.GetRealTypeColor(
                _index, MetaManager.instance.colorsEquipped);
            if (realTypeColor >= 0)
            {
                foreach (int idx in MetaManager.instance.cosmeticEquipped)
                {
                    if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;
                    var other = MetaManager.instance.cosmeticAssets[idx];
                    if (other == null || other.type != asset.type) continue;
                    if (other.assetId == asset.assetId) continue;
                    if (BridgeIds.IsBridgeAsset(other)) continue; // bridge: opt-in tinting only
                    if (PerCosmeticColors.HasOverride(other.assetId)) continue; // already pinned
                    PerCosmeticColors.SetNoSave(other.assetId, realTypeColor);
                }
            }
        }

        // Save the target asset's new whole-asset colour (also saves siblings pinned above).
        PerCosmeticColors.Set(asset.assetId, _colorID);

        // A per-cosmetic paint returns false (vanilla never runs), so no SetupColorsLogic repaints the BTMs — apply to live instances now. This is also what actually colours World cosmetics (BTM-only, never in the PlayerMaterial pipeline).
        if (slotCapable)
            BridgeTintHelper.ApplyWholeAssetColorToLiveInstances(asset, _colorID);

        // Keep colorsEquipped[type] in sync (picker indicator). NOT for worlds: they share the Hat type, so writing colorsEquipped[Hat] would tint the player's real hats.
        if (!HhhCosmeticLoader.IsWorldAsset(asset)
            && MetaManager.instance != null
            && _index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
            MetaManager.instance.colorsEquipped[_index] = _colorID;
        BridgeSlotSelectorRow.Active?.Refresh();
        return false;
    }

}

// Vanilla UpdateColorButton only checks PlayerMaterials, so a bridge-only subcategory gets disabled — enable when at least one eligible bridge cosmetic is in the section (regular subcategories + World 999).
[HarmonyPatch(typeof(MenuElementCosmeticSection), "UpdateColorButton")]
internal static class SectionColorButtonBridgePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticSection __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (!__instance.colorButton.disabled) return;  // vanilla already enabled it

        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;

        bool isWorldSection = __instance.subCategory == CosmeticsFilterPatch.WorldSubCategory;

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (!BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset)) continue;

            bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
            if (isWorldSection)
            {
                // World section: only world cosmetics qualify.
                if (!assetIsWorld) continue;
            }
            else
            {
                // Regular subcategory: world cosmetics are excluded (they live in the World section).
                if (assetIsWorld) continue;
                if (asset.type != __instance.subCategory) continue;
            }

            // Found an eligible bridge cosmetic → enable the button and icon.
            __instance.colorButton.disabled = false;
            __instance.colorButtonIcon.color = UnityEngine.Color.white;
            if (__instance.menuPageCosmetics.selectedSubCategory == __instance.subCategory)
            {
                __instance.menuPageCosmetics.stickyHeader.colorButton.disabled = false;
                __instance.menuPageCosmetics.stickyHeader.colorButtonIcon.color = UnityEngine.Color.white;
            }
            return;
        }
    }
}
