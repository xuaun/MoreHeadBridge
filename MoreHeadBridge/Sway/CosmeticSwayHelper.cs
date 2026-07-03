using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Shared helper for CosmeticSwayVanillaPatches/CosmeticSwayBridgePatches, also used directly by BridgeSwaySpring to resolve per-cosmetic sway settings.
internal static class CosmeticSwayHelper
{
    private static readonly HashSet<SemiFunc.CosmeticType> _allTypes = new(SemiFunc.CosmeticGetTypes());

    internal static bool ShouldSuppressSway(Cosmetic? cosmetic)
    {
        if (cosmetic == null || !BridgeIds.IsBridgeAsset(cosmetic.cosmeticAsset)) return false;
        return GetEffectiveSway(cosmetic) == SwayMode.None;
    }

    private static SwayMode? GetEffectiveSway(Cosmetic cosmetic)
        => ResolveSway(cosmetic.playerCosmetics, cosmetic.cosmeticAsset?.assetId);

    // Remote PC (incl. a remote player's mini) → ONLY the owner's broadcast data: no payload means "no override", never the viewer's local store — else local settings leak onto other players.
    private static SwayMode? ResolveSway(PlayerCosmetics? playerCosmetics, string? assetId)
    {
        if (assetId == null) return null;

        if (TryGetRemoteActor(playerCosmetics, out int actorNumber))
        {
            CustomizerSync.TryGetRemote(actorNumber, assetId, out var remoteData);
            return remoteData?.EnableSway;
        }

        return CustomizerStore.GetEffectiveSway(assetId);
    }

    internal static bool IsSwayEnabled(PlayerCosmetics playerCosmetics, string? assetId)
        => ResolveSway(playerCosmetics, assetId) is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong;

    internal static float GetIntensityFactor(PlayerCosmetics playerCosmetics, string? assetId)
        => SwayModeToFactor(ResolveSway(playerCosmetics, assetId));

    internal static float SwayModeToFactor(SwayMode? mode) => mode switch
    {
        SwayMode.Light  => 0.35f,
        SwayMode.Strong => 2.2f,
        _               => 1.0f,
    };

    internal static bool ShouldSuppressSway(CosmeticSprings? springs)
    {
        if (springs == null) return false;
        var cosmetic = springs.cosmetic;
        if (cosmetic == null)
            cosmetic = springs.GetComponentInParent<Cosmetic>();
        return ShouldSuppressSway(cosmetic);
    }

    private static bool TryGetRemoteActor(PlayerCosmetics? instance, out int actorNumber)
    {
        actorNumber = -1;
        if (instance == null || !SemiFunc.IsMultiplayer()) return false;

        // A remote wearer's mini is a menu-avatar clone that can carry a leftover, owner-null PhotonView (NOT IsMine) — that would hit the photonView branch and resolve actor=-1. Resolve the mini by its follow-hierarchy FIRST so its sway stays owner-authoritative.
        actorNumber = MiniSemibotSpawner.RemoteMiniActorOf(instance);
        if (actorNumber > 0) return true;

        var photonView = instance.deathHead && instance.deathHead.setup
                      && instance.deathHead.playerAvatar
            ? instance.deathHead.playerAvatar.photonView
            : instance.photonView;

        if (photonView == null || photonView.IsMine) return false;

        actorNumber = photonView.Owner?.ActorNumber ?? -1;
        return actorNumber > 0;
    }

    // Wearer's event kicks (jump/land/crouch/steps) forwarded to their Mini-Semibot at reduced force — the mini mirrors the animation but its PC never receives the vanilla CosmeticSpring* calls. Tunables: clamp range of the size-based force factor.
    private const float MiniImpulseScaleMin = 0.25f;
    private const float MiniImpulseScaleMax = 1f;

    internal static void ImpulseBridgeSprings(
        PlayerCosmetics playerCosmetics,
        float force,
        CosmeticSprings.CosmeticSpring.JumpDirection direction,
        params SemiFunc.CosmeticType[] affectedTypes)
    {
        ImpulseCore(playerCosmetics, force, direction, affectedTypes);

        var mini = MiniSemibotSpawner.ActiveMiniOf(playerCosmetics);
        if (mini != null)
        {
            float factor = Mathf.Clamp(mini.Value.scale, MiniImpulseScaleMin, MiniImpulseScaleMax);
            ImpulseCore(mini.Value.pc, force * factor, direction, affectedTypes);
        }
    }

    private static void ImpulseCore(
        PlayerCosmetics playerCosmetics,
        float force,
        CosmeticSprings.CosmeticSpring.JumpDirection direction,
        SemiFunc.CosmeticType[] affectedTypes)
    {
        var allowedTypes = affectedTypes.Length > 0
            ? new HashSet<SemiFunc.CosmeticType>(affectedTypes)
            : _allTypes;

        foreach (var cosmetic in playerCosmetics.cosmeticEquipped)
        {
            if (cosmetic == null || !allowedTypes.Contains(cosmetic.type)) continue;
            // Bridge cosmetics + modded cosmetics with an injected BridgeSwaySpring. IsCustomizable gates both: IsBridgeAsset OR (AllowModdedOverrides && IsModded).
            if (!BridgeIds.IsCustomizable(cosmetic.cosmeticAsset)) continue;

            var resolvedDirection = affectedTypes.Length > 0
                ? direction
                : BridgeSwaySpring.DefaultDirection(cosmetic.type);

            foreach (var spring in cosmetic.GetComponentsInChildren<BridgeSwaySpring>(includeInactive: true))
                spring.Impulse(force, resolvedDirection);
        }
    }
}
