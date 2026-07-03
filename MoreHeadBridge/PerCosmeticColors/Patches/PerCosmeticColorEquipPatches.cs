using HarmonyLib;

namespace MoreHeadBridge;

// On equip, pin the current type colour onto unpinned same-type cosmetics so the new one doesn't "steal" the slot colour when SetupColorsLogic runs.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticEquip))]
internal static class CosmeticEquipColorPreservePatch
{
    [HarmonyPrefix]
    private static void Prefix(MetaManager __instance, CosmeticAsset _cosmeticAssetNew, bool _isPreview)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (_cosmeticAssetNew == null) return;

        int typeIdx = (int)_cosmeticAssetNew.type;
        var colors  = __instance.colorsEquipped;

        if (typeIdx < 0 || typeIdx >= colors.Length) return;
        int currentTypeColor = colors[typeIdx];
        if (currentTypeColor < 0) return; // not yet initialized

        if (_isPreview)
        {
            // Hover path: pin already-previewing same-type cosmetics without an override. Bridge cosmetics skipped — they show original colours, not the type slot.
            var previewEquipped = __instance.cosmeticEquippedPreview;
            foreach (int idx in previewEquipped)
            {
                if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
                var asset = __instance.cosmeticAssets[idx];
                if (asset == null || asset.type != _cosmeticAssetNew.type) continue;
                if (BridgeIds.IsBridgeAsset(asset)) continue;               // bridge: opt-in tinting only
                if (PerCosmeticColors.HasOverride(asset.assetId)) continue; // handled by ApplyOverrides
                if (PerCosmeticColors.HasPreviewOverride(asset.assetId)) continue; // already set
                PerCosmeticColors.SetPreview(asset.assetId, currentTypeColor);
            }
        }
        else
        {
            // Real equip path: persist the type colour for unpinned same-type cosmetics. Bridge skipped — their colour is opt-in, not type-driven.
            int realTypeColor = PerCosmeticColors.GetRealTypeColor(typeIdx, colors);
            if (realTypeColor < 0) return;

            var equipped = __instance.cosmeticEquipped;
            bool any = false;
            foreach (int idx in equipped)
            {
                if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
                var asset = __instance.cosmeticAssets[idx];
                if (asset == null || asset.type != _cosmeticAssetNew.type) continue;
                if (BridgeIds.IsBridgeAsset(asset)) continue;               // bridge: opt-in tinting only
                if (PerCosmeticColors.HasOverride(asset.assetId)) continue; // already pinned
                PerCosmeticColors.SetNoSave(asset.assetId, realTypeColor);
                any = true;
            }
            if (any) PerCosmeticColors.Save();
        }
    }
}

// On hover-preview end (and no paint in progress), discard the temporary preview pins so they don't bleed into the next hover.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPreviewSet))]
internal static class CosmeticPreviewSetClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool _state)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (!_state && PerCosmeticColors.PendingAsset == null)
        {
            bool wasPreviewing = PerCosmeticColors.PresetPreviewActive;
            PerCosmeticColors.ClearPreviewOverrides();
            PerCosmeticColors.NotifyPresetHoverStart(-1); // clear stale hint from last hovered preset
            // Leaving a preset preview: re-bind animators to the live store (else the cosmetic stays on the preview's last frame). Skipped mid-TogglePreset — PresetLoadColorsPatch.Postfix restores the store and re-binds itself.
            if (wasPreviewing && !PresetLoadColorsPatch.LoadInProgress)
                ColorAnimatorRefresher.RefreshLocal();
        }
    }
}

// Clear the per-cosmetic colour on unequip: re-equipping starts from the type colour, and the sync payload stops carrying unequipped cosmetics.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticUnequip))]
internal static class CosmeticUnequipColorClearPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticAsset _cosmeticAsset, bool _isPreview, bool __result)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (!__result) return;       // cosmetic wasn't actually unequipped (not in list)
        if (_isPreview) return;      // hover preview — CosmeticPreviewSetClearPatch handles this
        if (_cosmeticAsset == null) return;

        PerCosmeticColors.ClearForAsset(_cosmeticAsset.assetId); // clears both _colors and _slotColors
    }
}

// Reset All wipes every store (incl. "__base_N__" customs) — but the vanilla button already ran its visual update with the OLD stores, so re-apply afterwards or base meshes keep their custom until a second press.
[HarmonyPatch(typeof(MenuPageCosmetics), "ResetAllButton")]
internal static class ResetAllButtonClearPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        PerCosmeticColors.ClearAll();
        RuntimeConfigApplier.ReapplyLocalCosmeticColors();
    }
}

// Reset Body: base-mesh customs ("__base_N__") outrank the palette in ApplyBaseMeshColor, so clear them all (base meshes are exactly the meshSwitch types) and re-apply so the first press shows.
[HarmonyPatch(typeof(MenuPageCosmetics), "ResetBodyButton")]
internal static class ResetBodyButtonClearPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (PerCosmeticColors.ClearAllBaseMeshCustomColorsNoSave())
        {
            PerCosmeticColors.SaveCustom();
            RuntimeConfigApplier.ReapplyLocalCosmeticColors();
        }
    }
}
