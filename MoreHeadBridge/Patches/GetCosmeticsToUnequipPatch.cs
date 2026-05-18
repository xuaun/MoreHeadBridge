using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla assumes every CosmeticAsset/CosmeticTypeAsset list is initialized.
// Bridge assets are intentionally minimal, so customTypeList can be null and
// crash when vanilla overlay cosmetics check slot conflicts.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.GetCosmeticsToUnequip))]
internal static class GetCosmeticsToUnequipPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        MetaManager __instance,
        List<int> _cosmeticEquipped,
        CosmeticAsset _cosmeticAssetNew,
        ref List<CosmeticAsset> __result)
    {
        __result = [];

        if (!_cosmeticAssetNew)
            return false;

        CosmeticTypeAsset? cosmeticTypeAsset = GetTypeAsset(__instance, _cosmeticAssetNew.type);

        foreach (int item in _cosmeticEquipped)
        {
            if (item < 0 || item >= __instance.cosmeticAssets.Count)
                continue;

            CosmeticAsset cosmeticAsset = __instance.cosmeticAssets[item];
            if (!cosmeticAsset || cosmeticAsset == _cosmeticAssetNew)
                continue;

            CosmeticTypeAsset? cosmeticTypeAsset2 = GetTypeAsset(__instance, cosmeticAsset.type);
            bool newIsWorld = HhhCosmeticLoader.IsWorldAsset(_cosmeticAssetNew);
            bool equippedIsWorld = HhhCosmeticLoader.IsWorldAsset(cosmeticAsset);
            bool worldSlotInvolved = newIsWorld || equippedIsWorld;

            if (newIsWorld && equippedIsWorld)
            {
                __result.Add(cosmeticAsset);
                continue;
            }

            bool sameExclusiveType =
                !worldSlotInvolved &&
                cosmeticAsset.type == _cosmeticAssetNew.type &&
                !(cosmeticTypeAsset?.canEquipMultiple ?? false);

            bool typeDisabled =
                !worldSlotInvolved &&
                (TypeListContains(cosmeticTypeAsset?.disabledTypeList, cosmeticTypeAsset2) ||
                 TypeListContains(cosmeticTypeAsset2?.disabledTypeList, cosmeticTypeAsset));

            if (sameExclusiveType || typeDisabled)
            {
                __result.Add(cosmeticAsset);
                continue;
            }

            if (worldSlotInvolved)
                continue;

            foreach (CosmeticCustomCondition.Type disabledCustomType in cosmeticTypeAsset?.disabledCustomTypeList ?? [])
            {
                if (ContainsCustomType(cosmeticAsset.customTypeList, disabledCustomType) ||
                    ContainsCustomType(cosmeticTypeAsset2?.customTypeList, disabledCustomType))
                {
                    __result.Add(cosmeticAsset);
                    break;
                }
            }
        }

        return false;
    }

    private static CosmeticTypeAsset? GetTypeAsset(MetaManager metaManager, SemiFunc.CosmeticType type)
    {
        int index = (int)type;
        if (index < 0 || index >= metaManager.cosmeticTypeAssets.Count)
            return null;

        return metaManager.cosmeticTypeAssets[index];
    }

    // Checks whether a type list includes the type of a given CosmeticTypeAsset.
    private static bool TypeListContains(List<SemiFunc.CosmeticType>? list, CosmeticTypeAsset? asset)
        => list != null && asset != null && list.Contains(asset.type);

    private static bool ContainsCustomType(
        List<CosmeticCustomCondition.Type>? list,
        CosmeticCustomCondition.Type value)
        => list != null && list.Contains(value);
}
