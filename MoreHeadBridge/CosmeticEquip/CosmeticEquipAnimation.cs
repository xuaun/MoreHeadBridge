using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Fast-forwards a cosmetic's equip pop-in (equipLerp=1, mesh scales restored): cosmetics dressed on an
// inactive body never ran Update(), so they're stuck at scale 0 — the mini death-head needs them finished before cloning.
internal static class CosmeticEquipAnimation
{
    private static System.Reflection.FieldInfo? _equipLerpField;
    private static System.Reflection.FieldInfo? _meshParentsScaleField;

    internal static void Finish(GameObject go)
    {
        try
        {
            var cosmetic = go != null ? go.GetComponentInChildren<Cosmetic>(true) : null;
            if (cosmetic == null) return;

            _equipLerpField ??= AccessTools.Field(typeof(Cosmetic), "equipLerp");
            _equipLerpField?.SetValue(cosmetic, 1f);

            _meshParentsScaleField ??= AccessTools.Field(typeof(Cosmetic), "meshParentsScale");
            if (_meshParentsScaleField?.GetValue(cosmetic) is List<Vector3> scales)
            {
                var parents = cosmetic.meshParents;
                for (int i = 0; i < parents.Count && i < scales.Count; i++)
                {
                    if (parents[i] != null)
                        parents[i].localScale = scales[i];
                }
            }
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"CosmeticEquipAnimation.Finish failed: {ex.Message}");
        }
    }
}
