using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

/// Per-instance CustomTypesLogic mirror for a cosmetic's override (local or remote) — never mutates the shared CosmeticAsset (would bleed across players).
/// OwnerPc must be set by the injector: PlayerCosmetics isn't in the cosmetic's parent hierarchy.
internal sealed class BridgeCustomTypesBroadcaster : MonoBehaviour
{
    internal List<CosmeticCustomCondition.Type> Types = new();
    internal PlayerCosmetics? OwnerPc;

    /// When true, CustomTypesLogicNpeGuardPatch skips the cosmetic's native announce so this broadcaster
    /// is the single source (empty Types = conditions off).
    internal bool SuppressNative;

    private void Update()
    {
        if (OwnerPc == null || Types.Count == 0) return;
        foreach (var t in Types)
            OwnerPc.ConditionCustomSet(t);
    }
}
