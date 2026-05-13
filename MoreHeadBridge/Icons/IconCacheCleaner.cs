// ============================================================================
// [MenuCapture] Cache deletion — one-shot delete of bridge icon PNGs.
//
// Controlled by two configs in [Icons]:
//   * DeleteIconCache        (bool, one-shot)  - delete cached bridge icons
//   * DeleteIconsMatching    (string, optional) - comma-separated name filter.
//                                                 Empty = delete ALL bridge icons.
//
// Triggers on startup (before HhhCosmeticLoader runs), so the next time GetIcon is
// called for a deleted cosmetic the regular fallback chain (texture extract →
// placeholder) takes over and it can re-capture it on next hover.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MoreHeadBridge;

internal static class IconCacheCleaner
{
    internal static void Run()
    {
        if (!Plugin.DeleteIconCache.Value) return;

        try
        {
            string dir = IconCapture.CacheDir;
            if (!Directory.Exists(dir))
            {
                Plugin.Logger.LogInfo("DeleteIconCache: no cache directory, nothing to do.");
                ResetFlag();
                return;
            }

            string filterRaw = Plugin.DeleteIconsMatching.Value ?? "";
            string[] filters = filterRaw
                .Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0)
                .ToArray();

            var bridgeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in HhhCosmeticLoader.RegisteredAssetIds)
            {
                int colon = id.IndexOf(':');
                if (colon < 0 || colon + 1 >= id.Length) continue;
                bridgeNames.Add(id[(colon + 1)..]);
            }

            int deleted = 0;
            int kept = 0;

            foreach (string file in Directory.GetFiles(dir, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                // Only touch files whose name matches a registered bridge cosmetic.
                bool isBridge = bridgeNames.Contains(name);

                if (!isBridge) { kept++; continue; }

                if (filters.Length > 0 && !filters.Any(f => name.Contains(f)))
                {
                    kept++;
                    continue;
                }

                try { File.Delete(file); deleted++; }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Failed to delete '{file}': {ex.Message}");
                }
            }

            Plugin.Logger.LogInfo(
                $"DeleteIconCache: removed {deleted} bridge icon(s), kept {kept}. " +
                $"Filter: {(filters.Length == 0 ? "(all bridge icons)" : string.Join(",", filters))}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"DeleteIconCache failed: {ex.Message}");
        }
        finally
        {
            ResetFlag();
        }
    }

    private static void ResetFlag()
    {
        Plugin.DeleteIconCache.Value = false;
        Plugin.Instance.Config.Save();
    }
}
