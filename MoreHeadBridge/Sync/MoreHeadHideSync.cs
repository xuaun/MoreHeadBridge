// Sync + apply for "hide my MoreHead-menu decorations" (Plugin.HideMoreHeadDecorations).
// Owner-authoritative like CustomizerSync: each player broadcasts their own flag, every client
// applies it to that player's avatar. Rides the buffered BridgeNetMux snapshot (late joiners included).

using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class MoreHeadHideSync
{
    private static readonly HashSet<int> _remoteHidden = new();   // actors whose owner has the flag on

    // "1" = hidden, "" = shown (explicit clear on the remote side).
    internal static string BuildSection() => Plugin.HideMoreHeadDecorations.Value ? "1" : "";

    internal static void OnRemoteSection(int actor, string body)
    {
        bool hide = body == "1";
        if (hide == _remoteHidden.Contains(actor)) return;
        if (hide) _remoteHidden.Add(actor); else _remoteHidden.Remove(actor);
        ApplyToActor(actor, hide);
    }

    internal static void PurgeActor(int actor)
    {
        if (_remoteHidden.Remove(actor)) ApplyToActor(actor, false);
    }

    // Clears every actor's hidden flag — called on full disconnect (remote avatars are gone).
    internal static void PurgeAll() => _remoteHidden.Clear();

    internal static bool IsActorHidden(int actor) => _remoteHidden.Contains(actor);

    /// Re-apply the local config to every local/menu avatar, then broadcast so others match.
    internal static void ApplyLocalAndBroadcast()
    {
        bool hide = Plugin.HideMoreHeadDecorations.Value;
        int applied = 0;
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
            if (AvatarIdentity.IsLocalOrMenu(pc)) { SetHider(pc, hide); applied++; }

        BridgeLog.Debug($"HideMoreHeadDecorations={hide} applied to {applied} local/menu avatar(s)");
        CustomizerSync.BroadcastAll();
    }

    private static void ApplyToActor(int actor, bool hide)
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
            if (pc.photonView?.Owner != null && pc.photonView.Owner.ActorNumber == actor)
                SetHider(pc, hide);
    }

    internal static void SetHider(PlayerCosmetics pc, bool hide)
    {
        var visuals = pc.playerAvatarVisuals;
        if (visuals == null) return;

        var hider = visuals.GetComponent<MoreHeadDecorationHider>();
        if (hide)
            (hider ?? visuals.gameObject.AddComponent<MoreHeadDecorationHider>()).Init(visuals.transform);
        else if (hider != null)
            Object.Destroy(hider);
    }
}

// Re-applies on every cosmetics (re)build: local/menu avatars follow the local config, remote avatars
// follow their owner's last synced flag — covers respawns, late joiners, and snapshots that arrived
// before the avatar existed.
[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.SetupCosmeticsLogic))]
internal static class MoreHeadHideApplyPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        if (__instance.playerAvatarVisuals == null) return;

        if (AvatarIdentity.IsLocalOrMenu(__instance))
        {
            MoreHeadHideSync.SetHider(__instance, Plugin.HideMoreHeadDecorations.Value);
            return;
        }

        int actor = __instance.photonView?.Owner?.ActorNumber ?? -1;
        if (actor > 0)
            MoreHeadHideSync.SetHider(__instance, MoreHeadHideSync.IsActorHidden(actor));
    }
}
