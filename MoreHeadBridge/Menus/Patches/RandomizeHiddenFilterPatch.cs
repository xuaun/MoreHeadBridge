// ============================================================================
// Prevents hidden cosmetics from being picked by any of the three Randomize
// buttons (All / Body / Cosmetics).
//
// Strategy: temporarily remove the indices of hidden assets from
// MetaManager.cosmeticUnlocks before vanilla (and WorldCosmeticsRandomize)
// read that list, then restore them in the Postfix.
// ============================================================================

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

[HarmonyPatch]
internal static class RandomizeHiddenFilterPatch
{
    // Indices temporarily removed from cosmeticUnlocks; restored in Postfix.
    private static readonly List<int> _removed = new();

    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeAllButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeBodyButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeCosmeticsButton");
    }

    // Runs before vanilla AND before WorldCosmeticsRandomize (both at Priority.Normal).
    [HarmonyPrefix]
    private static void Prefix()
    {
        _removed.Clear();

        BridgeFavoritesManager.EnsureLoaded();
        if (!BridgeFavoritesManager.HasAnyHidden()) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        // Iterate backwards so RemoveAt doesn't shift indices we haven't visited yet.
        // Wrapped in try-catch: if anything throws mid-loop, restore whatever was
        // already removed so cosmeticUnlocks is never left in a truncated state.
        try
        {
            for (int i = meta.cosmeticUnlocks.Count - 1; i >= 0; i--)
            {
                int assetIdx = meta.cosmeticUnlocks[i];
                if (assetIdx < 0 || assetIdx >= meta.cosmeticAssets.Count) continue;

                var asset = meta.cosmeticAssets[assetIdx];
                if (asset != null && BridgeFavoritesManager.IsHidden(asset))
                {
                    _removed.Add(assetIdx);
                    meta.cosmeticUnlocks.RemoveAt(i);
                }
            }
        }
        catch (Exception ex)
        {
            // Rollback any partial removals so the Postfix has nothing to restore
            // and cosmeticUnlocks is left intact.
            meta.cosmeticUnlocks.AddRange(_removed);
            _removed.Clear();
            Plugin.Logger.LogWarning($"RandomizeHiddenFilterPatch: failed to filter hidden cosmetics, skipping: {ex.Message}");
        }
    }

    // Runs LAST — after vanilla Save() and after WorldCosmeticsRandomize.Postfix.
    // Restores the hidden entries in memory then rewrites the save file so that
    // hidden cosmetics are never lost from the unlock record.
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        if (_removed.Count == 0) return;

        var meta = MetaManager.instance;
        if (meta != null)
        {
            meta.cosmeticUnlocks.AddRange(_removed);

            // Save without creating a new backup — the backup was already written
            // by vanilla's own Save() call and represents the correct pre-randomize
            // state. This call only corrects the main save file.
            meta.Save(createBackup: false);
        }

        _removed.Clear();
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (_removed.Count > 0)
        {
            var meta = MetaManager.instance;
            if (meta != null)
                meta.cosmeticUnlocks.AddRange(_removed);
            _removed.Clear();

            Plugin.Logger.LogWarning(
                "RandomizeHiddenFilterPatch: vanilla threw during Randomize — " +
                "hidden cosmetics restored to unlock record via Finalizer.");
        }
        return __exception;
    }
}
