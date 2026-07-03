using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla's Unequip-button (X) hover strips ALL Hat-type items from cosmeticEquippedPreview (real hats and worlds share subCategory == Hat). A prefix on CosmeticPreviewSet restores the "other side": WORLD category → restore real hats; HEAD/Hat category → restore worlds; SELECTED/SEARCH → detect from section name or synthetic subCategory.
[HarmonyPatch(typeof(MetaManager), "CosmeticPreviewSet")]
internal static class WorldCosmeticsUnequipHoverPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool _state)
    {
        // Only intercept preview-start; preview-end (false) unwinds normally.
        if (!_state) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        // Partition equipped Hat-type cosmetics by subtype.
        WorldCosmeticsMenuState.PartitionHatCosmetics(meta, out var equippedRealHats, out var equippedWorlds);

        // Nothing to protect if neither type was equipped.
        if (equippedRealHats.Count == 0 && equippedWorlds.Count == 0) return;

        var preview = meta.cosmeticEquippedPreview;

        var hoveredBtn     = WorldCosmeticsMenuState.CurrentPage?.pendingHoveredCosmeticButton;
        var hoveredSection = hoveredBtn?.cosmeticSection;
        bool isVirtualWorldX = hoveredBtn?.cosmeticAsset == null
            && hoveredSection != null
            && (hoveredSection.subCategory == CosmeticsFilterPatch.WorldSubCategory
                || hoveredSection.gameObject.name == CosmeticsFilterPatch.WorldSectionName);

        if (isVirtualWorldX)
        {
            foreach (int idx in equippedWorlds)
                preview.Remove(idx);
            foreach (int idx in equippedRealHats)
            {
                if (!preview.Contains(idx))
                    preview.Add(idx);
            }
            return;
        }

        bool previewHasAnyHat = false;
        foreach (int idx in preview)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset?.type == SemiFunc.CosmeticType.Hat) { previewHasAnyHat = true; break; }
        }

        // X-hover signal: Hat-type was equipped but none survive in the preview.
        if (previewHasAnyHat) return;

        bool inWorldCategory = WorldCosmeticsMenuState.IsWorldCategory(
            WorldCosmeticsMenuState.CurrentPage?.selectedCategory);

        // In virtual categories (SELECTED/SEARCH) selectedCategory isn't WORLD even when a world section's X is hovered, so detect from the hovered button's section: the injected World section uses the synthetic WorldSubCategory (999), falling back to its name WorldSectionName.
        bool hoveringWorld = false;
        if (!inWorldCategory && hoveredSection != null)
        {
            hoveringWorld = hoveredSection.subCategory == CosmeticsFilterPatch.WorldSubCategory
                || hoveredSection.gameObject.name == CosmeticsFilterPatch.WorldSectionName;
        }

        var toRestore = (inWorldCategory || hoveringWorld) ? equippedRealHats : equippedWorlds;

        foreach (int idx in toRestore)
        {
            if (!preview.Contains(idx))
                preview.Add(idx);
        }
    }
}
