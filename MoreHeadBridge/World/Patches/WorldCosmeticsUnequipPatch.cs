using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Vanilla's CosmeticUnequip sweeps ALL Hat-type entries from cosmeticEquipped when
// any one Hat-type cosmetic is unequipped (worlds share Hat type).
//
// Fix: Prefix backs up every Hat-type entry that should survive:
//   - Always: the "opposite side" (worlds when unequipping hat, hats when unequipping world).
//   - With AllowMultipleCosmetics on: also same-side extras, so only the specific
//     cosmetic being unequipped is removed, not every hat/world at once.
// Postfix restores any entries vanilla swept as collateral.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticUnequip))]
internal static class WorldCosmeticsUnequipPatch
{
    [HarmonyPrefix]
    private static void Prefix(MetaManager __instance, CosmeticAsset _cosmeticAsset, ref List<int>? __state)
    {
        __state = null;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        if (_cosmeticAsset?.type != SemiFunc.CosmeticType.Hat) return;

        bool targetIsWorld = HhhCosmeticLoader.IsWorldAsset(_cosmeticAsset);

        var backup = new List<int>();
        foreach (int idx in __instance.cosmeticEquipped)
        {
            if (idx < 0 || idx >= __instance.cosmeticAssets.Count) continue;
            var asset = __instance.cosmeticAssets[idx];
            if (asset == null || asset == _cosmeticAsset) continue;
            if (asset.type != SemiFunc.CosmeticType.Hat) continue;

            bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
            bool isOpposite = assetIsWorld != targetIsWorld;
            bool isSameSideExtra = Plugin.AllowMultipleCosmetics.Value && assetIsWorld == targetIsWorld;
            if (isOpposite || isSameSideExtra)
                backup.Add(idx);
        }

        if (backup.Count > 0)
            __state = backup;
    }

    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance, List<int>? __state)
    {
        if (__state == null) return;

        foreach (int idx in __state)
        {
            if (!__instance.cosmeticEquipped.Contains(idx))
                __instance.cosmeticEquipped.Add(idx);
        }
        // Visual refresh happens automatically on the next LateUpdate via
        // MenuPageCosmetics → CosmeticPlayerUpdateLocal → SetupCosmeticsLogic.
    }
}
