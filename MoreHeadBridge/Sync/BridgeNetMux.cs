// Single fixed-code Photon event carrying ALL MoreHeadBridge sync channels (overrides, mini-semibot, colours, mimic audio).
// Why not REPOLib NetworkedEvent per channel: REPOLib codes go by REGISTRATION ORDER and omit the name, so a mod-list/version mismatch misroutes payloads. A fixed code + magic/channel envelope routes by NAME — foreign payloads silently dropped.
// Stateful channels ride one buffered room-cache snapshot per sender (RemoveFromRoomCache filters by code+sender); receivers dedupe per actor+channel.

using ExitGames.Client.Photon;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;

namespace MoreHeadBridge;

internal static class BridgeNetMux
{
    // Custom Photon codes are 0..199 (200+ PUN internals); REPOLib uses 3+, vanilla 0..2. A high fixed code stays clear; the Magic check makes any collision a no-op.
    private const byte EventCode = 187;
    private const string Magic = "MHB1";

    // Channel names — part of the wire protocol, never rename.
    internal const string ChOverrides  = "Overrides";
    internal const string ChMini       = "Mini";
    internal const string ChColors     = "Colors";
    internal const string ChAnims      = "Anims";
    internal const string ChCustom     = "Custom";
    internal const string ChSlotAnims  = "SlotAnims";
    internal const string ChMimicAudio = "MimicAudio";
    internal const string ChHideMH     = "HideMH";
    private  const string ChSnapshot   = "Snap";

    // One row per stateful snapshot channel — the loops below handle send, dispatch and purge, so
    // adding a channel means adding exactly one row here. Build contract: null = omit the section
    // (remotes keep their last state), "" = explicit clear, else the payload.
    // ChMimicAudio is transient (SendTransient, never cached) and stays outside the table.
    private sealed class Channel
    {
        internal readonly string Name;
        internal readonly System.Func<string?> Build;
        internal readonly System.Action<int, string> OnRemote;
        internal readonly System.Action<int>? PurgeActor;
        internal readonly System.Action? PurgeAll;

        internal Channel(string name, System.Func<string?> build, System.Action<int, string> onRemote,
                         System.Action<int>? purgeActor = null, System.Action? purgeAll = null)
        { Name = name; Build = build; OnRemote = onRemote; PurgeActor = purgeActor; PurgeAll = purgeAll; }
    }

    private static readonly Channel[] Channels =
    {
        new(ChOverrides, CustomizerSync.BuildSection, CustomizerSync.OnRemoteSection,
            CustomizerSync.PurgeActor, CustomizerSync.PurgeAll),
        new(ChMini, MiniSemibotSync.BuildSection, MiniSemibotSync.OnRemoteSection,
            MiniSemibotSync.PurgeActor, MiniSemibotSync.PurgeAll),
        new(ChHideMH, MoreHeadHideSync.BuildSection, MoreHeadHideSync.OnRemoteSection,
            MoreHeadHideSync.PurgeActor, MoreHeadHideSync.PurgeAll),
        // The four colour channels share one remote cache — its purge is registered once, on ChColors.
        new(ChColors, PerCosmeticColorNetworkSync.BuildColorsSection, PerCosmeticColorNetworkSync.OnColorSection,
            PerCosmeticColorNetworkSync.PurgeActor, PerCosmeticColorNetworkSync.PurgeAll),
        new(ChAnims, PerCosmeticColorNetworkSync.BuildAnimationsSection, PerCosmeticColorNetworkSync.OnAnimationSection),
        new(ChCustom, PerCosmeticColorNetworkSync.BuildCustomColorsSection, PerCosmeticColorNetworkSync.OnCustomColorSection),
        new(ChSlotAnims, PerCosmeticColorNetworkSync.BuildSlotAnimationsSection, PerCosmeticColorNetworkSync.OnSlotAnimSection),
    };

    private const int MaxSnapshotChars = 128 * 1024;   // anti-grief cap on the combined inbound snapshot

    // Last section body applied per actor+channel — dedupes the repeated full-snapshot sends.
    private static readonly Dictionary<int, Dictionary<string, string>> _lastBodies = new();

    private static bool _subscribed;

    /// Called from BridgeNetMuxSubscribePatch at RunManager.Awake — touching PhotonNetwork at plugin load
    /// initializes Photon too early and breaks lobby hosting (same startup gotcha as MonoBehaviourPunCallbacks);
    /// REPOLib defers its own EventReceived subscription to this exact hook.
    internal static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
        UnityEngine.Application.quitting += () => PhotonNetwork.NetworkingClient.EventReceived -= OnEvent;
    }

    /// Drops the dedupe state AND every registered channel's cache for an actor that left
    /// (called from BridgeRoomCallbacks).
    internal static void PurgeActor(int actorNumber)
    {
        _lastBodies.Remove(actorNumber);
        foreach (var ch in Channels) ch.PurgeActor?.Invoke(actorNumber);
    }

    /// Clears all dedupe state and every channel cache — called on full disconnect (you left the session).
    internal static void PurgeAll()
    {
        _lastBodies.Clear();
        foreach (var ch in Channels) ch.PurgeAll?.Invoke();
    }

    // ── Send ─────────────────────────────────────────────────────────────────────

    /// Broadcasts the full local state (all stateful channels) and replaces this sender's single
    /// buffered room-cache entry, so late joiners get everything in one atomic event.
    internal static void BroadcastSnapshot()
    {
        if (!SemiFunc.IsMultiplayer()) return;

        var snap = new Dictionary<string, string>();
        foreach (var ch in Channels)
        {
            string? body = ch.Build();
            if (body != null) snap[ch.Name] = body;
        }

        string json = JsonConvert.SerializeObject(snap);

        // Atomic replace: drop our previous buffered snapshot, then buffer the new one so late joiners get the latest.
        ClearMyBufferedSnapshot();
        SendBufferedSnapshot(json);
    }

    // Removes THIS sender's buffered snapshot from the room cache. Non-Hashtable content matches by code+sender only, so it clears server-side without delivering anything to clients.
    private static void ClearMyBufferedSnapshot()
        => PhotonNetwork.RaiseEvent(EventCode, "",
            new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache },
            SendOptions.SendReliable);

    // Sends the snapshot to other clients AND buffers it in the room cache so late joiners receive it.
    private static void SendBufferedSnapshot(string json)
        => PhotonNetwork.RaiseEvent(EventCode, new object[] { Magic, ChSnapshot, json },
            new RaiseEventOptions { Receivers = ReceiverGroup.Others, CachingOption = EventCaching.AddToRoomCache },
            SendOptions.SendReliable);

    /// Fire-and-forget channel message (never cached) — used for mimic-audio chunks.
    internal static void SendTransient(string channel, object body)
    {
        if (!SemiFunc.IsMultiplayer()) return;
        PhotonNetwork.RaiseEvent(EventCode, new object[] { Magic, channel, body },
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);
    }

    // ── Receive ──────────────────────────────────────────────────────────────────

    private static void OnEvent(EventData ev)
    {
        if (ev.Code != EventCode) return;
        int actor = ev.Sender;
        if (actor <= 0) return;

        // Strict envelope check — anything that isn't ours (another mod on the same code) is dropped silently.
        if (ev.CustomData is not object[] env || env.Length != 3) return;
        if (env[0] is not string magic || magic != Magic) return;
        if (env[1] is not string channel) return;

        // A malformed/foreign payload must never escape into Photon's dispatch loop (the old per-event receivers spammed AggregateException every dispatch).
        try
        {
            if (channel == ChSnapshot)
            {
                if (env[2] is not string json || json.Length > MaxSnapshotChars) return;
                OnSnapshot(actor, json);
            }
            else if (channel == ChMimicAudio)
            {
                MiniSemibotMimicAudio.OnChunk(actor, env[2]);
            }
            // Unknown channel: a newer mod version on the sender — ignore.
        }
        catch (System.Exception ex)
        {
            BridgeLog.Debug($"BridgeNetMux: dropped bad '{channel}' payload from actor {actor} — {ex.Message}");
        }
    }

    private static void OnSnapshot(int actor, string json)
    {
        var snap = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        if (snap == null) return;

        // Absent section = "owner didn't broadcast this channel" (e.g. colour feature off) → keep last state. "" = explicit clear.
        var last = GetOrCreateLast(actor);
        foreach (var kvp in snap)
        {
            string body = kvp.Value ?? "";
            if (last.TryGetValue(kvp.Key, out var prev) && prev == body) continue;
            // Mark "seen" only AFTER a successful dispatch and isolate each section: a throwing handler must
            // neither be deduped (an identical resend must still apply) nor abort the remaining sections.
            try
            {
                DispatchSection(actor, kvp.Key, body);
                last[kvp.Key] = body;
            }
            catch (System.Exception ex)
            {
                BridgeLog.Debug($"BridgeNetMux: section '{kvp.Key}' from actor {actor} failed — {ex.Message}");
            }
        }
    }

    private static void DispatchSection(int actor, string channel, string body)
    {
        foreach (var ch in Channels)
        {
            if (ch.Name != channel) continue;
            ch.OnRemote(actor, body);
            return;
        }
        // Unknown section: newer sender version — ignore.
    }

    private static Dictionary<string, string> GetOrCreateLast(int actor)
    {
        if (!_lastBodies.TryGetValue(actor, out var last))
            _lastBodies[actor] = last = new Dictionary<string, string>();
        return last;
    }
}
