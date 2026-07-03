using System.Collections;
using UnityEngine;

namespace MoreHeadBridge;

internal static class RuntimeConfigApplier
{
    // The MoreHead buttons are built lazily across several menu frames, so a single Apply() can miss buttons that don't exist yet — re-apply on a fixed cadence for a few seconds to catch them all.
    private const int   HideMoreHeadRetries  = 12;
    private const float HideMoreHeadInterval = 0.25f;   // 12 × 0.25s ≈ 3s of coverage

    internal static void HideMoreHeadButtonsSoon()
    {
        if (Plugin.Instance != null)
            Plugin.Instance.StartCoroutine(HideMoreHeadButtonsRoutine());
        else
            HideMoreHeadUIPatch.Apply(Plugin.HideMoreHeadButton.Value);
    }

    private static IEnumerator HideMoreHeadButtonsRoutine()
    {
        for (int i = 0; i < HideMoreHeadRetries; i++)
        {
            HideMoreHeadUIPatch.Apply(Plugin.HideMoreHeadButton.Value);
            yield return new WaitForSecondsRealtime(HideMoreHeadInterval);
        }
    }

    internal static void RefreshCosmeticsMenu()
    {
        var page = CosmeticsMenuState.ActivePage ?? Object.FindObjectOfType<MenuPageCosmetics>(true);
        page?.RefreshScrollContent();
    }

    internal static void ReapplyLocalCosmeticColors()
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (!AvatarIdentity.IsLocalStyleTarget(pc)) continue;
            pc.SetupColors(_synced: false);
        }

        LobbyHeadCustomColorPatch.RefreshAllHeads();

        CustomGrabColorCompat.RefreshLocalBeam();

        MiniSemibotSpawner.OnLocalColorsChanged();
    }

    internal static void ReinstantiateAllLocalCosmetics()
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            // Local-style targets only: SetupCosmetics(_synced:false) reads OUR MetaManager loadout — a remote
            // player's mini is owner-authoritative (RefreshOutfit / ApplyRemoteMiniColors) and must be skipped.
            if (!AvatarIdentity.IsLocalStyleTarget(pc)) continue;
            pc.SetupCosmetics(_synced: false, _forced: true);
            pc.SetupColors(_synced: false);
        }
    }

    internal static void RefreshColorAnimations()
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
            ColorAnimatorRefresher.RefreshLiveAnimators(pc);

        // A live (un)animate toggle must rebuild our minis' static death-head clones too — else a mini already in death-head state keeps the pre-toggle animation baked in.
        MiniSemibotSpawner.InvalidateLocalDeathHeads();

        PerCosmeticColorNetworkSync.BroadcastAll();
    }

    internal static void RefreshRemoteColorAnimations()
    {
        foreach (var sync in Object.FindObjectsOfType<PerCosmeticColorSyncComponent>(includeInactive: true))
        {
            var pc = sync.GetComponent<PlayerCosmetics>();
            if (pc == null) continue;
            sync.RefreshAnimators(pc);
            // Toggle OFF strips animators mid-frame — re-apply the cached static colours (remote targets only; RemoteColorSync gates).
            RemoteColorSync.Apply(pc);
        }

        // Remote minis in death-head state bake the animation into the static model — rebuild them.
        MiniSemibotSpawner.InvalidateRemoteMiniDeathHeads();
    }

    internal static bool IsLivePaintTarget(PlayerCosmetics? pc)
        => pc != null
           && AvatarIdentity.IsLocalStyleTarget(pc)
           && !MiniSemibotSpawner.IsPresetMini(pc);

    // Custom-flow live-preview target: menu/preview avatars only — the in-game avatar and its world
    // mini apply at the cosmetics-menu confirm (vanilla timing).
    internal static bool IsMenuPreviewPaintTarget(PlayerCosmetics? pc)
    {
        if (!IsLivePaintTarget(pc)) return false;
        var visuals = pc!.playerAvatarVisuals;
        if (visuals != null && !visuals.isMenuAvatar && visuals.playerAvatar?.isLocal == true)
            return false;                                    // live in-game avatar
        return !MiniSemibotSpawner.IsLiveLocalMini(pc);      // world mini follows the same timing
    }

    // Menu-scoped ReapplyLocalCosmeticColors for the custom flows: in-game stays untouched until the menu confirm.
    internal static void ReapplyMenuCosmeticColors()
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (!IsMenuPreviewPaintTarget(pc)) continue;
            pc.SetupColors(_synced: false);
        }

        LobbyHeadCustomColorPatch.RefreshAllHeads();
    }
}
