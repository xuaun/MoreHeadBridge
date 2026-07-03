using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla buckets all Hat-type cosmetics together and picks one winner; since worlds share Hat type, that means hat OR world, never both. After vanilla runs, we strip its single Hat pick and replace it with two independent 75% rolls (one per slot). Also, after any Randomize button, eligible bridge cosmetics receive the type colour vanilla picked for their slot — extending vanilla's non-bridge colouring to bridge cosmetics that opted in to tinting.
internal static class WorldCosmeticsRandomize
{
    private static void SyncBridgeColorsAfterRandomize()
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null || meta.colorsEquipped == null) return;

        bool any = false;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (!BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset)) continue;

            int typeIdx = (int)asset.type;
            if (typeIdx < 0 || typeIdx >= meta.colorsEquipped.Length) continue;
            int colorId = meta.colorsEquipped[typeIdx];
            if (colorId < 0) continue;

            PerCosmeticColors.SetNoSave(asset.assetId, colorId);
            any = true;
        }

        if (!any) return;
        PerCosmeticColors.Save();
        meta.CosmeticPlayerUpdateLocal(_synced: false);
    }

    private static void ApplyIndependentRolls()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

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

            if (unlockedWorlds.Count > 0)
            {
                meta.cosmeticEquipped.RemoveAll(idx =>
                {
                    if (idx < 0 || idx >= meta.cosmeticAssets.Count) return false;
                    var asset = meta.cosmeticAssets[idx];
                    return asset != null && asset.type == SemiFunc.CosmeticType.Hat;
                });

                if (unlockedHats.Count > 0 && Random.Range(0f, 1f) <= 0.75f)
                    meta.cosmeticEquipped.Add(unlockedHats[Random.Range(0, unlockedHats.Count)]);

                if (Random.Range(0f, 1f) <= 0.75f)
                    meta.cosmeticEquipped.Add(unlockedWorlds[Random.Range(0, unlockedWorlds.Count)]);

                meta.Save();
                meta.CosmeticPlayerUpdateLocal(_synced: false);
            }
        }
    }

    [HarmonyPatch(typeof(MenuPageCosmetics), "RandomizeAllButton")]
    internal static class RandomizeAllPostfix
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SyncBridgeColorsAfterRandomize();
            ApplyIndependentRolls();
        }
    }

    [HarmonyPatch(typeof(MenuPageCosmetics), "RandomizeBodyButton")]
    internal static class RandomizeBodyPostfix
    {
        [HarmonyPostfix]
        private static void Postfix() => SyncBridgeColorsAfterRandomize();
    }

    [HarmonyPatch(typeof(MenuPageCosmetics), "RandomizeCosmeticsButton")]
    internal static class RandomizeCosmeticsPostfix
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SyncBridgeColorsAfterRandomize();
            ApplyIndependentRolls();
        }
    }
}
