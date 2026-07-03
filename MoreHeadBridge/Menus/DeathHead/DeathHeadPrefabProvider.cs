// ============================================================================
// Captures LevelGenerator.PlayerDeathHeadPrefab via a LevelGenerator.Awake postfix — the main menu has its own LevelGenerator, so the prefab is available without entering a session.
//
// Also lists the cosmetic types the death head can carry (SupportedTypes), used to gate
// the "Death Head" button and pick which equipped cosmetics to mount.
// ============================================================================

using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(LevelGenerator), "Awake")]
internal static class DeathHeadPrefabProvider
{
    /// The captured death-head prefab, or null if no LevelGenerator has awoken yet.
    internal static GameObject? Prefab { get; private set; }

    // Types the death head has anchors for (read from the prefab): head/face gear + the little legs. NOT HeadBottom/HeadBottomMesh/HeadBottomOverlay (no anchor).
    internal static readonly HashSet<SemiFunc.CosmeticType> SupportedTypes = new()
    {
        SemiFunc.CosmeticType.Hat,
        SemiFunc.CosmeticType.HeadTopMesh,
        SemiFunc.CosmeticType.HeadTopOverlay,
        SemiFunc.CosmeticType.Ears,
        SemiFunc.CosmeticType.Eyewear,
        SemiFunc.CosmeticType.FaceTop,
        SemiFunc.CosmeticType.FaceBottom,
        SemiFunc.CosmeticType.LegRightMesh,
        SemiFunc.CosmeticType.LegLeftMesh,
    };

    [HarmonyPostfix]
    private static void Postfix(LevelGenerator __instance)
    {
        if (Prefab != null) return;
        Prefab = __instance.PlayerDeathHeadPrefab;
    }
}
