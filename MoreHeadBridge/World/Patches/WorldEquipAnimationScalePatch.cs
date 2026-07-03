using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

// For bridged cosmetics, adjust the scale-to-zero behavior based on the configured mode.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Setup))]
internal static class WorldEquipAnimationScalePatch
{
    private static readonly FieldInfo? MeshParentsScaleField =
        AccessTools.Field(typeof(Cosmetic), "meshParentsScale");

    [HarmonyPostfix]
    private static void Postfix(Cosmetic __instance)
    {
        if (__instance == null || __instance.cosmeticAsset == null) return;
        // Apply equip-animation override for bridge AND modded non-bridge cosmetics
        if (!BridgeIds.IsCustomizable(__instance.cosmeticAsset)) return;

        // If we're in the live-preview context, use the pending mode so the
        // preview reflects the user's current (unsaved) selection.
        var assetId = __instance.cosmeticAsset.assetId;
        VanillaEquipAnimationMode mode;
        bool isPreview = OverridePreviewContext.IsActiveFor(__instance.playerCosmetics, assetId);
        if (isPreview && OverridePreviewContext.Data != null && OverridePreviewContext.Data.VanillaEquipAnimationMode.HasValue)
            mode = OverridePreviewContext.Data.VanillaEquipAnimationMode.Value;
        else
            mode = CustomizerStore.GetEffectiveEquipAnimationMode(assetId);
        if (mode == VanillaEquipAnimationMode.Normal) return;

        if (MeshParentsScaleField?.GetValue(__instance) is not List<Vector3> scales) return;

        const float MinScaleFactor = 0.05f;
        var parents = __instance.meshParents;
        for (int i = 0; i < parents.Count && i < scales.Count; i++)
        {
            if (parents[i] == null) continue;

            if (mode == VanillaEquipAnimationMode.Disabled)
            {
                __instance.iconCreationAvatar = true;
                parents[i].localScale = scales[i];
            }
            else
            {
                parents[i].localScale = scales[i] * MinScaleFactor;
            }
        }
    }
}
