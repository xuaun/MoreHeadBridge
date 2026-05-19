// ============================================================================
// When [General] HighlightModdedCosmetics is true, bridge cosmetics display
// an orange border in the cosmetics menu instead of their vanilla rarity color.
//
// Per-cosmetic IsModded overrides (set via the CosmeticCustomizer popup) take
// priority over the global setting:
//   IsModded = true  → always orange, even when HighlightModdedCosmetics=false
//   IsModded = false → never orange, even when HighlightModdedCosmetics=true
//   IsModded = null  → follow the global HighlightModdedCosmetics setting
//
// The actual rarity field (CosmeticAsset.rarity) is never modified, so the
// sort order in the cosmetics menu is unchanged — it is still controlled by
// the [General] DefaultRarity config entry (or per-cosmetic Rarity override).
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(CosmeticAsset), "GetRarityColor")]
internal static class ModdedRarityBorderPatch
{
    // Deep orange-red — clearly distinct from UltraRare gold (255,199,0)
    // and from all other vanilla rarity colors (green / cyan / magenta).
    // RGB(255, 77, 0): enough red dominance to read as orange-red, not gold.
    private static readonly Color OrangeColor = new Color(1f, 0.3f, 0f, 1f);

    [HarmonyPostfix]
    private static void Postfix(CosmeticAsset __instance, ref Color __result)
    {
        // PerCosmeticOverrides.IsModdedForAsset handles both the per-cosmetic override
        // and the global fallback; it returns false for non-bridge assets.
        if (PerCosmeticOverrides.IsModdedForAsset(__instance))
            __result = OrangeColor;
    }
}
