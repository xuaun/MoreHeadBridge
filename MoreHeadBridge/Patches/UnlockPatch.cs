using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using REPOLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.Load))]
internal static class UnlockPatch
{
    // Captured at startup so only the pre-launch config value is honoured — avoids toggle loops and lets us auto-flip the flag back to false safely.
    private static readonly bool _resetRequestedAtStartup      = Plugin.ResetBridgeUnlocks.Value;
    private static readonly bool _resetModdedRequestedAtStartup = Plugin.ResetModdedUnlocks.Value;
    private static bool _resetDone;

    // ── Modded-cosmetic unlock tracking ───────────────────────────────────────
    // Persists which non-bridge modded cosmetics AutoUnlockModdedCosmetics unlocked so ResetModdedUnlocks can remove exactly those without touching earned unlocks.

    private static readonly string _moddedTrackPath = BridgePaths.Of("AutoUnlockedModded.json");

    // Loaded once at class-init time; updated and saved whenever the set changes.
    private static HashSet<string> _autoUnlockedModded = LoadModdedTracking();

    private static HashSet<string> LoadModdedTracking()
    {
        try
        {
            if (!File.Exists(_moddedTrackPath)) return new();
            var set = JsonConvert.DeserializeObject<HashSet<string>>(
                          File.ReadAllText(_moddedTrackPath));
            return set ?? new();
        }
        catch (System.Exception ex) { BridgeLog.Debug($"AutoUnlockModded: tracking file unreadable, starting fresh — {ex.Message}"); return new(); }
    }

    private static void SaveModdedTracking()
    {
        try
        {
            File.WriteAllText(
                _moddedTrackPath,
                JsonConvert.SerializeObject(_autoUnlockedModded, Formatting.Indented));
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"AutoUnlockModded: could not save tracking — {ex.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance)
    {
        // Reset runs first — both immediate and deferred — so unlock logic operates on a clean state.
        if (_resetRequestedAtStartup && !_resetDone)
        {
            TryReset(__instance);
            BundleLoader.OnAllBundlesLoaded += OnBundlesLoaded;
            return;
        }

        // Bridge unlock: can run immediately (bridge cosmetics registered before MetaManager.Load).
        if (Plugin.AutoUnlockBridgeCosmetics.Value)
            TryUnlock(__instance);
        else
            BridgeLog.Trace("AutoUnlockBridgeCosmetics=false, skipping bridge auto-unlock");

        // Modded cosmetics (REPOLib bundles) aren't available until OnAllBundlesLoaded — always register the deferred handler when either feature needs it.
        bool needDeferred = Plugin.AutoUnlockBridgeCosmetics.Value
                         || Plugin.AutoUnlockModdedCosmetics.Value
                         || _resetModdedRequestedAtStartup;
        if (needDeferred)
            BundleLoader.OnAllBundlesLoaded += OnBundlesLoaded;
    }

    private static void OnBundlesLoaded()
    {
        BundleLoader.OnAllBundlesLoaded -= OnBundlesLoaded;

        if (MetaManager.instance == null)
        {
            BceConsole.LogWarning("MetaManager.instance is null in deferred path — skipping");
            return;
        }

        if (_resetRequestedAtStartup && !_resetDone)
        {
            TryReset(MetaManager.instance);
            if (!_resetDone) return; // reset still deferred — nothing to unlock yet
            // fall through: if AutoUnlockBridgeCosmetics is on, re-unlock on the same launch after wiping
        }

        if (Plugin.AutoUnlockBridgeCosmetics.Value)
            TryUnlock(MetaManager.instance);

        // Modded cosmetics are available now that all bundles have loaded.
        if (_resetModdedRequestedAtStartup)
        {
            TryResetModded(MetaManager.instance);
            // Auto-flip flag, same as bridge reset.
            Plugin.ResetModdedUnlocks.Value = false;
            Plugin.Instance?.Config.Save();
        }
        else if (Plugin.AutoUnlockModdedCosmetics.Value)
        {
            TryUnlockModded(MetaManager.instance);
        }
    }

    private static void TryUnlock(MetaManager instance)
    {
        // assetId → index map built once: O(m) instead of a FindIndex per bridge cosmetic (O(n×m) ≈ 3.2M comparisons at 1600+ bridge assets).
        var idToIndex = BuildAssetIdIndex(instance);

        int added = 0;
        foreach (string assetId in HhhCosmeticLoader.RegisteredAssetIds)
        {
            if (!idToIndex.TryGetValue(assetId, out int index)) continue;

            if (!instance.cosmeticUnlocks.Contains(index))
            {
                instance.cosmeticUnlocks.Add(index);
                added++;
            }
        }

        if (added > 0)
            BceConsole.LogInfo($"Auto-unlocked {added} bridge cosmetic(s) (AutoUnlockBridgeCosmetics=true)", ConsoleColor.Magenta);
    }

    // ── Ingame one-shot paths (SettingChanged handlers) ───────────────────────

    /// Runs auto-unlock immediately at runtime (MetaManager already loaded).
    internal static void RunAutoUnlockNow()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;
        TryUnlock(meta);
    }

    /// Runs reset immediately at runtime, then optionally re-unlocks when AutoUnlock is on.
    /// Re-instantiates local cosmetics if any bridge cosmetics were equipped at the time.
    internal static void RunResetNow()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        var bridgeIds = new HashSet<string>(HhhCosmeticLoader.RegisteredAssetIds);
        if (bridgeIds.Count == 0)
        {
            Plugin.ResetBridgeUnlocks.Value = false;
            Plugin.Instance?.Config.Save();
            return;
        }

        var idToIndex = BuildAssetIdIndex(meta);
        var bridgeIndices = new HashSet<int>();
        foreach (string id in bridgeIds)
            if (idToIndex.TryGetValue(id, out int idx)) bridgeIndices.Add(idx);

        bool hadEquipped = meta.cosmeticEquipped.Exists(bridgeIndices.Contains);

        int removedUnlocks = meta.cosmeticUnlocks.RemoveAll(bridgeIndices.Contains);
        int removedEquipped = meta.cosmeticEquipped.RemoveAll(bridgeIndices.Contains);
        int removedHistory  = meta.cosmeticHistory.RemoveAll(bridgeIndices.Contains);
        meta.Save();

        Plugin.ResetBridgeUnlocks.Value = false;
        Plugin.Instance?.Config.Save();

        if (Plugin.AutoUnlockBridgeCosmetics.Value)
            TryUnlock(meta);

        if (hadEquipped)
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();

        BceConsole.LogInfo(
            $"ResetBridgeUnlocks (ingame): cleared {removedUnlocks} unlock(s), " +
            $"{removedEquipped} equipped, {removedHistory} history. Flag reset to false",
            ConsoleColor.Magenta);
    }

    // ── Modded cosmetic unlock / reset ────────────────────────────────────────

    private static void TryUnlockModded(MetaManager instance)
    {
        var idToIndex = BuildAssetIdIndex(instance);
        int added = 0;

        foreach (var asset in instance.cosmeticAssets)
        {
            if (asset == null || !BridgeIds.IsModdedCosmetic(asset)) continue;
            if (!idToIndex.TryGetValue(asset.assetId, out int index)) continue;

            if (!instance.cosmeticUnlocks.Contains(index))
            {
                instance.cosmeticUnlocks.Add(index);
                _autoUnlockedModded.Add(asset.assetId);
                added++;
            }
        }

        if (added > 0)
        {
            SaveModdedTracking();
            instance.Save();
            BceConsole.LogInfo(
                $"Auto-unlocked {added} modded cosmetic(s) (AutoUnlockModdedCosmetics=true)",
                ConsoleColor.Magenta);
        }
    }

    private static void TryResetModded(MetaManager instance)
    {
        if (_autoUnlockedModded.Count == 0)
        {
            BridgeLog.Trace("ResetModdedUnlocks: no tracked unlocks to remove");
            return;
        }

        // Snapshot tracked ids BEFORE clearing: ids of UNINSTALLED mods have no live index for the removal below, and REPOLib's "missing" list would re-unlock them when the mod returns.
        var trackedIds = new List<string>(_autoUnlockedModded);

        var idToIndex = BuildAssetIdIndex(instance);
        var toRemove  = new HashSet<int>();
        foreach (var assetId in _autoUnlockedModded)
            if (idToIndex.TryGetValue(assetId, out int idx)) toRemove.Add(idx);

        bool hadEquipped = instance.cosmeticEquipped.Exists(toRemove.Contains);

        int removedUnlocks  = instance.cosmeticUnlocks.RemoveAll(toRemove.Contains);
        int removedEquipped = instance.cosmeticEquipped.RemoveAll(toRemove.Contains);
        int removedHistory  = instance.cosmeticHistory.RemoveAll(toRemove.Contains);

        // Drop the tracked ids from REPOLib's "missing" lists so a reset purges even cosmetics whose mod is uninstalled — else REPOLib re-saves them and they return on reinstall.
        int removedMissing = PurgeFromRepoLibMissing(trackedIds);

        _autoUnlockedModded.Clear();
        SaveModdedTracking();
        instance.Save();   // persists AFTER the missing-list purge, so the dropped ids aren't re-written

        if (hadEquipped)
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();

        BceConsole.LogInfo(
            $"ResetModdedUnlocks: removed {removedUnlocks} unlock(s), " +
            $"{removedEquipped} equipped, {removedHistory} history, {removedMissing} uninstalled-mod " +
            $"entr(ies). Tracking cleared.",
            ConsoleColor.Magenta);
    }

    // Best-effort: drop the ids from REPOLib's private "missing" lists (re-saved every launch) — otherwise an uninstalled-mod unlock survives the reset and returns on reinstall. Pure reflection; a REPOLib change just no-ops. Returns entries dropped.
    private static int PurgeFromRepoLibMissing(ICollection<string> assetIds)
    {
        if (assetIds.Count == 0) return 0;
        int removed = 0;
        try
        {
            var type = AccessTools.TypeByName("REPOLib.Patches.MetaManagerPatch");
            if (type == null) return 0;
            var idSet = new HashSet<string>(assetIds, StringComparer.Ordinal);
            foreach (var fieldName in new[] { "missingCosmeticUnlocks", "missingCosmeticHistory" })
            {
                var field = AccessTools.Field(type, fieldName);
                if (field?.GetValue(null) is System.Collections.Generic.List<string> list)
                    removed += list.RemoveAll(id => idSet.Contains(id));
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"ResetModded: could not prune REPOLib missing list — {ex.Message}");
        }
        return removed;
    }

    /// Runs AutoUnlockModdedCosmetics immediately at runtime (MetaManager already loaded).
    internal static void RunAutoUnlockModdedNow()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;
        TryUnlockModded(meta);
    }

    /// Runs ResetModdedUnlocks immediately at runtime, then flips the flag back.
    internal static void RunResetModdedNow()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;
        TryResetModded(meta);
        Plugin.ResetModdedUnlocks.Value = false;
        Plugin.Instance?.Config.Save();
    }

    // Builds an assetId → cosmeticAssets list-index dictionary in O(m); shared by TryUnlock and TryReset to avoid repeated O(n × m) FindIndex scans.
    private static Dictionary<string, int> BuildAssetIdIndex(MetaManager instance)
    {
        var map = new Dictionary<string, int>(instance.cosmeticAssets.Count, StringComparer.Ordinal);
        for (int i = 0; i < instance.cosmeticAssets.Count; i++)
        {
            if (instance.cosmeticAssets[i]?.assetId is string id)
                map[id] = i;
        }
        return map;
    }

    // Removes every bridge cosmetic from unlocks/equipped/history, then persists. Needs assets registered in cosmeticAssets, so it's called from both the immediate Load postfix AND the deferred OnAllBundlesLoaded; _resetDone guards double execution.
    private static void TryReset(MetaManager instance)
    {
        var bridgeIds = new HashSet<string>(HhhCosmeticLoader.RegisteredAssetIds);
        if (bridgeIds.Count == 0) return;

        var idToIndex = BuildAssetIdIndex(instance);
        var bridgeIndices = new HashSet<int>();
        foreach (string id in bridgeIds)
        {
            if (idToIndex.TryGetValue(id, out int idx))
                bridgeIndices.Add(idx);
        }

        // Nothing registered yet → wait for OnAllBundlesLoaded.
        if (bridgeIndices.Count == 0)
        {
            BridgeLog.Trace("ResetBridgeUnlocks: no bridge cosmetics in cosmeticAssets yet, deferring");
            return;
        }

        int removedUnlocks = instance.cosmeticUnlocks.RemoveAll(bridgeIndices.Contains);
        int removedEquipped = instance.cosmeticEquipped.RemoveAll(bridgeIndices.Contains);
        int removedHistory = instance.cosmeticHistory.RemoveAll(bridgeIndices.Contains);

        instance.Save();

        // Auto-clear the flag so it really is one-shot.
        Plugin.ResetBridgeUnlocks.Value = false;
        Plugin.Instance?.Config.Save();

        _resetDone = true;

        BceConsole.LogInfo(
            $"ResetBridgeUnlocks: cleared {removedUnlocks} unlock(s), " +
            $"{removedEquipped} equipped, {removedHistory} history entry. Flag reset to false",
            ConsoleColor.Magenta);
    }
}
