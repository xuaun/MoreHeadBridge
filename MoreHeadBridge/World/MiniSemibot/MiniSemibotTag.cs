using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Marker so WorldCosmeticsSetupPatch can associate the mini avatar GameObject with its CosmeticAsset
// (the avatar root has no Cosmetic component, unlike normal world cosmetics).
internal sealed class MiniSemibotTag : MonoBehaviour
{
    internal CosmeticAsset? Asset;
    internal string? OutfitSig;        // last outfit fingerprint applied, so re-dress only on real changes
    internal int[]? PresetCosmetics;   // RandomPreset snapshot (null = mirror live outfit)
    internal int[]? PresetColors;
    internal int PresetSlot = -1;      // the rolled preset's slot (>= 0 → use its per-cosmetic colours); -1 = none
}

// Pose the follow component can force on the mini's animator for special states.
internal enum MiniPoseOverride
{
    None,        // mirror the wearer normally
    CrouchIdle,  // hold a crouch (death: crouch-wait)
    TumbleIdle,  // hold a tumble/slump (death: death-head)
}
