// Favorite/hidden/modded sorting. Keys: 1) favorite | normal | hidden-last, 2) unlocked first, 3) bridged | modded | vanilla, 4) pack-themed borders first, 5) vanilla rarity (UltraRare→Common, then name).
// Truly-inactive buttons go last; badge markers (* / X) refresh on every visible button.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class CosmeticsSortHelper
{
    // Thin wrapper: computes the boolean arguments and delegates to RebuildSectionSortOrder.
    internal static void SortFavoritesInCategory(MenuPageCosmetics page,
                                                  bool hiddenAtEnd  = false,
                                                  bool skipRebuild  = false)
    {
        bool hasFavs      = BridgeFavoritesManager.HasAnyFavorite();
        bool hasAnyHidden = BridgeFavoritesManager.HasAnyHidden();
        RebuildSectionSortOrder(page, hasFavs, hasAnyHidden, hiddenAtEnd && hasAnyHidden, skipRebuild);
    }

    private static void RebuildSectionSortOrder(
        MenuPageCosmetics page, bool hasFavs, bool hasAnyHidden, bool sortHiddenAtEnd, bool skipRebuild)
    {
        // Fast path: nothing to sort or badge — skip the expensive per-section scan.
        if (!hasFavs && !hasAnyHidden && !CustomizerStore.HasAnyModded())
            return;

        foreach (var section in page.sections)
        {
            if (section == null || section.cosmeticListTransform == null) continue;

            var lazy = section.GetComponent<LazyCosmeticGrid>();
            if (lazy != null)
            {
                SortLazyGrid(lazy, hasFavs, hasAnyHidden, sortHiddenAtEnd);
                continue;
            }

            var allButtons = section.cosmeticListTransform
                .GetComponentsInChildren<MenuElementCosmeticButton>(includeInactive: true);

            MenuElementCosmeticButton? clearButton = null;
            var visible  = new List<SortEntry<MenuElementCosmeticButton>>();
            var inactive = new List<(MenuElementCosmeticButton Button, int Sibling)>();

            bool hasFavsHere   = false;
            bool hasHiddenHere = false;
            bool hasModdedHere = false;

            foreach (var btn in allButtons)
            {
                if (btn == null) continue;

                if (btn.cosmeticAsset == null)
                {
                    clearButton ??= btn;
                    continue;
                }

                int sibling = btn.transform.GetSiblingIndex();
                if (!btn.gameObject.activeSelf)
                {
                    inactive.Add((btn, sibling));
                    continue;
                }

                var entry = new SortEntry<MenuElementCosmeticButton>(btn, btn.cosmeticAsset, sibling, hasFavs, hasAnyHidden);

                FavHideMarkerHelper.UpdateMarker(btn, entry.Favorite, entry.Hidden);

                hasFavsHere   |= entry.Favorite;
                hasHiddenHere |= entry.Hidden;
                hasModdedHere |= entry.Modded || entry.NonBridgeModded;
                visible.Add(entry);
            }

            if (visible.Count == 0 && inactive.Count == 0) continue;
            if (!hasFavsHere && !hasHiddenHere && !hasModdedHere) continue;

            int si = 0;
            if (clearButton != null) clearButton.transform.SetSiblingIndex(si++);
            foreach (var item in InMenuOrder(visible, sortHiddenAtEnd)) item.Item.transform.SetSiblingIndex(si++);
            foreach (var item in inactive.OrderBy(b => b.Sibling))      item.Button.transform.SetSiblingIndex(si++);

            var listRect = section.cosmeticListTransform?.GetComponent<RectTransform>();
            if (listRect != null && !skipRebuild)
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        }
    }

    // Same keys, on the section's data: its buttons don't exist yet. Markers go on as each one is created.
    private static void SortLazyGrid(LazyCosmeticGrid lazy, bool hasFavs, bool hasAnyHidden, bool sortHiddenAtEnd)
    {
        var entries = new List<SortEntry<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)>>(lazy.Count);
        bool hasAnyHere = false;
        for (int i = 0; i < lazy.Count; i++)
        {
            var entry = new SortEntry<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)>(lazy.Items[i], lazy.Items[i].rep, i, hasFavs, hasAnyHidden);
            hasAnyHere |= entry.Favorite || entry.Hidden || entry.Modded || entry.NonBridgeModded;
            entries.Add(entry);
        }

        if (hasAnyHere)
            lazy.SetOrder(InMenuOrder(entries, sortHiddenAtEnd).Select(e => e.Item).ToList());
    }

    private static IEnumerable<SortEntry<T>> InMenuOrder<T>(List<SortEntry<T>> entries, bool sortHiddenAtEnd)
        => entries
            .OrderBy(b =>
            {
                if (b.Favorite) return 0;
                if (sortHiddenAtEnd && b.Hidden) return 3;
                return 1;
            })
            // Mini-Semibot pinned right after favorites (the world-list builders order it first; this re-sort must not displace it).
            .ThenBy(b => b.Asset.assetId == MiniSemibotCosmetic.AssetId ? 0 : 1)
            .ThenBy(b => b.Unlocked ? 0 : 1)
            .ThenBy(b => b.Modded ? 0 : b.NonBridgeModded ? 1 : 2)
            // Pack-themed borders (RepoPride gradient / Xuaun coral) lead their group.
            .ThenBy(b => b.Themed ? 0 : 1)
            .ThenBy(b => b.Position)
            .ToArray();

    private readonly struct SortEntry<T>
    {
        internal readonly T Item;
        internal readonly CosmeticAsset Asset;
        internal readonly int Position;
        internal readonly bool Favorite, Hidden, Modded, NonBridgeModded, Themed, Unlocked;

        internal SortEntry(T item, CosmeticAsset asset, int position, bool hasFavs, bool hasAnyHidden)
        {
            Item = item;
            Asset = asset;
            Position = position;
            Favorite = hasFavs && BridgeFavoritesManager.IsFavorite(asset);
            Hidden = hasAnyHidden && BridgeFavoritesManager.IsHidden(asset);
            Modded = CustomizerStore.IsModdedForAsset(asset);
            NonBridgeModded = CustomizerStore.IsNonBridgeModdedForAsset(asset);
            Themed = BorderTheme.TryResolve(asset, out _);
            Unlocked = CosmeticsFilterPatch.IsUnlocked(asset);
        }
    }
}
