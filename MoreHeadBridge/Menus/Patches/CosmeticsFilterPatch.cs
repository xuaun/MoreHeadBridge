// Postfix on MenuPageCosmetics.RefreshScrollContent.
//
// VIRTUAL CATEGORIES (SEARCH, SELECTED, FAV, HIDE):
//   Buttons are shown / hidden based on the Matches() predicate.
//   No sibling reordering — cosmetics appear in the same vanilla order
//   (locked-last → rarity-desc → name-asc) as they do in HEAD/BODY/etc.,
//   just filtered to the relevant subset.
//   Injects a dedicated World section after all others.
//
// VANILLA CATEGORIES (HEAD, BODY, ARMS, LEGS, WORLD, …):
//   Buttons stay in vanilla order except that favorites are sorted first
//   inside each section (directly after the clear button).
//   Hidden items are suppressed when there are any.
//   Markers (* / X) are updated on every visible button.

using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "RefreshScrollContent")]
internal static class CosmeticsFilterPatch
{
    private const float  SectionSpacing   = 10f;
    private const float  SectionHeader    = 40f;
    internal const string WorldSectionName = "MHB_WorldSection";
    // Synthetic CosmeticType for the injected World section in virtual categories.
    internal const SemiFunc.CosmeticType WorldSubCategory = (SemiFunc.CosmeticType)999;

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        // selectedTab is authoritative — selectedCategory may still point to our
        // virtual category from a previous visit even when Presets is active.
        if (__instance.selectedTab == MenuPageCosmetics.CosmeticPageTab.Presets)
        {
            UpdateSearchFieldVisibility(false);
            HideEmptyState();
            return;
        }

        var selected = __instance.selectedCategory;
        if (selected == null) return;
        if (CosmeticsMenuState.IsPresetsCategory(selected))
        {
            UpdateSearchFieldVisibility(false);
            HideEmptyState();
            return;
        }

        bool isSelected = CosmeticsMenuState.IsSelected(selected);
        bool isSearch   = CosmeticsMenuState.IsSearch(selected);
        bool isFav      = CosmeticsMenuState.IsFavCategory(selected);
        bool isHide     = CosmeticsMenuState.IsHideCategory(selected);
        bool isVirtual  = CosmeticsMenuState.IsVirtual(selected);   // covers all four

        string search    = (CosmeticsMenuState.SearchText?.Trim() ?? "").ToLowerInvariant();
        bool applySearch = search.Length > 0;

        // Manage search field visibility and SearchMode flag.
        UpdateSearchFieldVisibility(isSearch);

        BridgeFavoritesManager.EnsureLoaded();

        // Whether we need to suppress hidden items in this view.
        // Hidden items are only visible in the HIDE and SELECTED tabs.
        bool suppressHidden = !isHide && !isSelected && BridgeFavoritesManager.HasAnyHidden();

        // For vanilla categories with no special filtering required:
        // sort favorites first and update markers, then exit.
        if (!isVirtual && !applySearch && !suppressHidden)
        {
            SortFavoritesInCategory(__instance);
            HideEmptyState();
            return;
        }

        if (MetaManager.instance == null) return;

        var equippedSet   = new HashSet<int>(MetaManager.instance.cosmeticEquipped);
        var unlocksSet    = new HashSet<int>(MetaManager.instance.cosmeticUnlocks);
        var assetIndexMap = BuildAssetIndexMap(MetaManager.instance);

        // Build sub-category button lookup.
        var subCatButtons = new Dictionary<SemiFunc.CosmeticType, GameObject>();
        foreach (Transform child in __instance.subCategoriesTransform)
        {
            var btn = child.GetComponent<MenuElementButtonCosmeticCategory>();
            if (btn != null && btn.buttonType == MenuElementButtonCosmeticCategory.ButtonType.SubCategory)
                subCatButtons[btn.subCategory] = child.gameObject;
        }

        // Show sub-category buttons for vanilla categories; hide all for virtual.
        foreach (var go in subCatButtons.Values)
            go.SetActive(!isVirtual);

        float yPos         = 0f;
        int   totalVisible = 0;

        bool splitWorldFromHat = isVirtual && HhhCosmeticLoader.WorldAssetIds.Count > 0;

        MenuElementCosmeticSection? lastReflowedSection = null;

        foreach (var section in __instance.sections.ToList())
        {
            if (section.isStickyHeader) continue;

            // In virtual categories, world assets are excluded from the Hat section and
            // shown in a dedicated World section injected after the loop.
            bool isHatSection = splitWorldFromHat
                && section.subCategory == SemiFunc.CosmeticType.Hat;

            var allButtons = section.cosmeticListTransform
                .GetComponentsInChildren<MenuElementCosmeticButton>(includeInactive: true);

            var cosmeticButtons = allButtons
                .Where(b => b != null && b.cosmeticAsset != null)
                .ToArray();

            int removedCount = 0;
            foreach (var btn in cosmeticButtons)
            {
                if (btn == null || btn.gameObject == null) continue;

                bool show;
                if (isHatSection && HhhCosmeticLoader.IsWorldAsset(btn.cosmeticAsset))
                {
                    // World assets go to their own injected section — always hide here.
                    show = false;
                }
                else if (!isVirtual && suppressHidden)
                {
                    // Non-virtual category: only HIDE the hidden items; never force-show.
                    // (WorldCosmeticsMenuFilterPatch already manages show/hide for world assets.)
                    if (BridgeFavoritesManager.IsHidden(btn.cosmeticAsset) && btn.gameObject.activeSelf)
                        btn.gameObject.SetActive(false);
                    if (btn.gameObject.activeSelf)
                        FavHideMarkerHelper.UpdateMarker(btn);
                    continue; // skip the standard show/hide logic below
                }
                else
                {
                    show = Matches(btn.cosmeticAsset,
                        isSelected, isSearch, isFav, isHide, suppressHidden,
                        applySearch, search,
                        equippedSet, assetIndexMap, unlocksSet);
                }

                if (btn.gameObject.activeSelf != show)
                    btn.gameObject.SetActive(show);

                if (show)
                    FavHideMarkerHelper.UpdateMarker(btn);
                else
                    removedCount++;
            }

            int remaining = cosmeticButtons.Length - removedCount;

            if (remaining == 0)
            {
                if (isVirtual)
                {
                    __instance.sections.Remove(section);
                    Object.Destroy(section.gameObject);
                }
                continue;
            }

            totalVisible += remaining;

            // Only reflow and re-show for virtual categories — vanilla handles non-virtual layout.
            if (!isVirtual) continue;

            if (!section.gameObject.activeSelf)
                section.gameObject.SetActive(true);

            // Hide "new item" highlight badges — virtual categories have no new-unlock context.
            if (section.highlightObj != null)
                section.highlightObj.gameObject.SetActive(false);

            // ── Layout reflow (no sibling reordering — vanilla order is preserved) ──
            var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();

            // Strip the extra bottom padding vanilla appends to the last section
            // (virtual categories manage layout manually).
            grid.padding = new RectOffset(
                grid.padding.left, grid.padding.right, grid.padding.top, 0);

            int columns = Mathf.Max(1, grid.constraintCount);
            int rows    = Mathf.Max(1, Mathf.CeilToInt((float)(remaining + 1) / columns));
            float gridH = grid.cellSize.y * rows
                        + grid.spacing.y  * (rows - 1)
                        + grid.padding.top + grid.padding.bottom;
            float sectionH = SectionHeader + gridH;

            var sectionRect = section.GetComponent<RectTransform>();
            sectionRect.localPosition = new Vector3(
                sectionRect.localPosition.x, yPos, sectionRect.localPosition.z);
            sectionRect.sizeDelta = new Vector2(sectionRect.sizeDelta.x, sectionH);

            var listRect = section.cosmeticListTransform.GetComponent<RectTransform>();
            listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, gridH);

            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);

            lastReflowedSection = section;
            yPos -= sectionH + SectionSpacing;
        }

        // Sort: clear button → favorites → modded → (hidden at end for SELECTED) → rest.
        // Runs for all non-virtual categories AND for all virtual tabs.
        //   FAV and HIDE  — bridge favorites appear before vanilla items.
        if (!isVirtual || isSearch || isSelected || isFav || isHide)
            SortFavoritesInCategory(__instance, hiddenAtEnd: isSelected);

        // Inject a dedicated World section after all other sections (virtual only).
        int worldCount = splitWorldFromHat
            ? InjectWorldSection(__instance, yPos,
                                 isSelected, isSearch, isFav, isHide, suppressHidden,
                                 applySearch, search,
                                 equippedSet, assetIndexMap, unlocksSet)
            : 0;
        totalVisible += worldCount;

        // Re-apply the sticky-header scroll padding to the true last section.
        if (isVirtual)
        {
            var lastSection = worldCount > 0
                ? __instance.sections[^1]
                : lastReflowedSection;
            ApplyStickyPadding(__instance, lastSection);
        }

        // Empty-state messages.
        if (isVirtual && totalVisible == 0)
        {
            string msg;
            if (isFav)
                msg = "Add a favorite with Ctrl+click :)";
            else if (isHide)
                msg = "Hide cosmetics with Alt+click :P";
            else if (isSearch)
                msg = string.IsNullOrWhiteSpace(CosmeticsMenuState.SearchText)
                    ? "Type to search cosmetics here :)"
                    : "No cosmetics found :'(";
            else
                msg = "Equip a cosmetic to see it here :3";

            ShowEmptyState(msg);
        }
        else
            HideEmptyState();

        if (isVirtual)
            RebuildScroll(__instance);
    }

    // ── Favorite / hidden / modded(rarity/border) sorting ──────────────────────────────────
    //
    // Sort keys applied in order:
    //   1. Group:   favorite(0)  |  normal(1)  |  hidden-at-end(3, SELECTED only)
    //   2. Lock:    unlocked(0)  |  locked(1)
    //   3. Origin:  bridge(0)    |  vanilla(1)   ← only when HighlightModdedCosmetics=true
    //   4. Sibling: vanilla rarity order (UltraRare → Rare → Uncommon → Common → name-asc)
    //
    // Bridge acts as a rarity tier above UltraRare
    //
    // Inactive buttons (truly hidden in the UI sense — not the user-hidden
    // concept) always go at the very end, after all visible buttons.
    private static void SortFavoritesInCategory(MenuPageCosmetics page,
                                                bool hiddenAtEnd = false)
    {
        bool hasFavs     = BridgeFavoritesManager.HasAnyFavorite();
        bool hasHidden   = hiddenAtEnd && BridgeFavoritesManager.HasAnyHidden();
        bool moddedFirst = Plugin.HighlightModdedCosmetics.Value;

        foreach (var section in page.sections)
        {
            if (section == null || section.cosmeticListTransform == null) continue;

            var allButtons = section.cosmeticListTransform
                .GetComponentsInChildren<MenuElementCosmeticButton>(includeInactive: true);

            // Update markers on every VISIBLE cosmetic button.
            foreach (var btn in allButtons)
                if (btn != null && btn.cosmeticAsset != null && btn.gameObject.activeSelf)
                    FavHideMarkerHelper.UpdateMarker(btn);

            // Nothing to reorder if none of the three conditions apply.
            if (!hasFavs && !hasHidden && !moddedFirst) continue;

            // Clear button = cosmeticAsset == null (first slot of the sectionPrefab).
            var clearButton     = allButtons.FirstOrDefault(b => b != null && b.cosmeticAsset == null);
            var cosmeticButtons = allButtons.Where(b => b != null && b.cosmeticAsset != null).ToArray();

            if (cosmeticButtons.Length == 0) continue;

            bool hasFavsHere = hasFavs && cosmeticButtons.Any(b =>
                b.gameObject.activeSelf && BridgeFavoritesManager.IsFavorite(b.cosmeticAsset));
            bool hasHiddenHere = hasHidden && cosmeticButtons.Any(b =>
                b.gameObject.activeSelf && BridgeFavoritesManager.IsHidden(b.cosmeticAsset));
            bool hasModdedHere = moddedFirst && cosmeticButtons.Any(b =>
                b.gameObject.activeSelf && BridgeIds.IsBridgeAsset(b.cosmeticAsset));

            // Nothing to do in this section — skip the rebuild.
            if (!hasFavsHere && !hasHiddenHere && !hasModdedHere) continue;

            var sorted = cosmeticButtons
                .Where(b => b.gameObject.activeSelf)
                .OrderBy(b =>
                {
                    if (BridgeFavoritesManager.IsFavorite(b.cosmeticAsset))            return 0;
                    if (hasHidden && BridgeFavoritesManager.IsHidden(b.cosmeticAsset)) return 3;
                    return 1;
                })
                .ThenBy(b => IsUnlocked(b) ? 0 : 1)
                .ThenBy(b => moddedFirst && BridgeIds.IsBridgeAsset(b.cosmeticAsset) ? 0 : 1)
                .ThenBy(b => b.transform.GetSiblingIndex())
                .ToArray();

            // Inactive (truly not shown) buttons go after all visible ones.
            var inactive = cosmeticButtons
                .Where(b => !b.gameObject.activeSelf)
                .OrderBy(b => b.transform.GetSiblingIndex())
                .ToArray();

            // Rebuild sibling order: clear → sorted visible → inactive.
            int si = 0;
            if (clearButton != null) clearButton.transform.SetSiblingIndex(si++);
            foreach (var btn in sorted)   btn.transform.SetSiblingIndex(si++);
            foreach (var btn in inactive) btn.transform.SetSiblingIndex(si++);

            // Rebuild the list rect so the GridLayoutGroup picks up the new order.
            var listRect = section.cosmeticListTransform?.GetComponent<RectTransform>();
            if (listRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        }
    }

    private static bool IsUnlocked(MenuElementCosmeticButton btn)
    {
        if (MetaManager.instance == null) return true;
        int idx = CosmeticsMenuState.GetAssetIndex(btn.cosmeticAsset);
        if (idx < 0) return true;
        return MetaManager.instance.cosmeticUnlocks.Contains(idx);
    }

    // ── Mirrors vanilla's sticky-header padding ───────────────────────────────
    private static void ApplyStickyPadding(MenuPageCosmetics page, MenuElementCosmeticSection? section)
    {
        if (section == null) return;
        var viewport = page.stickyHeader?.viewport;
        if (viewport == null) return;

        var sectionRect = section.GetComponent<RectTransform>();
        var listRect    = section.cosmeticListTransform?.GetComponent<RectTransform>();
        var grid        = section.cosmeticListTransform?.GetComponent<GridLayoutGroup>();
        if (sectionRect == null || listRect == null || grid == null) return;

        float extra = Mathf.Max(0f, viewport.rect.height - sectionRect.sizeDelta.y - 10f);
        if (extra <= 0f) return;

        grid.padding      = new RectOffset(grid.padding.left, grid.padding.right, grid.padding.top, (int)extra);
        listRect.sizeDelta    = new Vector2(listRect.sizeDelta.x,    listRect.sizeDelta.y    + extra);
        sectionRect.sizeDelta = new Vector2(sectionRect.sizeDelta.x, sectionRect.sizeDelta.y + extra);

        LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
    }

    // Creates a World section from scratch using vanilla prefabs, containing only
    // world assets that pass the current filter. Returns the number of items shown.
    private static int InjectWorldSection(
        MenuPageCosmetics page, float yPos,
        bool isSelected, bool isSearch, bool isFav, bool isHide, bool suppressHidden,
        bool applySearch, string search,
        HashSet<int> equippedSet, Dictionary<CosmeticAsset, int> assetIndexMap, HashSet<int> unlocksSet)
    {
        // Destroy any stale World section left from a previous refresh.
        for (int i = page.sections.Count - 1; i >= 0; i--)
        {
            if (page.sections[i] != null && page.sections[i].gameObject.name == WorldSectionName)
            {
                Object.Destroy(page.sections[i].gameObject);
                page.sections.RemoveAt(i);
            }
        }

        // World assets in vanilla order (same LINQ as vanilla: locked-last, rarity-desc, name-asc).
        var worldAssets = (from a in MetaManager.instance.cosmeticAssets
                           where a != null && a.prefab.IsValid()
                              && HhhCosmeticLoader.IsWorldAsset(a)
                              && Matches(a,
                                         isSelected, isSearch, isFav, isHide, suppressHidden,
                                         applySearch, search,
                                         equippedSet, assetIndexMap, unlocksSet)
                           orderby !unlocksSet.Contains(assetIndexMap.TryGetValue(a, out var idx) ? idx : -1),
                                   a.rarity descending,
                                   a.assetName
                           select a).ToList();

        // For SEARCH and SELECTED: additionally sort favorites first (and hidden last
        // for SELECTED), preserving vanilla order within each group via index.
        // All world assets are bridge assets, so no bridge/vanilla split is needed here.
        if (isSearch || isSelected)
        {
            bool hiddenAtEnd = isSelected;
            worldAssets = worldAssets
                .Select((a, i) => (a, i))
                .OrderBy(t => BridgeFavoritesManager.IsFavorite(t.a) ? 0
                            : (hiddenAtEnd && BridgeFavoritesManager.IsHidden(t.a) ? 3 : 1))
                .ThenBy(t => t.i)
                .Select(t => t.a)
                .ToList();
        }

        if (worldAssets.Count == 0) return 0;

        var sectionGO = Object.Instantiate(page.sectionPrefab, page.sectionRootTransform);
        sectionGO.name = WorldSectionName;
        var section    = sectionGO.GetComponent<MenuElementCosmeticSection>();
        section.subCategory = WorldSubCategory;

        if (section.headerText != null)
        {
            section.headerText.text = "WORLD";
            section.headerText.ForceMeshUpdate();
        }
        if (section.highlightObj != null)
            section.highlightObj.gameObject.SetActive(false);

        var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();

        foreach (var asset in worldAssets)
        {
            var btnGO = Object.Instantiate(page.sectionButtonPrefab, section.cosmeticListTransform);
            var btn   = btnGO.GetComponent<MenuElementCosmeticButton>();
            btn.cosmeticAsset = asset;
            FavHideMarkerHelper.UpdateMarker(btn);
        }

        int   count   = worldAssets.Count;
        int   columns = Mathf.Max(1, grid.constraintCount);
        int   rows    = Mathf.Max(1, Mathf.CeilToInt((float)(count + 1) / columns));
        float gridH   = grid.cellSize.y * rows
                      + grid.spacing.y  * (rows - 1)
                      + grid.padding.top + grid.padding.bottom;
        float sectionH = SectionHeader + gridH;

        var sectionRect = sectionGO.GetComponent<RectTransform>();
        sectionRect.localPosition = new Vector3(
            sectionRect.localPosition.x, yPos, sectionRect.localPosition.z);
        sectionRect.sizeDelta = new Vector2(sectionRect.sizeDelta.x, sectionH);

        var listRect = section.cosmeticListTransform.GetComponent<RectTransform>();
        listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, gridH);

        LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);

        page.sections.Add(section);
        return count;
    }

    private static void UpdateSearchFieldVisibility(bool isSearch)
    {
        var field = CosmeticsMenuState.SearchField;
        if (field == null) return;

        if (isSearch)
        {
            CosmeticsMenuState.SetSearchMode(true);
            field.gameObject.SetActive(true);
        }
        else
        {
            if (CosmeticsMenuState.SearchMode)
                CosmeticsMenuState.ClearSearch();
            field.gameObject.SetActive(false);
        }
    }

    private static bool Matches(
        CosmeticAsset asset,
        bool isSelected, bool isSearch, bool isFav, bool isHide, bool suppressHidden,
        bool applySearch, string search,
        HashSet<int> equippedSet, Dictionary<CosmeticAsset, int> assetIndexMap, HashSet<int> unlocksSet)
    {
        // SEARCH with empty field shows nothing — user hasn't typed yet.
        if (isSearch && !applySearch) return false;

        // assetIndexMap covers only vanilla-registered assets; bridge-only assets return -1.
        assetIndexMap.TryGetValue(asset, out int idx);

        // HIDE tab: show only hidden items.
        if (isHide) return BridgeFavoritesManager.IsHidden(asset);

        // FAV tab: show only favorites.
        if (isFav) return BridgeFavoritesManager.IsFavorite(asset);

        // Suppress hidden items everywhere except HIDE and SELECTED tabs.
        if (suppressHidden && BridgeFavoritesManager.IsHidden(asset)) return false;

        // SELECTED tab: show only equipped items.
        if (isSelected && !equippedSet.Contains(idx)) return false;

        // SEARCH only shows unlocked cosmetics; bridge-injected assets (idx == -1) always pass.
        if (isSearch && idx >= 0 && !unlocksSet.Contains(idx)) return false;

        if (applySearch)
        {
            // `search` is already lowercased by the caller — no extra allocation per button.
            string name = (asset.assetName ?? asset.name ?? "").ToLowerInvariant();
            if (!name.Contains(search)) return false;
        }

        return true;
    }

    private static Dictionary<CosmeticAsset, int> BuildAssetIndexMap(MetaManager meta)
    {
        var map = new Dictionary<CosmeticAsset, int>(meta.cosmeticAssets.Count);
        for (int i = 0; i < meta.cosmeticAssets.Count; i++)
        {
            var a = meta.cosmeticAssets[i];
            if (a != null) map[a] = i;
        }
        return map;
    }

    private static void ShowEmptyState(string message)
    {
        var go = CosmeticsMenuState.EmptyStateLabel;
        if (go == null) return;
        var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null) tmp.text = message;
        if (!go.activeSelf) go.SetActive(true);
    }

    private static void HideEmptyState()
    {
        var go = CosmeticsMenuState.EmptyStateLabel;
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    private static void RebuildScroll(MenuPageCosmetics page)
    {
        var scrollRect = page.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect?.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
    }

}
