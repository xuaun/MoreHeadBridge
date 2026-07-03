using HarmonyLib;

namespace MoreHeadBridge;

// Hooks the mux into Photon only at RunManager.Awake (first scene, long before any lobby) — subscribing at
// plugin load breaks lobby hosting. Mirrors REPOLib's RunManagerPatch, which defers NetworkingEvents.Initialize
// to this same hook for the same reason.
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Awake))]
internal static class BridgeNetMuxSubscribePatch
{
    [HarmonyPostfix]
    private static void Postfix() => BridgeNetMux.Subscribe();
}
