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
        RecolorMiniOfLiveLocalWearer(__instance);
    }

    // LIVE local wearer recoloured → refresh what hangs off their colours (world mini + CustomGrabColor beam).
    internal static void RecolorMiniOfLiveLocalWearer(PlayerCosmetics pc)
    {
        if (pc == null) return;
        var visuals = pc.playerAvatarVisuals;
        if (visuals == null || visuals.isMenuAvatar) return;
        if (visuals.playerAvatar == null || !visuals.playerAvatar.isLocal) return;
        MiniSemibotSpawner.RecolorLocalMini(pc);
        CustomGrabColorCompat.RefreshLocalBeam();
    }
}

// Same trigger for SetupColorsAllLogic (SetupColors collapses to it when all types share one colour).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsAllLogic")]
internal static class MiniSemibotColorsAllRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => MiniSemibotColorsRefreshPatch.RecolorMiniOfLiveLocalWearer(__instance);
}
