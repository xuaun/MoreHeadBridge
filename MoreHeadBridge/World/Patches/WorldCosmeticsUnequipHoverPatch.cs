using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Vanilla's Unequip-button (X) hover strips ALL Hat-type items from cosmeticEquippedPreview
// (both real hats and worlds share subCategory == Hat). Prefix on CosmeticPreviewSet
// intercepts just before the update fires and restores the "other side":
//   In WORLD category  → restore real hats (world correctly disappears)
//   In HEAD/Hat category → restore worlds (hat correctly disappears)
[HarmonyPatch(typeof(MetaManager), "CosmeticPreviewSet")]
internal static class WorldCosmeticsUnequipHoverPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool __0) // __0 == _state
    {
        // Only intercept preview-start; preview-end (false) unwinds normally.
        if (!__0) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        // Partition equipped Hat-type cosmetics by subtype.
        var equippedRealHats = new List<int>();
        var equippedWorlds   = new List<int>();

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset?.type != SemiFunc.CosmeticType.Hat) continue;

            if (HhhCosmeticLoader.IsWorldAsset(asset))
                equippedWorlds.Add(idx);
            else
                equippedRealHats.Add(idx);
        }

        // Nothing to protect if neither type was equipped.
        if (equippedRealHats.Count == 0 && equippedWorlds.Count == 0) return;

        bool previewHasAnyHat = false;
        foreach (int idx in meta.cosmeticEquippedPreview)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset?.type == SemiFunc.CosmeticType.Hat) { previewHasAnyHat = true; break; }
        }

        // X-hover signal: Hat-type was equipped but none survive in the preview.
        // A normal hover would leave at least the hovered item in the list.
        if (previewHasAnyHat) return;
        bool inWorldCategory = WorldCosmeticsMenuState.IsWorldCategory(
            WorldCosmeticsMenuState.CurrentPage?.selectedCategory);

        var preview = meta.cosmeticEquippedPreview;
        var toRestore = inWorldCategory ? equippedRealHats : equippedWorlds;

        foreach (int idx in toRestore)
        {
            if (!preview.Contains(idx))
                preview.Add(idx);
        }
    }
}
