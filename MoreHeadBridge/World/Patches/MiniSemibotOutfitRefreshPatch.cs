using HarmonyLib;

namespace MoreHeadBridge;

// Re-dress the wearer's Mini-Semibot when their COLORS change. Equip changes are already covered by WorldCosmeticsSetupPatch's postfix on SetupCosmeticsLogic; this covers colour-only changes (which go through SetupColorsLogic without touching SetupCosmeticsLogic).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class MiniSemibotColorsRefreshPatch
{
    // Cache the colour-index array (so remote players' Mini-Semibot can be coloured from what the engine synced), then refresh the wearer's Mini-Semibot.
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, int[] _colors)
    {
        MiniSemibotOutfitCache.RecordColors(__instance, _colors);
        MiniSemibotSpawner.RefreshOutfit(__instance);
    }
}
