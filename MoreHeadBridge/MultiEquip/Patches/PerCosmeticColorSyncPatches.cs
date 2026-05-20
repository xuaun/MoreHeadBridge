using HarmonyLib;
using Photon.Pun;

namespace MoreHeadBridge;

// Adds PerCosmeticColorSyncComponent to every PlayerCosmetics GameObject so the
// component is present for both the local player (sending RPC) and remote players
// (receiving RPC).
[HarmonyPatch(typeof(PlayerCosmetics), "Awake")]
internal static class PerCosmeticColorSyncAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => __instance.gameObject.AddComponent<PerCosmeticColorSyncComponent>();
}

[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmetics")]
internal static class SendPerCosmeticColorsSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, bool _synced)
    {
        if (!_synced) return;
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (!SemiFunc.IsMultiplayer()) return;
        if (__instance.photonView == null || !__instance.photonView.IsMine) return;

        var sync = __instance.GetComponent<PerCosmeticColorSyncComponent>();
        if (sync?.photonView == null) return;

        string colorData = PerCosmeticColorSerializer.Serialize(PerCosmeticColors.GetAll());

        PhotonNetwork.RemoveBufferedRPCs(sync.photonView.ViewID, "SyncPerCosmeticColorsRPC");
        sync.photonView.RPC("SyncPerCosmeticColorsRPC", RpcTarget.OthersBuffered, colorData);
    }
}

// Re-apply remote per-cosmetic color overrides after SetupColorsLogic rebuilds
// playerMaterials. Only targets remote in-game avatars — local player and menu avatar
// are handled by PerCosmeticColors.ApplyOverrides (SetupColorsLogicOverridePatch).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class ApplyRemoteColorsAfterSetupLogicPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => RemoteColorSync.Apply(__instance);
}

// Re-apply remote per-cosmetic color overrides after SetupColorsAllLogic
// (called when all cosmetic types share the same color index).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsAllLogic")]
internal static class ApplyRemoteColorsAfterSetupAllLogicPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => RemoteColorSync.Apply(__instance);
}

internal static class RemoteColorSync
{
    internal static void Apply(PlayerCosmetics pc)
    {
        var visuals = pc.playerAvatarVisuals;
        if (visuals == null || visuals.isMenuAvatar) return;
        if (visuals.playerAvatar?.isLocal == true) return;

        pc.GetComponent<PerCosmeticColorSyncComponent>()?.ApplyToCosmetics(pc);
    }
}
