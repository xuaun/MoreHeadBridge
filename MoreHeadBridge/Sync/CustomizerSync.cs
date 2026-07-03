// Multiplayer sync for per-cosmetic overrides: on save, broadcast via the BridgeNetMux snapshot (buffered per-sender → late joiners get it). Remote data keyed by actor, read by MoreHeadCosmeticMountPatch when spawning remote cosmetics. Mod-less clients ignore it.

using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CustomizerSync
{
    // Remote overrides: Photon actor number → assetId → slim sync payload.
    private static readonly Dictionary<int, Dictionary<string, BridgeSyncPayload>> _remote = new();

    /// Fired whenever remote override data changes for any actor (add, update, or remove).
    /// The int parameter is the Photon actor number that changed.
    /// Consumers (e.g. CosmeticSettingsPopup) subscribe while open and unsubscribe on close.
    internal static event Action<int>? OnRemoteDataChanged;

    // Cache pruning on player-leave lives in BridgeRoomCallbacks (patch on NetworkManager.OnPlayerLeftRoom). Do NOT register our own MonoBehaviourPunCallbacks here — at plugin startup it broke PhotonNetwork.Disconnect() and hung lobby hosting/joining.

    /// Drops all cached data for an actor that left the room.
    internal static void PurgeActor(int actorNumber)
    {
        bool hadData = _remote.Remove(actorNumber);
        _lastRefresh.Remove(actorNumber);
        _refreshPending.Remove(actorNumber);
        if (hadData) OnRemoteDataChanged?.Invoke(actorNumber);
    }

    /// Clears all remote override caches — called on full disconnect (you left the session).
    internal static void PurgeAll()
    {
        bool hadData = _remote.Count > 0;
        _remote.Clear();
        _lastRefresh.Clear();
        _refreshPending.Clear();
        // Notify any open Sync Customizer popup (handler ignores the actor; -1 = "roster changed").
        if (hadData) OnRemoteDataChanged?.Invoke(-1);
    }

    /// Broadcasts override data for EQUIPPED cosmetics to all clients (rides the buffered mux snapshot → late joiners get the latest). Called from SetAndApply (save) and BridgeSyncBroadcastPatch (room join / re-equip).
    internal static void BroadcastAll() => BridgeNetMux.BroadcastSnapshot();

    /// The "Overrides" snapshot section: override data for EQUIPPED cosmetics, "" when there is none (remote side clears).
    internal static string BuildSection()
    {
        // Only overrides for currently-equipped cosmetics — SetupCosmeticsLogic re-broadcasts on every loadout change, so the snapshot stays current.
        var allData = CustomizerStore.GetAllData();
        var equipped = GetEquippedAssetIds();
        var toSend = new Dictionary<string, BridgeSyncPayload>();
        bool tintingOff = !Plugin.EnableBridgeTinting.Value;
        foreach (var id in equipped)
        {
            BridgeSyncPayload? p = null;
            if (allData.TryGetValue(id, out var d))
                p = BridgeSyncPayload.FromOverrideData(d);

            // World "Avoid Walls" lives in WorldFollowPrefs (not the override store) but is owner-authoritative, so it rides along even when the cosmetic has no other overrides.
            if (WorldFollowPrefs.GetAvoidWalls(id))
            {
                p ??= new BridgeSyncPayload();
                p.AvoidWalls = true;
            }

            // World "Hide on Kart" — same WorldFollowPrefs origin, also owner-authoritative.
            if (WorldFollowPrefs.GetHideOnKart(id))
            {
                p ??= new BridgeSyncPayload();
                p.HideOnKart = true;
            }

            // EnableBridgeTinting OFF is owner-authoritative
            if (tintingOff && BridgeIds.IsBridgeAsset(id))
            {
                p ??= new BridgeSyncPayload();
                p.Tintable ??= false;
            }

            if (p != null) toSend[id] = p;
        }

        return toSend.Count == 0 ? "" : JsonConvert.SerializeObject(toSend);
    }

    private static HashSet<string> GetEquippedAssetIds()
    {
        var result = new HashSet<string>();
        var meta = MetaManager.instance;
        if (meta == null) return result;

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset != null) result.Add(asset.assetId);
        }
        return result;
    }

    /// Returns true and sets data if remote override exists for actorNumber + assetId.
    internal static bool TryGetRemote(int actorNumber, string assetId, out BridgeSyncPayload? data)
    {
        data = null;
        return _remote.TryGetValue(actorNumber, out var playerData)
            && playerData.TryGetValue(assetId, out data!);
    }

    /// One entry per OTHER player in the room (actor, name, override count, friend flag) — count 0 when they have none, so the Sync Customizer always shows the full roster.
    internal static List<(int actorNumber, string nickName, int overrideCount, bool isSteamFriend)> GetRemotePlayersWithData()
    {
        var result = new List<(int, string, int, bool)>();

        if (!SemiFunc.IsMultiplayer()) return result;

        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return result;

        foreach (var kvp in room.Players)
        {
            int actor = kvp.Key;
            var player = kvp.Value;
            if (player == null || player.IsLocal) continue; // skip ourselves

            int overrideCount = _remote.TryGetValue(actor, out var data) ? data.Count : 0;
            string nick = !string.IsNullOrEmpty(player.NickName) ? player.NickName : $"Player {actor}";
            bool isFriend = IsSteamFriend(GetSteamIdForActor(actor));
            result.Add((actor, nick, overrideCount, isFriend));
        }

        return result;
    }

    // Photon's player.UserId is a Photon GUID, NOT the SteamID — resolve the real 64-bit SteamID via the PlayerAvatar by actor number (this is what made the friend ★ never appear).
    private static string? GetSteamIdForActor(int actorNumber)
    {
        var players = GameDirector.instance?.PlayerList;
        if (players == null) return null;
        foreach (var avatar in players)
        {
            if (avatar == null) continue;
            var pv = avatar.photonView;
            if (pv != null && pv.Owner?.ActorNumber == actorNumber)
                return avatar.steamID;
        }
        return null;
    }

    private static bool IsSteamFriend(string? steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out ulong steamId)) return false;
        try
        {
            foreach (var friend in SteamFriends.GetFriends())
                if (friend.Id == steamId) return true;
        }
        catch { /* Steamworks not initialized */ }
        return false;
    }

    /// Returns all override data for a specific actor, or null if not cached.
    internal static Dictionary<string, BridgeSyncPayload>? GetRemotePlayerData(int actorNumber)
        => _remote.TryGetValue(actorNumber, out var data) ? data : null;

    /// Returns true if any currently-in-room player has broadcasted override data.
    internal static bool HasAnyRemoteData()
    {
        if (!SemiFunc.IsMultiplayer()) return false;
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return false;
        foreach (var kvp in _remote)
        {
            if (room.Players.ContainsKey(kvp.Key)) return true;
        }
        return false;
    }

    /// Called by BridgeNetMux for the "Overrides" snapshot section (deduped per actor; mux shields exceptions).
    internal static void OnRemoteSection(int actor, string json)
    {
        // Empty payload = the sender cleared their entry (no overridden cosmetics worn) — remove them from our local cache.
        if (string.IsNullOrEmpty(json))
        {
            if (_remote.Remove(actor))
            {
                RequestRemoteRefresh(actor);
                OnRemoteDataChanged?.Invoke(actor);
            }
            return;
        }

        const int MaxPayloadChars = 32 * 1024;
        if (json.Length > MaxPayloadChars)
        {
            BceConsole.LogWarning($"OverrideSync: actor={actor} payload too large ({json.Length} chars), ignoring");
            return;
        }

        var dict = JsonConvert.DeserializeObject<Dictionary<string, BridgeSyncPayload>>(json);
        if (dict == null)
        {
            BceConsole.LogWarning($"OverrideSync: actor={actor} — JSON deserialization returned null");
            return;
        }

        foreach (var payload in dict.Values)
            payload.ClampValues();

        _remote[actor] = dict;

        // Refresh any already-instantiated cosmetics for this remote player so offsets and custom-type broadcasters reflect the new data without re-equip.
        RequestRemoteRefresh(actor);
        OnRemoteDataChanged?.Invoke(actor);
    }

    // ── Anti-grief throttle for inbound refreshes ───────────────────────────────
    // Each event triggers a full _forced re-instantiation on every client — a buggy/malicious peer could spam for performance grief. Coalesce per actor: apply at most once per cooldown, bursts collapse into one trailing refresh with the latest data.
    private const float RefreshCooldownSeconds = 0.25f;
    private static readonly Dictionary<int, float> _lastRefresh = new();
    private static readonly HashSet<int> _refreshPending = new();

    private static void RequestRemoteRefresh(int actor)
    {
        float now = Time.realtimeSinceStartup;
        if (_lastRefresh.TryGetValue(actor, out float last) && now - last < RefreshCooldownSeconds)
        {
            // Too soon since the last refresh — schedule a single trailing one (if not already pending) so the final state still applies without re-instantiating on every event.
            if (_refreshPending.Add(actor) && Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(TrailingRefresh(actor));
            return;
        }

        _lastRefresh[actor] = now;
        MoreHeadCosmeticMountPatch.RefreshRemoteCosmetics(actor);
    }

    private static IEnumerator TrailingRefresh(int actor)
    {
        yield return new WaitForSeconds(RefreshCooldownSeconds);
        _refreshPending.Remove(actor);
        _lastRefresh[actor] = Time.realtimeSinceStartup;
        // Uses the latest data already stored in _remote (this method carries no stale snapshot).
        MoreHeadCosmeticMountPatch.RefreshRemoteCosmetics(actor);
    }
}
