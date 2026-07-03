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

            var allButtons = section.cosmeticListTransform
                .GetComponentsInChildren<MenuElementCosmeticButton>(includeInactive: true);

            MenuElementCosmeticButton? clearButton = null;
            var visible  = new List<(MenuElementCosmeticButton Button, bool Favorite, bool Hidden, bool Modded, bool NonBridgeModded, bool Themed, bool Unlocked, int Sibling)>();
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

                bool isFav             = hasFavs      && BridgeFavoritesManager.IsFavorite(btn.cosmeticAsset);
                bool isHidden          = hasAnyHidden  && BridgeFavoritesManager.IsHidden(btn.cosmeticAsset);
                bool isModded          = CustomizerStore.IsModdedForAsset(btn.cosmeticAsset);
                bool isNonBridgeModded = CustomizerStore.IsNonBridgeModdedForAsset(btn.cosmeticAsset);
                bool isThemed          = BorderTheme.TryResolve(btn.cosmeticAsset, out _);

                FavHideMarkerHelper.UpdateMarker(btn, isFav, isHidden);

                hasFavsHere   |= isFav;
                hasHiddenHere |= isHidden;
                hasModdedHere |= isModded || isNonBridgeModded;
                visible.Add((btn, isFav, isHidden, isModded, isNonBridgeModded, isThemed, CosmeticsFilterPatch.IsUnlocked(btn), sibling));
            }

            if (visible.Count == 0 && inactive.Count == 0) continue;
            if (!hasFavsHere && !hasHiddenHere && !hasModdedHere) continue;

            var sorted = visible
                .OrderBy(b =>
                {
                    if (b.Favorite) return 0;
                    if (sortHiddenAtEnd && b.Hidden) return 3;
                    return 1;
                })
                // Mini-Semibot pinned right after favorites (the world-list builders order it first; this re-sort must not displace it).
                .ThenBy(b => b.Button.cosmeticAsset.assetId == MiniSemibotCosmetic.AssetId ? 0 : 1)
                .ThenBy(b => b.Unlocked ? 0 : 1)
                .ThenBy(b => b.Modded ? 0 : b.NonBridgeModded ? 1 : 2)
                // Pack-themed borders (RepoPride gradient / Xuaun coral) lead their group.
                .ThenBy(b => b.Themed ? 0 : 1)
                .ThenBy(b => b.Sibling)
                .ToArray();

            int si = 0;
            if (clearButton != null) clearButton.transform.SetSiblingIndex(si++);
            foreach (var item in sorted)                            item.Button.transform.SetSiblingIndex(si++);
            foreach (var item in inactive.OrderBy(b => b.Sibling)) item.Button.transform.SetSiblingIndex(si++);

            var listRect = section.cosmeticListTransform?.GetComponent<RectTransform>();
            if (listRect != null && !skipRebuild)
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        }
    }
}
