using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla Cosmetic.Setup() logs a LogError for any Hat/HeadTopMesh cosmetic with no CosmeticPlayerCrown in its hierarchy ("'{name}' has no CosmeticPlayerCrown!").
// "Fix Crown Error" injects an empty CosmeticPlayerCrown before that check. With no targetMain/cosmeticBlocked, PlayerCrown.UpdateTarget never selects it, so no crown appears — purely cosmetic.
// Bridge cosmetics skipped — ApplyCrownConfig already injects a configured one.
[HarmonyPatch(typeof(Cosmetic), "Setup")]
internal static class CosmeticSetupFixCrownPatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        // Only Hat and HeadTopMesh trigger the crown log in Setup().
        if (__instance.type != SemiFunc.CosmeticType.Hat &&
            __instance.type != SemiFunc.CosmeticType.HeadTopMesh)
            return;

        // Only apply when the per-cosmetic override explicitly requests it.
        if (!CustomizerStore.GetEffectiveFixCrown(__instance.cosmeticAsset?.assetId))
            return;

        // Bridge cosmetics handle crown through ApplyCrownConfig — skip them.
        if (BridgeIds.IsBridgeAsset(__instance.cosmeticAsset))
            return;

        // Don't duplicate an existing CosmeticPlayerCrown (prefab already has one, or Setup() ran twice on a re-instantiated cosmetic).
        if (__instance.GetComponentInChildren<CosmeticPlayerCrown>(includeInactive: true) != null)
            return;

        var child = new GameObject("Crown Fix (Bridge)");
        child.transform.SetParent(__instance.transform, false);
        child.AddComponent<CosmeticPlayerCrown>();
    }
}
