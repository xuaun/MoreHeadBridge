using HarmonyLib;

namespace MoreHeadBridge;

// When equipping a new cosmetic, pin the current type color onto every already-equipped
// cosmetic of the same type that has no per-cosmetic override yet. This prevents the
// new cosmetic from "stealing" the slot's type color from the others when SetupColorsLogic
// runs after the equip.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticEquip))]
internal static class CosmeticEquipColorPreservePatch
{
    [HarmonyPrefix]
    private static void Prefix(MetaManager __instance, CosmeticAsset _cosmeticAssetNew, bool _isPreview)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (_cosmeticAssetNew == null) return;

        int typeIdx = (int)_cosmeticAssetNew.type;
        var colors  = __instance.colorsEquipped;

        if (typeIdx < 0 || typeIdx >= colors.Length) return;
        int currentTypeColor = colors[typeIdx];
        if (currentTypeColor < 0) return; // not yet initialized

        if (_isPreview)
        {
            // Hover path: save the current type color for each already-previewing
            // cosmetic of the same type that has no persistent per-cosmetic override.
            var previewEquipped = __instance.cosmeticEquippedPreview;
            foreach (int idx in previewEquipped)
            {
                if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
                var asset = __instance.cosmeticAssets[idx];
                if (asset == null || asset.type != _cosmeticAssetNew.type) continue;
                if (PerCosmeticColors.HasOverride(asset.assetId)) continue; // handled by ApplyOverrides
                if (PerCosmeticColors.HasPreviewOverride(asset.assetId)) continue; // already set
                PerCosmeticColors.SetPreview(asset.assetId, currentTypeColor);
            }
        }
        else
        {
            // Real equip path: persist the type color for every equipped same-type
            // cosmetic that does not already have its own per-cosmetic override.
            int realTypeColor = PerCosmeticColors.GetRealTypeColor(typeIdx, colors);
            if (realTypeColor < 0) return;

            var equipped = __instance.cosmeticEquipped;
            bool any = false;
            foreach (int idx in equipped)
            {
                if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
                var asset = __instance.cosmeticAssets[idx];
                if (asset == null || asset.type != _cosmeticAssetNew.type) continue;
                if (PerCosmeticColors.HasOverride(asset.assetId)) continue; // already pinned
                PerCosmeticColors.SetNoSave(asset.assetId, realTypeColor);
                any = true;
            }
            if (any) PerCosmeticColors.SaveNow();
        }
    }
}

// When the hover preview ends (CosmeticPreviewSet(false)) and no per-cosmetic paint
// is in progress, discard the temporary preview color overrides accumulated by
// CosmeticEquipColorPreservePatch so they don't bleed into the next hover session.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPreviewSet))]
internal static class CosmeticPreviewSetClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool _state)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (!_state && PerCosmeticColors.PendingAsset == null)
            PerCosmeticColors.ClearPreviewOverrides();
    }
}

// Reset All (MenuPageCosmetics.ResetAllButton) unequips ALL cosmetics BEFORE calling
// CosmeticColorSet(type, 0) for each type.
[HarmonyPatch(typeof(MenuPageCosmetics), "ResetAllButton")]
internal static class ResetAllButtonClearPatch
{
    [HarmonyPostfix]
    private static void Postfix()
        => PerCosmeticColors.ClearAll();
}
