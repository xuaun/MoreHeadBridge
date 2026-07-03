using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Surfaces a non-bridge modded cosmetic's own root-level CosmeticOffsetCondition in the customizer and lets
// the bridge take it over. Only single-customType offsets fit the bridge's per-trigger model (made editable);
// the rest (poses, cosmetic-type, sibling, multi-condition, fit) are carried through verbatim.
internal static class NativeOffsetImport
{
    // Editable = one customType condition and nothing else.
    private static bool IsEditable(CosmeticOffsetCondition.Offset o, out CosmeticCustomCondition.Type trigger)
    {
        trigger = default;
        if (o?.customTypes == null || o.customTypes.Count != 1) return false;
        if (o.cosmeticTypes is { Count: > 0 } || o.playerPoses is { Count: > 0 } || o.cosmetics is { Count: > 0 })
            return false;
        trigger = o.customTypes[0];
        return !OffsetSeedDefaults.IsFitTrigger(trigger);
    }

    // Pre-fill the editor with the cosmetic's editable native offsets, for triggers not already configured.
    // Reads the PREFAB — the live instance's mesh scale is zeroed for the equip animation by injection time.
    internal static void MergeIntoPending(CosmeticAsset asset, List<CosmeticOffsetEntry> pending)
    {
        if (BridgeIds.IsBridgeAsset(asset) || asset.prefab?.Prefab is not { } prefab) return;

        foreach (var comp in prefab.GetComponents<CosmeticOffsetCondition>())
        {
            if (comp?.offsets == null) continue;
            foreach (var o in comp.offsets)
            {
                if (!IsEditable(o, out var trigger)) continue;

                bool already = false;
                foreach (var e in pending) if (e.TriggerType == trigger) { already = true; break; }
                if (already) continue;

                pending.Add(new CosmeticOffsetEntry
                {
                    TriggerType = trigger,
                    PosX = o.position.x, PosY = o.position.y, PosZ = o.position.z,
                    RotX = o.rotation.x, RotY = o.rotation.y, RotZ = o.rotation.z,
                    ScaleX = o.scale.x, ScaleY = o.scale.y, ScaleZ = o.scale.z,
                    LerpSpeed = o.lerpSpeed,
                });
            }
        }
    }

    // Carry non-editable native offsets into the bridge component and remove the native one(s), so a single
    // CosmeticOffsetCondition drives the transform. Editable offsets are dropped — they live in the bridge list.
    internal static void AbsorbAndStrip(GameObject instance, CosmeticOffsetCondition bridgeComp)
    {
        foreach (var nat in instance.GetComponents<CosmeticOffsetCondition>())
        {
            if (nat == bridgeComp) continue;
            if (nat.offsets != null)
                foreach (var o in nat.offsets)
                    if (!IsEditable(o, out _))
                        bridgeComp.offsets.Add(CloneOffset(o));
            UnityEngine.Object.Destroy(nat);
        }
    }

    private static CosmeticOffsetCondition.Offset CloneOffset(CosmeticOffsetCondition.Offset o) => new()
    {
        setupName     = o.setupName,
        cosmeticTypes = o.cosmeticTypes != null ? new List<SemiFunc.CosmeticType>(o.cosmeticTypes) : new(),
        customTypes   = o.customTypes != null ? new List<CosmeticCustomCondition.Type>(o.customTypes) : new(),
        playerPoses   = o.playerPoses != null ? new List<PlayerAvatarVisuals.Pose>(o.playerPoses) : new(),
        cosmetics     = o.cosmetics != null ? new List<CosmeticAsset>(o.cosmetics) : new(),
        position      = o.position,
        rotation      = o.rotation,
        scale         = o.scale,
        lerpSpeed     = o.lerpSpeed,
    };
}
