// ============================================================================
// Persistent store for the player's FAVORITES and HIDDEN cosmetic lists, written to config\MoreHeadBridge\Favorites.json. Each entry is keyed by assetId (preferred), assetName, or name as a fallback, matching the priority used everywhere else in this mod. Mutual exclusion: favoriting removes from hidden and vice versa.
// ============================================================================

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MoreHeadBridge;

internal static class BridgeFavoritesManager
{
    private static readonly HashSet<string> _favorites = new();
    private static readonly HashSet<string> _hidden    = new();
    private static bool _loaded;

    // Computed once at class-init time; BepInEx.Paths is already populated by then.
    private static readonly string SavePath = BridgePaths.Of("Favorites.json");

    // Call this before any read/write — safe to call multiple times.
    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Load();
    }

    // ── State queries ─────────────────────────────────────────────────────────

    internal static bool IsFavorite(CosmeticAsset? asset) =>
        asset != null && _favorites.Contains(KeyFor(asset));

    internal static bool IsHidden(CosmeticAsset? asset) =>
        asset != null && _hidden.Contains(KeyFor(asset));

    internal static bool HasAnyFavorite() => _favorites.Count > 0;
    internal static bool HasAnyHidden()   => _hidden.Count > 0;

    // ── Toggles ───────────────────────────────────────────────────────────────

    /// Toggles favorite state. Returns true if the asset is now a favorite.
    internal static bool ToggleFavorite(CosmeticAsset asset)
    {
        string key = KeyFor(asset);
        if (_favorites.Remove(key)) { Save(); return false; }

        _hidden.Remove(key);   // mutual exclusion
        _favorites.Add(key);
        Save();
        return true;
    }

    /// Toggles hidden state. Returns true if the asset is now hidden.
    internal static bool ToggleHidden(CosmeticAsset asset)
    {
        string key = KeyFor(asset);
        if (_hidden.Remove(key)) { Save(); return false; }

        _favorites.Remove(key);   // mutual exclusion
        _hidden.Add(key);
        Save();
        return true;
    }

    /// Marks an asset hidden without toggling (idempotent). Used to start a freshly-blacklisted
    /// LoadHidden cosmetic hidden; the user can un-hide it afterwards like any other.
    internal static void EnsureHidden(CosmeticAsset asset)
    {
        EnsureLoaded();
        string key = KeyFor(asset);
        _favorites.Remove(key);   // mutual exclusion
        if (_hidden.Add(key)) Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private sealed class SaveData
    {
        public List<string> Favorites { get; set; } = new();
        public List<string> Hidden    { get; set; } = new();
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            var data = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;

            _favorites.Clear();
            _hidden.Clear();
            foreach (var k in data.Favorites ?? new List<string>()) _favorites.Add(k);
            foreach (var k in data.Hidden    ?? new List<string>()) _hidden.Add(k);

            BceConsole.LogInfo(
                $"BridgeFavoritesManager: loaded {_favorites.Count} favorite(s), {_hidden.Count} hidden.",
                ConsoleColor.DarkBlue);
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"BridgeFavoritesManager: load failed: {ex.Message}");
        }
    }

    // Write-task chain: ContinueWith serialises background writes so File.WriteAllText never runs on two threads at once.
    private static Task _lastWrite = Task.CompletedTask;

    private static void Save()
    {
        // Serialize on the game thread — _favorites/_hidden are only mutated here, so the snapshot is safe and the lambda captures an immutable string.
        string json = JsonConvert.SerializeObject(
            new SaveData { Favorites = new List<string>(_favorites), Hidden = new List<string>(_hidden) },
            Formatting.Indented);

        // Chain the write onto the previous one so writes are strictly sequential and the file is never raced by two threads.
        _lastWrite = AtomicJson.QueueWrite(_lastWrite, SavePath, json, "BridgeFavoritesManager: save failed");
    }

    /// Blocks briefly for the in-flight background write to finish. Called on quit so a
    /// favorite/hide toggle made right before exit is never lost.
    internal static void FlushPendingWrites()
    {
        try { _lastWrite.Wait(System.TimeSpan.FromSeconds(2)); }
        catch { /* best-effort flush on shutdown */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string KeyFor(CosmeticAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.assetId))   return asset.assetId;
        if (!string.IsNullOrEmpty(asset.assetName)) return asset.assetName;
        return asset.name ?? "";
    }
}
