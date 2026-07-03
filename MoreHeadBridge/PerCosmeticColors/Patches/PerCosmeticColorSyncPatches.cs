using HarmonyLib;
using Photon.Pun;

namespace MoreHeadBridge;

// Adds PerCosmeticColorSyncComponent to every PlayerCosmetics GO — present for both the local sender and remote receivers.
[HarmonyPatch(typeof(PlayerCosmetics), "Awake")]
internal static class PerCosmeticColorSyncAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        var sync = __instance.gameObject.AddComponent<PerCosmeticColorSyncComponent>();
        PerCosmeticColorNetworkSync.ApplyCachedTo(__instance, sync);
    }
}

[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmetics")]
internal static class SendPerCosmeticColorsSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, bool _synced)
    {
        if (!_synced) return;
        if (!SemiFunc.IsMultiplayer()) return;
        if (__instance.photonView == null || !__instance.photonView.IsMine) return;

        // Vanilla confirm point (cosmetics-menu exit) — close the browse gate so committed colours go out now.
        bool gateWasOpen = PerCosmeticColorNetworkSync.CloseGate();
        if (PerCosmeticColors.FeatureEnabled)
            PerCosmeticColorNetworkSync.BroadcastAll();
        else if (gateWasOpen)
            BridgeNetMux.BroadcastSnapshot();   // feature off: still flush the mini's committed body colours
    }
}

// Vanilla / REPOLib-only peers only understand the per-type colour RPCs (one colour per type) — without help they render the first cosmetic of each type with whatever stale baseline sat in colorsEquipped[type]. SetupColors is the ONLY emitter of vanilla-compatible colour RPCs: right before it sends, provide a temporary payload aligned to each type's first compatible cosmetic. MetaManager.colorsEquipped must stay untouched (it's also menu/save state).
[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.SetupColors))]
internal static class AlignTypeColorsForSyncPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerCosmetics __instance, bool _synced, ref int[] _colors)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (!_synced || _colors != null) return; // only the normal outgoing sync path

        // Never the menu avatar; in multiplayer, only the local owner sends to remotes.
        var visuals = __instance.playerAvatarVisuals;
        if (visuals == null || visuals.isMenuAvatar) return;
        if (SemiFunc.IsMultiplayer() && visuals.playerAvatar?.isLocal != true) return;

        var syncColors = PerCosmeticColors.BuildCompatibilitySyncColors(MetaManager.instance);
        if (syncColors != null)
            _colors = syncColors;
    }
}

// Re-apply remote per-cosmetic overrides after SetupColorsLogic rebuilds playerMaterials. Remote in-game avatars only — local/menu are handled by ApplyOverrides.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class ApplyRemoteColorsAfterSetupLogicPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => RemoteColorSync.Apply(__instance);
}

// Re-apply remote per-cosmetic color overrides after SetupColorsAllLogic (called when all cosmetic types share the same color index).
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
        if (visuals == null) return;

        // A REMOTE player's Mini-Semibot would be skipped by the menu-avatar guard below, but its colours ARE the owner's synced ones — re-apply them after each SetupColorsLogic so they survive the vanilla rebuild.
        if (visuals.isMenuAvatar)
        {
            if (AvatarIdentity.IsRemoteMini(pc))
            {
                var miniSync = pc.GetComponent<PerCosmeticColorSyncComponent>();
                if (miniSync != null) { miniSync.ApplyToCosmetics(pc); miniSync.RefreshAnimators(pc); }
            }
            return;
        }
        if (visuals.playerAvatar?.isLocal == true) return;

        var sync = pc.GetComponent<PerCosmeticColorSyncComponent>();
        if (sync == null) return;
        sync.ApplyToCosmetics(pc);
        sync.RefreshAnimators(pc);
    }
}
