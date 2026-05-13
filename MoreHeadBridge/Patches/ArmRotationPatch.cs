// ============================================================================
// [ArmRotation] Re-parent bridge arm cosmetics onto the same rig bones
// MoreHead uses, preserving the .hhh prefab's authored transform values.
//
// Why:
//   * REPO vanilla parents arm cosmetics under its own CosmeticParent anchor
//     and resets localPosition/Rotation/Scale to identity in InstantiateCosmetic.
//   * .hhh arm prefabs were authored for MoreHead's bones (code_arm_l /
//     ANIM ARM R SCALE) WITH the prefab's own transform values intact.
//   * Replicating MoreHead's mount — same bone, prefab's original transform —
//     makes every .hhh look exactly the way its creator intended without any
//     per-cosmetic guesswork.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static class ArmRotationPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null) return;
        if (!BridgeIds.IsBridgeAsset(_cosmeticAsset)) return;
        if (__instance == null || __instance.playerAvatarVisuals == null) return;

        string targetBone;
        if (_cosmeticAsset.type == SemiFunc.CosmeticType.ArmRight) targetBone = "ANIM ARM R SCALE";
        else if (_cosmeticAsset.type == SemiFunc.CosmeticType.ArmLeft) targetBone = "code_arm_l";
        else return;

        var bone = FindByName(__instance.playerAvatarVisuals.transform, targetBone);
        var sourcePrefab = _cosmeticAsset.prefab?.Prefab;
        if (bone == null || sourcePrefab == null) return;

        __result.transform.SetParent(bone, worldPositionStays: false);
        __result.transform.localPosition = sourcePrefab.transform.localPosition;
        __result.transform.localRotation = sourcePrefab.transform.localRotation;
        __result.transform.localScale = sourcePrefab.transform.localScale;
    }

    private static Transform? FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var hit = FindByName(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}
