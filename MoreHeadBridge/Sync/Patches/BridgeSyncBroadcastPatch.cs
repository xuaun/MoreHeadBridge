using HarmonyLib;

namespace MoreHeadBridge;

// Broadcasts local overrides whenever the local player's cosmetics are (re)set up — also fires on joining a populated room, so our overrides reach players even without a save since joining.
[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.SetupCosmeticsLogic))]
internal static class BridgeSyncBroadcastPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        if (!SemiFunc.IsMultiplayer()) return;
        if (__instance.photonView == null || !__instance.photonView.IsMine) return;
        CustomizerSync.BroadcastAll();
    }
}
