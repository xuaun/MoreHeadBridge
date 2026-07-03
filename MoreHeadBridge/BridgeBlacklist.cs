using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MoreHeadBridge;

// What the bridge does with a blacklisted cosmetic at load time.
public enum BlacklistLoadMode
{
    NotLoadIngame,    // skip registration entirely (saves memory; matches MoreHead)
    LoadOnHiddenMenu, // register, but start it hidden in the menu (shows in the HIDE tab)
}

// Bridge's own cosmetic blacklist, keyed by assetId with the displayName kept for MoreHead interop.
// Seeded once from MoreHeadBlacklist.json when this file is absent. With the mirror on, pushes its names
// into MoreHead's blacklist (merge-union); the ledger tracks what the bridge wrote so it owns only its own.
internal static class BridgeBlacklist
{
    private sealed class Entry
    {
        public string AssetId { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private sealed class SaveData
    {
        public List<Entry> Entries { get; set; } = new();
        public List<string> MirroredNames { get; set; } = new();
    }

    private sealed class MoreHeadBlacklistData
    {
        public List<string> DecorationNames { get; set; } = new();
    }

    private static readonly Dictionary<string, string> _byAssetId = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _mirrored = new(StringComparer.Ordinal);
    private static readonly string SavePath = BridgePaths.Of("Blacklist.json");
    private static readonly string MoreHeadPath = Path.Combine(BepInEx.Paths.ConfigPath, "MoreHeadBlacklist.json");

    private static bool _loaded;

    // False on the first run (no file yet) — the loader uses it to seed from MoreHead.
    internal static bool ExistedAtStartup { get; private set; }

    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        ExistedAtStartup = File.Exists(SavePath);
        if (!ExistedAtStartup) return;
        try
        {
            var data = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;
            foreach (var e in data.Entries ?? new())
                if (!string.IsNullOrEmpty(e.AssetId)) _byAssetId[e.AssetId] = e.DisplayName ?? "";
            foreach (var n in data.MirroredNames ?? new()) _mirrored.Add(n);
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: load failed: {ex.Message}"); }
    }

    internal static bool Contains(string assetId) => _byAssetId.ContainsKey(assetId);

    internal static void Add(string assetId, string displayName)
    {
        if (!string.IsNullOrEmpty(assetId)) _byAssetId[assetId] = displayName ?? "";
    }

    // Runtime toggle from the customizer (load-time effect). Keeps MoreHead's file in sync when mirroring.
    internal static void SetBlacklisted(string assetId, string? displayName, bool blacklisted)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return;

        if (blacklisted)
        {
            if (_byAssetId.ContainsKey(assetId)) return;
            _byAssetId[assetId] = displayName ?? "";
            Save();
            if (Plugin.MirrorBlacklistToMoreHead.Value) MirrorAdd(displayName);
        }
        else
        {
            if (!_byAssetId.TryGetValue(assetId, out var name)) return;
            _byAssetId.Remove(assetId);
            Save();
            if (Plugin.MirrorBlacklistToMoreHead.Value) MirrorRemove(name);
        }
    }

    // The display names MoreHead would key on (for seeding match + mirror writeback).
    internal static HashSet<string> ReadMoreHeadNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(MoreHeadPath)) return set;
            var data = JsonConvert.DeserializeObject<MoreHeadBlacklistData>(File.ReadAllText(MoreHeadPath));
            foreach (var n in data?.DecorationNames ?? new()) set.Add(n);
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: reading MoreHead blacklist failed: {ex.Message}"); }
        return set;
    }

    internal static void Save()
    {
        try
        {
            Directory.CreateDirectory(BridgePaths.DataDir);
            var data = new SaveData
            {
                Entries = _byAssetId.Select(kv => new Entry { AssetId = kv.Key, DisplayName = kv.Value }).ToList(),
                MirroredNames = _mirrored.ToList(),
            };
            AtomicJson.Write(SavePath, JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: save failed: {ex.Message}"); }
    }

    // Union the bridge's blacklisted names into MoreHead's file (never removing the user's own entries),
    // recording what we added in the ledger so a future un-blacklist can remove only ours. Self-gated.
    internal static void MirrorToMoreHead()
    {
        if (!Plugin.MirrorBlacklistToMoreHead.Value) return;
        try
        {
            var names = ReadMoreHeadNames();
            bool changed = false;
            foreach (var name in _byAssetId.Values.Where(n => !string.IsNullOrEmpty(n)))
                if (names.Add(name)) { _mirrored.Add(name); changed = true; }

            if (changed) { WriteMoreHead(names); Save(); }
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: mirror failed: {ex.Message}"); }
    }

    // Add one name to MoreHead's file (claiming ownership in the ledger).
    private static void MirrorAdd(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var names = ReadMoreHeadNames();
            if (names.Add(name!)) { _mirrored.Add(name!); WriteMoreHead(names); Save(); }
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: mirror add failed: {ex.Message}"); }
    }

    // Remove one name from MoreHead's file — only if we own it (it's in the ledger).
    private static void MirrorRemove(string? name)
    {
        if (string.IsNullOrEmpty(name) || !_mirrored.Contains(name!)) return;
        try
        {
            var names = ReadMoreHeadNames();
            if (names.Remove(name!)) WriteMoreHead(names);
            _mirrored.Remove(name!);
            Save();
        }
        catch (Exception ex) { BceConsole.LogWarning($"BridgeBlacklist: mirror remove failed: {ex.Message}"); }
    }

    private static void WriteMoreHead(HashSet<string> names)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MoreHeadPath)!);
        File.WriteAllText(MoreHeadPath,
            JsonConvert.SerializeObject(new MoreHeadBlacklistData { DecorationNames = names.ToList() },
                Formatting.Indented));
    }
}
