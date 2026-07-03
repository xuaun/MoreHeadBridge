using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

internal static class WorldFollowPrefs
{
    private const FollowSpringMode DefaultMode = FollowSpringMode.Soft;

    private sealed class Data
    {
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Dictionary<string, FollowSpringMode> Springs { get; set; } = new(StringComparer.Ordinal);

        // Per-cosmetic "Show To Self": whether the LOCAL player also sees this world cosmetic on their own avatar in game (others always see it). Default false — matches MoreHead, which keeps its world node disabled for the local player.
        public Dictionary<string, bool> ShowToSelf { get; set; } = new(StringComparer.Ordinal);

        // Per-cosmetic "Avoid Walls": keep the cosmetic's visible body out of level geometry (walls, plus steps/ledges for ground-sitting cosmetics). Default false.
        public Dictionary<string, bool> AvoidWalls { get; set; } = new(StringComparer.Ordinal);

        // Per-cosmetic "Hide on Kart": hide the cosmetic while the wearer is seated on a vehicle (Semiscooter) or on the kart-arena level — same cramped clipping the Mini-Semibot avoids. Default false.
        public Dictionary<string, bool> HideOnKart { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly string SavePath = BridgePaths.Of("World.json");

    private static Data _data = new();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(SavePath))
                _data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(SavePath)) ?? new Data();
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"World prefs load failed: {ex.Message}");
            _data = new Data();
        }
        _data.Springs ??= new Dictionary<string, FollowSpringMode>(StringComparer.Ordinal);
        _data.ShowToSelf ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        _data.AvoidWalls ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        _data.HideOnKart ??= new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    internal static bool GetHideOnKart(string? assetId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(assetId)
               && _data.HideOnKart.TryGetValue(assetId!, out var on) && on;
    }

    internal static void SetHideOnKart(string? assetId, bool on)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return;
        if (GetHideOnKart(assetId) == on) return;
        _data.HideOnKart[assetId!] = on;
        Save();
    }

    internal static bool GetAvoidWalls(string? assetId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(assetId)
               && _data.AvoidWalls.TryGetValue(assetId!, out var on) && on;
    }

    internal static void SetAvoidWalls(string? assetId, bool on)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return;
        if (GetAvoidWalls(assetId) == on) return;
        _data.AvoidWalls[assetId!] = on;
        Save();
    }

    internal static bool GetShowToSelf(string? assetId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(assetId)
               && _data.ShowToSelf.TryGetValue(assetId!, out var show) && show;
    }

    internal static void SetShowToSelf(string? assetId, bool show)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return;
        if (GetShowToSelf(assetId) == show) return;
        _data.ShowToSelf[assetId!] = show;
        Save();
    }

    internal static FollowSpringMode GetSpring(string? assetId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return DefaultMode;
        return _data.Springs.TryGetValue(assetId!, out var mode) ? mode : DefaultMode;
    }

    internal static void SetSpring(string? assetId, FollowSpringMode mode)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(assetId)) return;
        if (_data.Springs.TryGetValue(assetId!, out var current) && current == mode) return;
        _data.Springs[assetId!] = mode;
        Save();
    }

    private static void Save()
    {
        try { AtomicJson.Write(SavePath, JsonConvert.SerializeObject(_data, Formatting.Indented)); }
        catch (Exception ex) { BceConsole.LogWarning($"World prefs save failed: {ex.Message}"); }
    }
}
