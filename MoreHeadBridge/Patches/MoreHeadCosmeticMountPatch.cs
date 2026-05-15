// ============================================================================
// Re-parent bridge cosmetics onto the same runtime anchors MoreHead uses,
// preserving each .hhh prefab's authored transform values.
//
// Vanilla REPO parents cosmetics under its own CosmeticParent anchors and resets
// localPosition/Rotation/Scale. MoreHead .hhh prefabs are authored against
// MoreHead's rig/world anchors, so bridge assets that need special placement are
// mounted here after vanilla instantiation.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static class MoreHeadCosmeticMountPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null) return;
        if (!BridgeIds.IsBridgeAsset(_cosmeticAsset)) return;
        if (__instance == null || __instance.playerAvatarVisuals == null) return;

        var sourcePrefab = _cosmeticAsset.prefab?.Prefab;
        if (sourcePrefab == null) return;

        if (HhhCosmeticLoader.IsWorldAsset(_cosmeticAsset))
        {
            var worldNode = EnsureWorldFollower(__instance.playerAvatarVisuals.transform);
            if (worldNode == null) return;

            Mount(__result, worldNode, sourcePrefab);
            return;
        }

        string targetBone;
        if (_cosmeticAsset.type == SemiFunc.CosmeticType.ArmRight) targetBone = "ANIM ARM R SCALE";
        else if (_cosmeticAsset.type == SemiFunc.CosmeticType.ArmLeft) targetBone = "code_arm_l";
        else return;

        var bone = FindByName(__instance.playerAvatarVisuals.transform, targetBone);
        if (bone == null) return;

        Mount(__result, bone, sourcePrefab);
    }

    internal static void Mount(GameObject instance, Transform parent, GameObject sourcePrefab)
    {
        instance.transform.SetParent(parent, worldPositionStays: false);
        instance.transform.localPosition = sourcePrefab.transform.localPosition;
        instance.transform.localRotation = sourcePrefab.transform.localRotation;
        instance.transform.localScale = sourcePrefab.transform.localScale;
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

    internal static Transform? EnsureWorldFollower(Transform root)
    {
        var existing = root.Find("WorldDecorationFollower");
        if (existing != null) return existing;

        var obj = new GameObject("WorldDecorationFollower");
        obj.transform.SetParent(root, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        obj.AddComponent<WorldSpaceFollower>();
        return obj.transform;
    }
}
