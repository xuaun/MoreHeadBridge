using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Event-based colour sync (rides the BridgeNetMux snapshot): unlike PhotonView RPCs, peers without this mod need no matching component/method, so vanilla/REPOLib-only clients log no "RPC method not found" errors.
internal static class PerCosmeticColorNetworkSync
{
    private sealed class RemoteColorCache
    {
        internal Dictionary<string, int>                              Colors          = new();
        internal Dictionary<string, Dictionary<int, int>>            SlotColors      = new();
        internal Dictionary<string, ColorAnimation>                   Animations      = new();
        internal Dictionary<string, UnityEngine.Color>                CustomColors    = new();
        internal Dictionary<string, Dictionary<int, UnityEngine.Color>> CustomSlotColors = new();
        internal Dictionary<string, Dictionary<int, ColorAnimation>> SlotAnimations  = new();
    }

    private static readonly Dictionary<int, RemoteColorCache> _remote = new();

    // Browse gate: open while the cosmetics menu is; closed at the vanilla confirm (menu exit). While open,
    // colour channels are omitted from snapshots and the mini broadcasts its committed outfit/colours only.
    internal static bool BrowseGateOpen { get; private set; }

    internal static void OpenGate() => BrowseGateOpen = true;

    // Closes the gate and commits the mini's pending broadcast state. True when it was open (caller re-broadcasts).
    internal static bool CloseGate()
    {
        if (!BrowseGateOpen) return false;
        BrowseGateOpen = false;
        MiniSemibotSpawner.CommitPendingBroadcast();
        return true;
    }

    internal static void PurgeActor(int actorNumber)
        => _remote.Remove(actorNumber);

    // Clears every player's colour cache — called on full disconnect (you left the session).
    internal static void PurgeAll()
    {
        _remote.Clear();
        BrowseGateOpen = false;
    }

    internal static void BroadcastAll()
    {
        if (!PerCosmeticColors.FeatureEnabled || !SemiFunc.IsMultiplayer()) return;
        BridgeNetMux.BroadcastSnapshot();
    }

    // The four colour-section builders registered in BridgeNetMux.Channels. Shared gating:
    // feature off → "" (explicit clear of the remote cache); browsing colours → null (section
    // omitted, remotes keep the last confirmed state until the menu confirm).
    private static bool TryGate(out string? gated)
    {
        if (!PerCosmeticColors.FeatureEnabled) { gated = ""; return true; }
        if (BrowseGateOpen) { gated = null; return true; }
        gated = null;
        return false;
    }

    // Palette index colours (whole-asset + per-slot).
    internal static string? BuildColorsSection()
    {
        if (TryGate(out var gated)) return gated;
        return PerCosmeticColorSerializer.SerializeWithSlots(
            PerCosmeticColors.GetAll(),
            PerCosmeticColors.GetAllSlots());
    }

    // Whole-asset animations.
    internal static string? BuildAnimationsSection()
    {
        if (TryGate(out var gated)) return gated;
        return Plugin.EnableBridgeColorAnimations.Value
            ? PerCosmeticColorSerializer.SerializeAnimations(PerCosmeticColors.GetAllAnimations())
            : "";
    }

    // Custom RGB colours (whole-asset + per-slot), filtered per sub-feature (see BuildSyncableCustom).
    internal static string? BuildCustomColorsSection()
    {
        if (TryGate(out var gated)) return gated;
        BuildSyncableCustom(out var customWhole, out var customSlots);
        return PerCosmeticColorSerializer.SerializeCustomColors(customWhole, customSlots);
    }

    // Per-slot animations.
    internal static string? BuildSlotAnimationsSection()
    {
        if (TryGate(out var gated)) return gated;
        return Plugin.EnableBridgeColorAnimations.Value
            ? PerCosmeticColorSerializer.SerializeSlotAnimations(
                  PerCosmeticColors.GetAllSlotAnimations())
            : "";
    }

    // Custom colours allowed to broadcast, gated per kind: base-mesh ("__base_N__") → EnableVanillaCustomColors,
    // else the asset's GetEffectiveCustomColors (same gate as the local apply).
    private static void BuildSyncableCustom(
        out Dictionary<string, UnityEngine.Color> whole,
        out Dictionary<string, Dictionary<int, UnityEngine.Color>> slots)
    {
        whole = new Dictionary<string, UnityEngine.Color>();
        slots = new Dictionary<string, Dictionary<int, UnityEngine.Color>>();

        var meta = MetaManager.instance;
        Dictionary<string, CosmeticAsset>? lookup = null;

        bool Allowed(string key)
        {
            if (VanillaTintHelper.IsBaseMeshId(key)) return Plugin.EnableVanillaCustomColors.Value;
            if (meta?.cosmeticAssets == null) return Plugin.EnableBridgeCustomColors.Value;
            if (lookup == null)
            {
                lookup = new Dictionary<string, CosmeticAsset>(meta.cosmeticAssets.Count);
                foreach (var a in meta.cosmeticAssets)
                    if (a != null && !string.IsNullOrEmpty(a.assetId)) lookup[a.assetId] = a;
            }
            return lookup.TryGetValue(key, out var asset)
                ? CustomizerStore.GetEffectiveCustomColors(asset)
                : Plugin.EnableBridgeCustomColors.Value;   // unknown id → treat as bridge
        }

        foreach (var kv in PerCosmeticColors.GetAllCustom())
            if (Allowed(kv.Key)) whole[kv.Key] = kv.Value;
        foreach (var kv in PerCosmeticColors.GetAllCustomSlots())
            if (Allowed(kv.Key)) slots[kv.Key] = kv.Value;
    }

    // The receive path must ONLY paint OTHER players' avatars (and remote minis). Painting the local avatar/menu (which happens when avatar PhotonViews resolve to the master actor) is the "for him, he has my colours" leak — locals are coloured from the LOCAL store.
    private static bool IsSyncReceiveTarget(PlayerCosmetics? pc)
        => pc != null
           && (!AvatarIdentity.IsLocalOrMenu(pc) || AvatarIdentity.IsRemoteMini(pc));

    internal static void ApplyCachedTo(PlayerCosmetics pc, PerCosmeticColorSyncComponent sync)
    {
        if (!IsSyncReceiveTarget(pc)) return;   // never paint our own avatar from a remote cache
        int actor = GetOwnerActor(pc);
        if (actor <= 0) return;
        // A remote RandomPreset mini owns its colours (preset payload); don't overwrite with the owner's live broadcast.
        if (AvatarIdentity.IsRemoteMini(pc) && MiniSemibotSync.RemoteMiniHasOwnColors(actor)) return;
        if (!_remote.TryGetValue(actor, out var cache)) return;

        sync.SetRemoteColors(cache.Colors, cache.SlotColors);
        sync.SetRemoteAnimations(cache.Animations);
        sync.SetRemoteCustomColors(cache.CustomColors, cache.CustomSlotColors);
        sync.SetRemoteSlotAnimations(cache.SlotAnimations);
        sync.ApplyToCosmetics(pc);
        sync.RefreshAnimators(pc);
    }

    // Populates <paramref name="sync"/> from an actor's cached maps WITHOUT applying — for a remote SameAsPlayer mini, whose owner can't be resolved from its PhotonView-less GO.
    internal static bool PopulateFromCachedActor(int actor, PerCosmeticColorSyncComponent sync)
    {
        if (actor <= 0 || !_remote.TryGetValue(actor, out var cache)) return false;
        sync.SetRemoteColors(cache.Colors, cache.SlotColors);
        sync.SetRemoteAnimations(cache.Animations);
        sync.SetRemoteCustomColors(cache.CustomColors, cache.CustomSlotColors);
        sync.SetRemoteSlotAnimations(cache.SlotAnimations);
        return true;
    }

    internal static void OnColorSection(int actor, string colorData)
    {
        if (!TryBeginReceive(actor, colorData, out var cache)) return;
        PerCosmeticColorSerializer.DeserializeWithSlots(colorData,
            out cache.Colors, out cache.SlotColors);
        ApplyCacheToActor(actor, cache);
    }

    internal static void OnAnimationSection(int actor, string animData)
    {
        if (!TryBeginReceive(actor, animData, out var cache)) return;
        cache.Animations = PerCosmeticColorSerializer.DeserializeAnimations(animData);
        ApplyCacheToActor(actor, cache);
    }

    internal static void OnCustomColorSection(int actor, string customData)
    {
        if (!TryBeginReceive(actor, customData, out var cache)) return;
        PerCosmeticColorSerializer.DeserializeCustomColors(customData,
            out cache.CustomColors, out cache.CustomSlotColors);
        ApplyCacheToActor(actor, cache);

        // A remote player's custom head colour changed — refresh lobby heads so theirs updates live (MenuPlayerHead only re-runs SetColor on a palette change, not a custom RGB one).
        LobbyHeadCustomColorPatch.RefreshAllHeads();
    }

    internal static void OnSlotAnimSection(int actor, string slotAnimData)
    {
        if (!TryBeginReceive(actor, slotAnimData, out var cache)) return;
        cache.SlotAnimations = PerCosmeticColorSerializer.DeserializeSlotAnimations(slotAnimData);
        ApplyCacheToActor(actor, cache);
    }

    // Pushes the actor's colour cache into each of their remote avatars and recolours them via SetupColors (its
    // postfix re-applies the cache). Then refreshes their mini's colours, which the Mini section can't carry.
    private static void ApplyCacheToActor(int actor, RemoteColorCache cache)
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (pc == null || GetOwnerActor(pc) != actor) continue;
            if (!IsSyncReceiveTarget(pc)) continue;
            // A remote RandomPreset mini owns its colours (preset payload); don't overwrite with the live broadcast.
            if (AvatarIdentity.IsRemoteMini(pc) && MiniSemibotSync.RemoteMiniHasOwnColors(actor)) continue;

            var sync = pc.GetComponent<PerCosmeticColorSyncComponent>();
            if (sync == null) continue;
            sync.SetRemoteColors(cache.Colors, cache.SlotColors);
            sync.SetRemoteAnimations(cache.Animations);
            sync.SetRemoteCustomColors(cache.CustomColors, cache.CustomSlotColors);
            sync.SetRemoteSlotAnimations(cache.SlotAnimations);

            // Recolour via vanilla (postfix re-applies the cache). Pass the remote's OWN palette — null falls back
            // to ours. Not ready yet → apply the bridge layer directly.
            if (pc.colorsEquipped != null)
                pc.SetupColors(_synced: false, pc.colorsEquipped);
            else
            {
                sync.ApplyToCosmetics(pc);
                sync.RefreshAnimators(pc);
            }
        }
        MiniSemibotSpawner.RefreshRemoteMiniColors(actor);
    }

    private const int MaxPayloadChars = 16 * 1024;   // anti-grief cap on inbound colour payloads

    // Shared guard for the four colour-section handlers: validates a sane payload from a real actor, then hands back that actor's cache. False drops the section.
    private static bool TryBeginReceive(int actor, string data, out RemoteColorCache cache)
    {
        cache = null!;
        if (data == null || data.Length > MaxPayloadChars || actor <= 0) return false;
        cache = GetOrCreate(actor);
        return true;
    }

    private static RemoteColorCache GetOrCreate(int actor)
    {
        if (!_remote.TryGetValue(actor, out var cache))
            _remote[actor] = cache = new RemoteColorCache();
        return cache;
    }

    private static int GetOwnerActor(PlayerCosmetics pc)
    {
        var view = pc.GetComponent<PhotonView>();
        if (view?.Owner != null) return view.Owner.ActorNumber;

        var avatarView = pc.playerAvatarVisuals?.playerAvatar?.photonView;
        if (avatarView?.Owner != null) return avatarView.Owner.ActorNumber;

        return 0;
    }
}
