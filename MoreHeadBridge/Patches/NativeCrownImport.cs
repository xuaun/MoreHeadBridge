using UnityEngine;

namespace MoreHeadBridge;

// Crown equivalent of NativeOffsetImport: imports a non-bridge cosmetic's native CosmeticPlayerCrown.
internal static class NativeCrownImport
{
    // Fills an unset pending from the prefab's crown target (root-local transform + priority/spring).
    internal static void MergeIntoPending(CosmeticAsset asset, ref CosmeticCrownConfig? pending)
    {
        if (pending != null || BridgeIds.IsBridgeAsset(asset)) return;
        if (asset.type is not (SemiFunc.CosmeticType.Hat or SemiFunc.CosmeticType.HeadTopMesh)) return;
        if (asset.prefab?.Prefab is not { } prefab) return;

        var comp = prefab.GetComponentInChildren<CosmeticPlayerCrown>(true);
        if (comp == null || comp.targetMain == null) return;

        // targetMain in the prefab root's local frame (the frame ApplyCrownConfig writes in).
        Matrix4x4 m = prefab.transform.worldToLocalMatrix * comp.targetMain.localToWorldMatrix;
        Vector3 pos = m.GetColumn(3);
        Vector3 rot = m.rotation.eulerAngles;
        Vector3 scale = m.lossyScale;

        pending = new CosmeticCrownConfig
        {
            PosX = pos.x, PosY = pos.y, PosZ = pos.z,
            RotX = Wrap180(rot.x), RotY = Wrap180(rot.y), RotZ = Wrap180(rot.z),
            ScaleX = scale.x, ScaleY = scale.y, ScaleZ = scale.z,
            Priority = comp.priority,
            DisableSpring = comp.disableSpring,
        };
    }

    private static float Wrap180(float deg) => deg > 180f ? deg - 360f : deg;
}
