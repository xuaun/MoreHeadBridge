using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Shape-condition equivalent of NativeOffsetImport: surfaces a non-bridge cosmetic's native
// CosmeticCustomCondition.types in the customizer and lets the bridge take them over.
internal static class NativeCustomTypeImport
{
    // True when a non-bridge cosmetic announces condition types from its shared asset list (what the broadcaster takes over).
    internal static bool HasNativeAnnounceList(CosmeticAsset asset)
        => !BridgeIds.IsBridgeAsset(asset) && asset.customTypeList is { Count: > 0 };

    // Adds the cosmetic's native condition types valid for its slot to the pending set.
    internal static void MergeIntoPending(CosmeticAsset asset, HashSet<CosmeticCustomCondition.Type> pending)
    {
        if (BridgeIds.IsBridgeAsset(asset)) return;

        var valid = new HashSet<CosmeticCustomCondition.Type>(CosmeticTriggerCatalog.ValidCustomTypes(asset.type));
        if (valid.Count == 0) return;

        // Primary source: the asset's own announce list (how vanilla / Unity authors declare conditions).
        if (asset.customTypeList != null)
            foreach (var t in asset.customTypeList)
                if (valid.Contains(t)) pending.Add(t);

        if (asset.prefab?.Prefab is { } prefab)
            foreach (var comp in prefab.GetComponentsInChildren<CosmeticCustomCondition>(true))
            {
                if (comp?.types == null) continue;
                foreach (var t in comp.types)
                    if (valid.Contains(t)) pending.Add(t);
            }
    }

    // Carries the native non-slot types into the broadcaster and destroys the native component, leaving the
    // broadcaster the single announcer. Slot types aren't carried — they live in the bridge set, so OFF sticks.
    internal static void AbsorbAndStrip(GameObject instance, CosmeticAsset asset, BridgeCustomTypesBroadcaster broadcaster)
    {
        if (BridgeIds.IsBridgeAsset(asset)) return;

        var valid = new HashSet<CosmeticCustomCondition.Type>(CosmeticTriggerCatalog.ValidCustomTypes(asset.type));
        foreach (var comp in instance.GetComponentsInChildren<CosmeticCustomCondition>(true))
        {
            if (comp == null) continue;
            if (comp.types != null)
                foreach (var t in comp.types)
                    if (!valid.Contains(t) && !broadcaster.Types.Contains(t))
                        broadcaster.Types.Add(t);
            Object.Destroy(comp);
        }
    }
}
