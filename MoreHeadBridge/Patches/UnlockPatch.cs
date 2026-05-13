using HarmonyLib;
using REPOLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.Load))]
internal static class UnlockPatch
{
    // Tracks whether ResetUnlocks needs to fire on this launch. Captured at startup
    // so we only honor the value the user set BEFORE the game ran (avoids toggling loops
    // and lets us auto-flip the config back to false safely).
    private static readonly bool _resetRequestedAtStartup = Plugin.ResetUnlocks.Value;
    private static bool _resetDone;

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

        if (!Plugin.UnlockAll.Value)
        {
            Plugin.Logger.LogDebug("UnlockAll=false, skipping auto-unlock.");
            return;
        }

        TryUnlock(__instance);
        BundleLoader.OnAllBundlesLoaded += OnBundlesLoaded;
    }

    private static void OnBundlesLoaded()
    {
        BundleLoader.OnAllBundlesLoaded -= OnBundlesLoaded;

        if (MetaManager.instance == null)
        {
            Plugin.Logger.LogWarning("MetaManager.instance is null in deferred path — skipping.");
            return;
        }

        if (_resetRequestedAtStartup && !_resetDone)
        {
            TryReset(MetaManager.instance);
            if (!_resetDone) return; // reset still deferred — nothing to unlock yet
            // fall through: if UnlockAll is on, re-unlock on the same launch after wiping
        }

        if (Plugin.UnlockAll.Value)
            TryUnlock(MetaManager.instance);
    }

    private static void TryUnlock(MetaManager instance)
    {
        int added = 0;
        foreach (string assetId in HhhCosmeticLoader.RegisteredAssetIds)
        {
            int index = instance.cosmeticAssets.FindIndex(a => a != null && a.assetId == assetId);
            if (index < 0) continue;

            if (!instance.cosmeticUnlocks.Contains(index))
            {
                instance.cosmeticUnlocks.Add(index);
                added++;
            }
        }

        if (added > 0)
            Plugin.Logger.LogInfo($"Auto-unlocked {added} bridge cosmetic(s) (UnlockAll=true).");
    }

    // Removes every bridge cosmetic from unlocks / equipped / history, then persists. We
    // need bridge cosmetics to be registered in MetaManager.cosmeticAssets before we can
    // map assetId → index, so this is called from both the immediate Load postfix AND
    // the deferred OnAllBundlesLoaded handler. _resetDone guards against double execution.
    private static void TryReset(MetaManager instance)
    {
        var bridgeIds = new HashSet<string>(HhhCosmeticLoader.RegisteredAssetIds);
        if (bridgeIds.Count == 0) return;

        var bridgeIndices = new HashSet<int>();
        for (int i = 0; i < instance.cosmeticAssets.Count; i++)
        {
            var asset = instance.cosmeticAssets[i];
            if (asset != null && bridgeIds.Contains(asset.assetId))
                bridgeIndices.Add(i);
        }

        // Nothing registered yet → wait for OnAllBundlesLoaded.
        if (bridgeIndices.Count == 0)
        {
            Plugin.Logger.LogDebug("ResetUnlocks: no bridge cosmetics in cosmeticAssets yet, deferring.");
            return;
        }

        int removedUnlocks = instance.cosmeticUnlocks.RemoveAll(bridgeIndices.Contains);
        int removedEquipped = instance.cosmeticEquipped.RemoveAll(bridgeIndices.Contains);
        int removedHistory = instance.cosmeticHistory.RemoveAll(bridgeIndices.Contains);

        instance.Save();

        // Auto-clear the flag so it really is one-shot.
        Plugin.ResetUnlocks.Value = false;
        Plugin.Instance.Config.Save();

        _resetDone = true;

        Plugin.Logger.LogInfo(
            $"ResetUnlocks: cleared {removedUnlocks} unlock(s), " +
            $"{removedEquipped} equipped, {removedHistory} history entry. Flag reset to false.");
    }
}
