using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Vanilla's CosmeticUnequip may sweep ALL Hat-type entries from cosmeticEquipped
// when any one Hat-type cosmetic is unequipped. Since world cosmetics are also Hat
// type, this incorrectly removes them when a real Hat is unequipped, and vice versa.
//
// Fix: Prefix backs up the "opposite side" Hat-type indices (world when unequipping
// hat, real hat when unequipping world). Postfix restores any that vanilla removed
// as collateral. The next LateUpdate's SetupCosmeticsLogic will re-spawn them.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticUnequip))]
internal static class WorldCosmeticUnequipPatch
{
    private static List<int>? _backup;

    [HarmonyPrefix]
    private static void Prefix(MetaManager __instance, CosmeticAsset _cosmeticAsset)
    {
        _backup = null;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        if (_cosmeticAsset?.type != SemiFunc.CosmeticType.Hat) return;

        bool targetIsWorld = HhhCosmeticLoader.IsWorldAsset(_cosmeticAsset);

        // Back up the cosmetics on the "other side" so we can restore them if vanilla
        // sweeps them as part of the Hat-slot type cleanup.
        var backup = new List<int>();
        foreach (int idx in __instance.cosmeticEquipped)
        {
            if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
            var asset = __instance.cosmeticAssets[idx];
            if (asset?.type != SemiFunc.CosmeticType.Hat) continue;

            bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
            if (assetIsWorld != targetIsWorld) // opposite side
                backup.Add(idx);
        }

        if (backup.Count > 0)
            _backup = backup;
    }

    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance)
    {
        var backup = _backup;
        _backup = null;
        if (backup == null) return;

        foreach (int idx in backup)
        {
            if (!__instance.cosmeticEquipped.Contains(idx))
                __instance.cosmeticEquipped.Add(idx);
        }
        // Visual refresh happens automatically on the next LateUpdate via
        // MenuPageCosmetics → CosmeticPlayerUpdateLocal → SetupCosmeticsLogic.
    }
}
