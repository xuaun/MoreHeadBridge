using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Runs just before Cosmetic.Setup() on every bridge cosmetic.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Setup))]
internal static class CosmeticSetupPatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        if (__instance == null) return;
        if (!BridgeIds.IsBridgeAsset(__instance.cosmeticAsset)) return;

        if (__instance.type == SemiFunc.CosmeticType.Hat ||
            __instance.type == SemiFunc.CosmeticType.HeadTopMesh)
        {
            if (__instance.GetComponentInChildren<CosmeticPlayerCrown>(true) == null)
                __instance.gameObject.AddComponent<CosmeticPlayerCrown>();
        }

        if (__instance.meshParents == null)
            __instance.meshParents = new List<Transform>();

        if (__instance.meshParents.Count == 0)
        {
            foreach (Transform child in __instance.transform)
                __instance.meshParents.Add(child);
        }
    }
}
