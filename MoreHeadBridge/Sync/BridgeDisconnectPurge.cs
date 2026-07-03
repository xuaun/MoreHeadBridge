using HarmonyLib;

namespace MoreHeadBridge;

// Purges every per-actor sync cache on disconnect (R.E.P.O. leaves via PhotonNetwork.Disconnect). Actor numbers
// are recycled per room, so a stale cache could surface a previous player's data on a reused actor.
[HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnDisconnected))]
internal static class BridgeDisconnectPurge
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        BridgeNetMux.PurgeAll();                 // dedupe + every registered channel cache
        MoreHeadCosmeticMountPatch.PurgeAll();   // refresh throttle — per-actor but not a sync channel
    }
}
