using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// The "X" button in a cosmetic section calls ToggleCosmetic with cosmeticAsset == null,
// which makes vanilla loop over every equipped cosmetic whose type matches the section's
// subCategory and call CosmeticUnequip on each. Because world cosmetics share Hat as
// their vanilla type with real hats, clicking the Hat "X" clears both hat + world, and
// clicking the World "X" also clears both.
//
// Fix: in the Prefix, back up the "opposite side" Hat-type cosmetics that vanilla is
// about to wrongly remove (real hats when in World category, worlds when in Hat category).
// In the Postfix, restore any of them that vanilla removed and re-save.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "ToggleCosmetic")]
internal static class WorldCosmeticClearButtonPatch
{
    private static List<int>? _backup;

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticButton __instance)
    {
        _backup = null;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        if (__instance.cosmeticAsset != null) return;   // specific cosmetic button, not "X"
        if (MetaManager.instance == null) return;

        var section = __instance.cosmeticSection;
        if (section == null || section.subCategory != SemiFunc.CosmeticType.Hat) return;

        bool inWorldCategory = WorldCosmeticsMenuState.IsWorldCategory(
            section.menuPageCosmetics?.selectedCategory);

        WorldCosmeticsMenuState.PartitionHatCosmetics(MetaManager.instance, out var realHats, out var worlds);
        // If in World category, protect real hats; if in Hat category, protect worlds.
        var toProtect = inWorldCategory ? realHats : worlds;

        if (toProtect.Count > 0)
            _backup = toProtect;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        var backup = _backup;
        _backup = null;
        if (backup == null || MetaManager.instance == null) return;

        bool any = false;
        foreach (int idx in backup)
        {
            if (!MetaManager.instance.cosmeticEquipped.Contains(idx))
            {
                MetaManager.instance.cosmeticEquipped.Add(idx);
                any = true;
            }
        }

        if (any)
            MetaManager.instance.Save();
    }
}
