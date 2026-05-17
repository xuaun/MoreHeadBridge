// Postfix on MenuPageCosmetics.RefreshScrollContent.
// Filters buttons and sections for the SEARCH and SELECTED virtual categories,
// and injects a dedicated World section after all others.

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
    // Using a value outside the vanilla enum range avoids sticky-header collisions with Hat.
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
        if (IsPresetsCategory(selected))
        {
            UpdateSearchFieldVisibility(false);
            HideEmptyState();
            return;
        }

        bool isSelected  = CosmeticsMenuState.IsSelected(selected);
        bool isSearch    = CosmeticsMenuState.IsSearch(selected);
        bool isVirtual   = CosmeticsMenuState.IsVirtual(selected);
        string search    = CosmeticsMenuState.SearchText?.Trim() ?? "";
        bool applySearch = search.Length > 0;

        // Manage search field visibility and SearchMode flag.
        UpdateSearchFieldVisibility(isSearch);

        // For vanilla categories with no active search: nothing to do.
        if (!isVirtual && !applySearch)
        {
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

        // Show sub-category buttons for vanilla categories; hide all for virtual
        // (virtual categories don't use sub-navigation).
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

                bool show = isHatSection && HhhCosmeticLoader.IsWorldAsset(btn.cosmeticAsset)
                    ? false
                    : Matches(btn.cosmeticAsset, isSelected, isSearch, applySearch, search,
                              equippedSet, assetIndexMap, unlocksSet);

                if (btn.gameObject.activeSelf != show)
                    btn.gameObject.SetActive(show);

                if (!show) removedCount++;
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

            var grid    = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();

            // Vanilla appends extra bottom padding to the last section so the sticky
            // header has room to scroll. Virtual categories manage layout manually and
            // don't use that mechanism — strip it so WORLD isn't pushed down by the gap.
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

        // Inject a dedicated World section after all other sections.
        int worldCount = splitWorldFromHat
            ? InjectWorldSection(__instance, yPos, isSelected, isSearch, applySearch, search,
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

        if (isVirtual && totalVisible == 0)
        {
            string msg = isSearch
                ? (string.IsNullOrWhiteSpace(CosmeticsMenuState.SearchText)
                    ? "Type to search cosmetics here :)"
                    : "No cosmetics found :'(")
                : "Equip a cosmetic to see it here :3";
            ShowEmptyState(msg);
        }
        else
            HideEmptyState();

        if (isVirtual)
            RebuildScroll(__instance);
    }

    // Mirrors vanilla's sticky-header padding
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

    // Creates a World section from scratch using the vanilla prefabs, containing only
    // world assets that pass the current filter. Returns the number of items shown.
    private static int InjectWorldSection(
        MenuPageCosmetics page, float yPos,
        bool isSelected, bool isSearch, bool applySearch, string search,
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

        var worldAssets = MetaManager.instance.cosmeticAssets
            .Where(a => a != null && a.prefab.IsValid()
                     && HhhCosmeticLoader.IsWorldAsset(a)
                     && Matches(a, isSelected, isSearch, applySearch, search,
                                equippedSet, assetIndexMap, unlocksSet))
            .ToList();

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
            Object.Instantiate(page.sectionButtonPrefab, section.cosmeticListTransform)
                  .GetComponent<MenuElementCosmeticButton>().cosmeticAsset = asset;

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
        bool isSelected, bool isSearch, bool applySearch, string search,
        HashSet<int> equippedSet, Dictionary<CosmeticAsset, int> assetIndexMap, HashSet<int> unlocksSet)
    {
        // SEARCH with empty field shows nothing — user hasn't typed yet.
        if (isSearch && !applySearch) return false;

        // assetIndexMap covers only vanilla-registered assets; bridge-only assets return -1.
        assetIndexMap.TryGetValue(asset, out int idx);

        if (isSelected && !equippedSet.Contains(idx)) return false;

        // SEARCH only shows unlocked cosmetics; bridge-injected assets (idx == -1) are always visible.
        if (isSearch && idx >= 0 && !unlocksSet.Contains(idx)) return false;

        if (applySearch)
        {
            string name = (asset.assetName ?? asset.name ?? "").ToLowerInvariant();
            if (!name.Contains(search.ToLowerInvariant())) return false;
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

    private static bool IsPresetsCategory(CosmeticCategoryAsset? cat)
    {
        if (cat == null) return false;
        string name = (cat.categoryName ?? cat.name ?? "").ToUpperInvariant();
        return name.Contains("PRESET") || name.Contains("OUTFIT");
    }
}
