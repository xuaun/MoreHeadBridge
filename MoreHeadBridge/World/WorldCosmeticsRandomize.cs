using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla buckets all Hat-type cosmetics together and picks one winner. Because worlds
// share Hat type, this means hat OR world, never both. After vanilla runs, we strip its
// single Hat pick and replace it with two independent 75% rolls (one per slot).
// RandomizeBodyButton is unaffected (it explicitly denies Hat type).
internal static class WorldCosmeticsRandomize
{
    private static void ApplyIndependentRolls()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        // World-specific hat-splitting: only needed when bridge-side worlds are loaded.
        if (HhhCosmeticLoader.WorldAssetIds.Count > 0)
        {
            var unlockedHats   = new List<int>();
            var unlockedWorlds = new List<int>();

            foreach (int idx in meta.cosmeticUnlocks)
            {
                if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
                var asset = meta.cosmeticAssets[idx];
                if (asset == null || asset.type != SemiFunc.CosmeticType.Hat) continue;
                if (!asset.prefab.IsValid()) continue;

                if (HhhCosmeticLoader.IsWorldAsset(asset))
                    unlockedWorlds.Add(idx);
                else
                    unlockedHats.Add(idx);
            }

            // Only override vanilla's combined Hat pick when there are bridge worlds to place.
            if (unlockedWorlds.Count > 0)
            {
                // Remove whatever vanilla placed in the combined Hat bucket so we can
                // replace it with properly split independent selections.
                meta.cosmeticEquipped.RemoveAll(idx =>
                {
                    if (idx < 0 || idx >= meta.cosmeticAssets.Count) return false;
                    var asset = meta.cosmeticAssets[idx];
                    return asset != null && asset.type == SemiFunc.CosmeticType.Hat;
                });

                // Independent 75% roll for a real hat.
                if (unlockedHats.Count > 0 && Random.Range(0f, 1f) <= 0.75f)
                    meta.cosmeticEquipped.Add(unlockedHats[Random.Range(0, unlockedHats.Count)]);

                // Independent 75% roll for a world cosmetic.
                if (Random.Range(0f, 1f) <= 0.75f)
                    meta.cosmeticEquipped.Add(unlockedWorlds[Random.Range(0, unlockedWorlds.Count)]);

                meta.Save();
                meta.CosmeticPlayerUpdateLocal(_synced: false);
            }
        }

        // Refresh SELECTED so the newly-rolled result appears immediately.
        var page = CosmeticsMenuState.ActivePage;
        if (page != null
            && page.selectedTab == MenuPageCosmetics.CosmeticPageTab.Cosmetics
            && CosmeticsMenuState.IsSelected(page.selectedCategory))
            page.RefreshScrollContent();
    }

    [HarmonyPatch(typeof(MenuPageCosmetics), "RandomizeAllButton")]
    internal static class RandomizeAllPostfix
    {
        [HarmonyPostfix]
        private static void Postfix() => ApplyIndependentRolls();
    }

    [HarmonyPatch(typeof(MenuPageCosmetics), "RandomizeCosmeticsButton")]
    internal static class RandomizeCosmeticsPostfix
    {
        [HarmonyPostfix]
        private static void Postfix() => ApplyIndependentRolls();
    }
}
