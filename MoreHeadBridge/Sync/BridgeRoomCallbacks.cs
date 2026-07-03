using HarmonyLib;
using Photon.Realtime;

namespace MoreHeadBridge;

// Prunes per-actor caches on player-leave — otherwise actor-keyed dictionaries leak all session, and a reused actor number could surface a previous player's overrides on the wrong avatar.
//
// Postfix the GAME's NetworkManager.OnPlayerLeftRoom, NOT our own MonoBehaviourPunCallbacks: registering one at BepInEx load broke PhotonNetwork.Disconnect() (never reached Disconnected), hanging CreateLobby on an eternal loading screen.
// NetworkManager is itself registered only when entering multiplayer, so patching its hook is safe.
[HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom))]
internal static class BridgeRoomCallbacks
{
    [HarmonyPostfix]
    private static void Postfix(Player otherPlayer)
    {
        if (otherPlayer == null) return;
        int actor = otherPlayer.ActorNumber;
        BridgeNetMux.PurgeActor(actor);                // dedupe + every registered channel cache
        MoreHeadCosmeticMountPatch.PurgeActor(actor);  // refresh throttle — per-actor but not a sync channel
    }
}
