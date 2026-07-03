// Coloured menu borders for modded cosmetics (never touches CosmeticAsset.rarity): bridge → orange (HighlightBridgeCosmetics), non-bridge modded → purple (HighlightModdedCosmetics); the per-cosmetic IsModded override wins.

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(CosmeticAsset), "GetRarityColor")]
internal static class ModdedRarityBorderPatch
{
    // Bridge cosmetics: deep orange-red, distinct from UltraRare gold and vanilla rarity colors.
    private static readonly Color OrangeColor = new Color32(255, 77, 0, 255);

    // Non-bridge modded cosmetics: reddish violet, distinct from orange/gold/green/cyan/magenta.
    private static readonly Color PurpleColor = new Color32(168, 26, 235, 255);

    [HarmonyPostfix]
    private static void Postfix(CosmeticAsset __instance, ref Color __result)
    {
        // Pack themes outrank the plain highlight. Returning the colour HERE (not repainting per frame) lets vanilla apply its idle/hover/selected scaling on top — no feedback loop.
        // Solid packs → their colour; gradient packs → WHITE so BridgeBorderTheme's texture shows true colours (still state-scaled).
        if (BorderTheme.TryResolve(__instance, out var theme))
        {
            __result = theme.HasSolid ? theme.Solid : Color.white;
            return;
        }

        if (CustomizerStore.IsModdedForAsset(__instance))
            __result = OrangeColor;
        else if (CustomizerStore.IsNonBridgeModdedForAsset(__instance))
            __result = PurpleColor;
    }
}
