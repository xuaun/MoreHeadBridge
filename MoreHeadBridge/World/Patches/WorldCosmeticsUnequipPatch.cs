using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Vanilla's CosmeticUnequip sweeps ALL Hat-type entries from cosmeticEquipped when any one is unequipped (worlds share Hat type). Fix: the prefix backs up every Hat-type entry that should survive — always the "opposite side" (worlds when unequipping a hat, hats when unequipping a world), plus same-side extras when AllowMultipleCosmetics is on (so only the specific cosmetic is removed). Postfix restores entries vanilla swept as collateral.
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
        // Visual refresh happens automatically next LateUpdate via MenuPageCosmetics → CosmeticPlayerUpdateLocal → SetupCosmeticsLogic.
    }
}
