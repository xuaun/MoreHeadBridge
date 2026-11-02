using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

// Loads the saved icons of cosmetic mods in the background once the main menu opens, a few per frame. Left to the buttons, a section scrolling into view decoded all of its icons in a single frame.
[HarmonyPatch(typeof(MenuPageMain), "Start")]
internal static class BridgeIconPreloadPatch
{
    private const int IconsPerBatch = 8;

    private static bool _started;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_started || MetaManager.instance == null) return;
        _started = true;
        _ = Preload();
    }

    private static async Task Preload()
    {
        double startedAt = Time.realtimeSinceStartupAsDouble;   // TEMP-TEST
        int total = 0;   // TEMP-TEST

        var batch = new List<Task>(IconsPerBatch);
        foreach (var asset in MetaManager.instance.cosmeticAssets.ToList())
        {
            var loading = LoadIcon(asset);
            if (loading == null) continue;

            total++;   // TEMP-TEST
            batch.Add(Keep(asset, loading));
            if (batch.Count < IconsPerBatch) continue;

            await Task.WhenAll(batch);
            batch.Clear();
            await Task.Yield();
        }
        await Task.WhenAll(batch);

        Plugin.Logger.LogInfo(System.FormattableString.Invariant($"[PERF] icon preload: {total} icons in {Time.realtimeSinceStartupAsDouble - startedAt:F1} s"));   // TEMP-TEST
    }

    private static Task<Sprite?>? LoadIcon(CosmeticAsset asset)
    {
        if (asset == null || asset.icon != null) return null;

        if (BridgeIds.IsBridgeAsset(asset))
            return IconCapture.HasCache(asset) ? SemiFunc.LoadSpriteFromFileAsync(IconCapture.CachePathFor(asset))! : null;

        return BridgeIds.IsModdedCosmetic(asset) && RenderedIconCache.Has(asset) ? RenderedIconCache.LoadAsync(asset) : null;
    }

    // A button may have loaded the same icon in the meantime.
    private static async Task Keep(CosmeticAsset asset, Task<Sprite?> loading)
    {
        try
        {
            var sprite = await loading;
            if (sprite == null) return;

            if (asset.icon == null)
            {
                asset.icon = sprite;
                return;
            }

            Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"BridgeIconPreloadPatch: could not load the icon of '{asset.name}': {ex.Message}");
        }
    }
}
