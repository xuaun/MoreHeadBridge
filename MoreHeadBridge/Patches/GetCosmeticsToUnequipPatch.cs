using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Worlds share the Hat slot but must always coexist with hats — this postfix drops world↔non-world pairs
// from vanilla's unequip result. Same-type/disabled/world↔world are left to vanilla (canEquipMultiple handles them).
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.GetCosmeticsToUnequip))]
internal static class GetCosmeticsToUnequipPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticAsset _cosmeticAssetNew, List<CosmeticAsset> __result)
    {
        if (__result.Count == 0 || _cosmeticAssetNew == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return; // no worlds registered → nothing to reconcile

        bool newIsWorld = HhhCosmeticLoader.IsWorldAsset(_cosmeticAssetNew);

        for (int i = __result.Count - 1; i >= 0; i--)
        {
            var asset = __result[i];
            if (asset == null) continue;
            // Exactly one side is a world → separate slots, keep both (remove from the unequip list).
            if (newIsWorld != HhhCosmeticLoader.IsWorldAsset(asset))
                __result.RemoveAt(i);
        }
    }
}
