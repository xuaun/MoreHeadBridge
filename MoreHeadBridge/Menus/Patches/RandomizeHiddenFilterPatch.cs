// ============================================================================
// Prevents hidden cosmetics from being picked by any of the three Randomize buttons (All / Body / Cosmetics): temporarily remove hidden assets' indices from MetaManager.cosmeticUnlocks before vanilla reads it, restoring in the Postfix.
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

        // Backwards so RemoveAt doesn't shift unvisited indices; on a mid-loop throw, restore what was already removed so cosmeticUnlocks is never left truncated.
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
            // Rollback any partial removals so the Postfix has nothing to restore and cosmeticUnlocks is left intact.
            meta.cosmeticUnlocks.AddRange(_removed);
            _removed.Clear();
            BceConsole.LogWarning($"RandomizeHiddenFilterPatch: failed to filter hidden cosmetics, skipping: {ex.Message}");
        }
    }

    // Runs LAST (after vanilla Save() and WorldCosmeticsRandomize.Postfix): restore in memory, then rewrite the save file so hidden cosmetics never drop from the unlock record.
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        if (_removed.Count == 0) return;

        var meta = MetaManager.instance;
        if (meta != null)
        {
            meta.cosmeticUnlocks.AddRange(_removed);

            // Save without a new backup — vanilla's own Save() already wrote the correct pre-randomize backup; this only corrects the main save file.
            meta.Save();
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

            BceConsole.LogWarning(
                "RandomizeHiddenFilterPatch: vanilla threw during Randomize — " +
                "hidden cosmetics restored to unlock record via Finalizer");
        }
        return __exception;
    }
}
