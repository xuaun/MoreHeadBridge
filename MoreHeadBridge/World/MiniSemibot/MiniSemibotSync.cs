// Owner-authoritative sync: Position, Size, Leg Speed, Holding, When You Die + the outfit (incl. rolled preset) follow the OWNER everywhere; other knobs stay local. Rides the BridgeNetMux snapshot (buffered per-sender, actor-keyed) like CustomizerSync. Cosmetics travel as asset IDs (cosmeticAssets ordering differs per client); colours as palette indices. Mod-less clients ignore it.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// The resolved, ready-to-use config for ONE mini (local or remote). Components read this every frame.
internal struct MiniSemibotConfig
{
    public MiniSemibotPosition Position;
    public float Scale;
    public MiniSemibotSize Size;   // the chosen size tier (death-state placement is tuned per tier)
    public float LegSpeed;
    public MiniSemibotGrabberVisual Grabber;
    public MiniSemibotDeathBehavior Death;
    public MiniSemibotGaze Gaze;
    public MiniSemibotMouthMode Mouth;
    public MiniSemibotBeamColor Beam;
    public bool AvoidWalls;   // owner-authoritative: the owner decides how THEIR mini handles geometry

    // MimicClips: resolved playback tuning (delay range / volume / 3D range).
    public float MimicMinDelay;
    public float MimicMaxDelay;
    public float MimicVol;
    public float MimicMaxDistance;

    // The local player's own live settings (used for our own mini, menu avatars, and singleplayer).
    public static MiniSemibotConfig Local()
    {
        var (mn, mx) = MiniSemibotSettings.MimicChatterDelay;
        return new()
        {
            Position = MiniSemibotSettings.Position,
            Scale    = MiniSemibotSettings.Scale,
            Size     = MiniSemibotVisualPrefs.Size,
            LegSpeed = MiniSemibotSettings.LegSpeedMultiplier,
            Grabber  = MiniSemibotSettings.GrabberVisual,
            Death    = MiniSemibotSettings.DeathBehavior,
            Gaze     = MiniSemibotSettings.Gaze,
            Mouth    = MiniSemibotSettings.MouthMode,
            Beam     = MiniSemibotSettings.BeamColor,
            AvoidWalls = MiniSemibotVisualPrefs.AvoidWalls,
            MimicMinDelay    = mn,
            MimicMaxDelay    = mx,
            MimicVol         = MiniSemibotSettings.MimicVolume,
            MimicMaxDistance = MiniSemibotSettings.MimicRange,
        };
    }
}

// Wire payload. Enums serialize by name (resilient to value reordering across mod versions).
internal sealed class MiniSemibotSyncPayload
{
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotPosition Position { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotSize Size { get; set; }
    public float LegSpeed { get; set; } = 1.4f;
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotGrabberVisual Grabber { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotDeathBehavior Death { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotGaze Gaze { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotMouthMode Mouth { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotBeamColor Beam { get; set; }
    public bool AvoidWalls { get; set; } = true;   // default matches the pref (older payloads → on)
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotMimicChatter MimicChatter { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotMimicVolume MimicVolume { get; set; }
    [JsonConverter(typeof(StringEnumConverter))] public MiniSemibotMimicRange MimicRange { get; set; }
    public string[]? Cosmetics { get; set; }   // assetIds — client-independent
    public int[]? Colors { get; set; }         // engine palette indices — consistent across clients

    // Per-cosmetic custom/animated colour transport (assetId-keyed). Only populated for RANDOM-PRESET minis — the preset's colours aren't in the owner's live broadcast; SameAsPlayer reuses the owner's synced live store.
    public string? ColorData { get; set; }     // PerCosmeticColorSerializer.SerializeWithSlots
    public string? AnimData { get; set; }      // PerCosmeticColorSerializer.SerializeAnimations
    public string? CustomData { get; set; }    // PerCosmeticColorSerializer.SerializeCustomColors
    public string? SlotAnimData { get; set; }  // PerCosmeticColorSerializer.SerializeSlotAnimations

    /// Clamps numeric fields, drops undefined enum values and caps array sizes — untrusted network
    /// input (mirrors BridgeSyncPayload.ClampValues). Called on every received payload before storing.
    internal void ClampValues()
    {
        const int MaxOutfit = 64;

        LegSpeed = Mathf.Clamp(LegSpeed, 1f, 3f);   // popup range 1.0x–3.0x

        Position     = DefinedOrDefault(Position);
        Size         = DefinedOrDefault(Size);
        Grabber      = DefinedOrDefault(Grabber);
        Death        = DefinedOrDefault(Death);
        Gaze         = DefinedOrDefault(Gaze);
        Mouth        = DefinedOrDefault(Mouth);
        Beam         = DefinedOrDefault(Beam);
        MimicChatter = DefinedOrDefault(MimicChatter);
        MimicVolume  = DefinedOrDefault(MimicVolume);
        MimicRange   = DefinedOrDefault(MimicRange);

        if (Cosmetics != null && Cosmetics.Length > MaxOutfit)
        {
            var t = new string[MaxOutfit];
            System.Array.Copy(Cosmetics, t, MaxOutfit);
            Cosmetics = t;
        }
        if (Colors != null && Colors.Length > MaxOutfit)
        {
            var t = new int[MaxOutfit];
            System.Array.Copy(Colors, t, MaxOutfit);
            Colors = t;
        }
    }

    // StringEnumConverter accepts raw integers, so an out-of-range value can arrive as a defined-looking enum.
    private static T DefinedOrDefault<T>(T value) where T : struct, System.Enum
        => System.Enum.IsDefined(typeof(T), value) ? value : default;

    public MiniSemibotConfig ToConfig()
    {
        var (mn, mx) = MiniSemibotSettings.ChatterToDelay(MimicChatter);
        return new()
        {
            Position = Position,
            Scale    = MiniSemibotSettings.ScaleForSize(Size),
            Size     = Size,
            LegSpeed = LegSpeed <= 0f ? 1f : LegSpeed,
            Grabber  = Grabber,
            Death    = Death,
            Gaze     = Gaze,
            Mouth    = Mouth,
            Beam     = Beam,
            AvoidWalls = AvoidWalls,
            MimicMinDelay    = mn,
            MimicMaxDelay    = mx,
            MimicVol         = MiniSemibotSettings.VolumeToValue(MimicVolume),
            MimicMaxDistance = MiniSemibotSettings.RangeToValue(MimicRange),
        };
    }
}

internal static class MiniSemibotSync
{
    // Photon actor number → that player's latest Mini-Semibot payload.
    private static readonly Dictionary<int, MiniSemibotSyncPayload> _remote = new();

    // Drops a left player's cached payload (called from BridgeRoomCallbacks).
    internal static void PurgeActor(int actorNumber) => _remote.Remove(actorNumber);

    // Clears every player's cached payload — called on full disconnect (you left the session).
    internal static void PurgeAll() => _remote.Clear();

    // Effective config for a mini whose wearer is `wearer`: local player / menu avatar / singleplayer → our own settings; remote → their synced payload, falling back to ours until it arrives.
    internal static MiniSemibotConfig Resolve(PlayerAvatar? wearer)
    {
        if (wearer == null || wearer.isLocal || !SemiFunc.IsMultiplayer())
            return MiniSemibotConfig.Local();
        int actor = ActorOf(wearer);
        if (actor >= 0 && _remote.TryGetValue(actor, out var p) && p != null)
            return p.ToConfig();
        return MiniSemibotConfig.Local();
    }

    // Remote outfit for an actor, mapped to THIS client's cosmetic indices. False when no synced payload yet (caller falls back to the engine-synced live outfit).
    internal static bool TryGetRemoteOutfit(int actor, out int[] cosmetics, out int[]? colors)
    {
        cosmetics = System.Array.Empty<int>();
        colors = null;
        if (actor < 0 || !_remote.TryGetValue(actor, out var p) || p == null || p.Cosmetics == null)
            return false;
        cosmetics = AssetIdsToIndices(p.Cosmetics);
        colors = p.Colors;
        return true;
    }

    internal static int ActorOf(PlayerAvatar? wearer)
        => wearer != null && wearer.photonView != null && wearer.photonView.Owner != null
            ? wearer.photonView.Owner.ActorNumber : -1;

    // True when actor's mini ships its OWN colour payload (RandomPreset) — the owner's live colour broadcast
    // must not paint it. SameAsPlayer minis carry none and mirror the owner.
    internal static bool RemoteMiniHasOwnColors(int actor)
        => _remote.TryGetValue(actor, out var p) && p != null
           && (!string.IsNullOrEmpty(p.ColorData) || !string.IsNullOrEmpty(p.CustomData)
               || !string.IsNullOrEmpty(p.AnimData) || !string.IsNullOrEmpty(p.SlotAnimData));

    // Applies a remote wearer's custom/animated colours onto their mini: populate its PerCosmeticColorSyncComponent and apply; RemoteColorSync re-applies on every later SetupColorsLogic.
    internal static void ApplyRemoteMiniColors(int actor, PlayerCosmetics miniPc)
    {
        if (miniPc == null || actor < 0) return;
        var sync = miniPc.GetComponent<PerCosmeticColorSyncComponent>();
        if (sync == null) return;

        bool fromPayload = false;
        if (RemoteMiniHasOwnColors(actor) && _remote.TryGetValue(actor, out var p) && p != null)
        {
            PerCosmeticColorSerializer.DeserializeWithSlots(p.ColorData ?? "", out var colors, out var slotColors);
            PerCosmeticColorSerializer.DeserializeCustomColors(p.CustomData ?? "", out var custom, out var customSlots);
            var anims = PerCosmeticColorSerializer.DeserializeAnimations(p.AnimData ?? "");
            var slotAnims = PerCosmeticColorSerializer.DeserializeSlotAnimations(p.SlotAnimData ?? "");
            sync.SetRemoteColors(colors, slotColors);
            sync.SetRemoteCustomColors(custom, customSlots);
            sync.SetRemoteAnimations(anims);
            sync.SetRemoteSlotAnimations(slotAnims);
            fromPayload = true;
        }
        else
        {
            // SameAsPlayer: the mini wears our outfit, so its colours are the owner's live store.
            fromPayload = PerCosmeticColorNetworkSync.PopulateFromCachedActor(actor, sync);
        }

        if (!fromPayload) return;
        sync.ApplyToCosmetics(miniPc);
        sync.RefreshAnimators(miniPc);
    }

    // Broadcasts the local player's current Mini-Semibot state to everyone (buffered for late joiners).
    internal static void BroadcastLocal() => BridgeNetMux.BroadcastSnapshot();

    // The "Mini" snapshot section: the local player's current Mini-Semibot state as JSON.
    internal static string BuildSection()
    {
        var payload = new MiniSemibotSyncPayload
        {
            Position = MiniSemibotSettings.Position,
            Size     = MiniSemibotVisualPrefs.Size,
            LegSpeed = MiniSemibotSettings.LegSpeedMultiplier,
            Grabber  = MiniSemibotSettings.GrabberVisual,
            Death    = MiniSemibotSettings.DeathBehavior,
            Gaze     = MiniSemibotSettings.Gaze,
            Mouth    = MiniSemibotSettings.MouthMode,
            Beam     = MiniSemibotSettings.BeamColor,
            AvoidWalls = MiniSemibotVisualPrefs.AvoidWalls,
            MimicChatter = MiniSemibotVisualPrefs.MimicChatter,
            MimicVolume  = MiniSemibotVisualPrefs.MimicVolume,
            MimicRange   = MiniSemibotVisualPrefs.MimicRange,
            Cosmetics = IndicesToAssetIds(MiniSemibotSpawner.LocalState.Cosmetics),
            Colors    = MiniSemibotSpawner.LocalState.Colors,
            ColorData    = MiniSemibotSpawner.LocalState.ColorData,
            AnimData     = MiniSemibotSpawner.LocalState.AnimData,
            CustomData   = MiniSemibotSpawner.LocalState.CustomData,
            SlotAnimData = MiniSemibotSpawner.LocalState.SlotAnimData,
        };

        return JsonConvert.SerializeObject(payload);
    }

    /// Called by BridgeNetMux for the "Mini" snapshot section (deduped per actor; mux shields exceptions).
    internal static void OnRemoteSection(int actor, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        if (json.Length > 32 * 1024) return;

        var payload = JsonConvert.DeserializeObject<MiniSemibotSyncPayload>(json);
        if (payload == null) return;
        payload.ClampValues();

        _remote[actor] = payload;
        // Re-dress / re-apply visuals on that player's already-spawned mini (config is read live, but the outfit + grabber mesh need an explicit refresh).
        MiniSemibotSpawner.OnRemoteSyncChanged(actor);
    }

    // ── assetId ↔ local index mapping ───────────────────────────────────────────
    private static string[]? IndicesToAssetIds(int[]? indices)
    {
        var meta = MetaManager.instance;
        if (indices == null || meta == null) return null;
        var list = new List<string>(indices.Length);
        foreach (int i in indices)
        {
            if (i < 0 || i >= meta.cosmeticAssets.Count) continue;
            var a = meta.cosmeticAssets[i];
            if (a != null && !string.IsNullOrEmpty(a.assetId)) list.Add(a.assetId);
        }
        return list.ToArray();
    }

    private static int[] AssetIdsToIndices(string[] assetIds)
    {
        var meta = MetaManager.instance;
        if (meta == null) return System.Array.Empty<int>();

        var lookup = new Dictionary<string, int>(meta.cosmeticAssets.Count);
        for (int i = 0; i < meta.cosmeticAssets.Count; i++)
        {
            var a = meta.cosmeticAssets[i];
            if (a != null && !string.IsNullOrEmpty(a.assetId)) lookup[a.assetId] = i;
        }

        var list = new List<int>(assetIds.Length);
        foreach (var id in assetIds)
            if (id != null && lookup.TryGetValue(id, out int idx)) list.Add(idx);
        return list.ToArray();
    }
}
