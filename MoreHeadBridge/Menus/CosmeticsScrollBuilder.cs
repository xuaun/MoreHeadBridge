// Builds cosmetics-menu scroll content from scratch for the virtual categories, the World category, and the vanilla tabs (FixCosmeticsMenuPerformance).
//
// Each Build* fully replaces RefreshScrollContent for its tab: destroy sections, rebuild, populate, layout, sort, scroll.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class CosmeticsScrollBuilder
{
    // ── Vanilla private members accessed via reflection ───────────────────────
    // Null when the corresponding field/method doesn't exist — callers use ?. safely.
    private static readonly MethodInfo? RecalculateScrollHeightAfterFrame =
        AccessTools.Method(typeof(MenuPageCosmetics), "RecalculateScrollHeightAfterFrame");
    private static readonly FieldInfo? TopDividerRestingPositionEnd =
        AccessTools.Field(typeof(MenuPageCosmetics), "topDividerRestingPositionEnd");
    private static readonly FieldInfo? TopDividerRestingPositionEndNew =
        AccessTools.Field(typeof(MenuPageCosmetics), "topDividerRestingPositionEndNew");

    // ── Routing ───────────────────────────────────────────────────────────────

    /// True when our build path replaces vanilla RefreshScrollContent on a normal tab: FixCosmeticsMenuPerformance on, OR variant grouping on (vanilla's own build path would never group).
    internal static bool ShouldUseVanillaPerfPath(CosmeticCategoryAsset? category)
        => (Plugin.FixCosmeticsMenuPerformance.Value || CosmeticGrouping.Enabled)
           && IsVanillaTabCategory(category);

    // Category sort order: unlocked first, then rarity descending, then name (ordinal).
    private static int CompareForMenu(CosmeticAsset a, int aIndex, CosmeticAsset b, int bIndex, HashSet<int> unlocksSet)
    {
        int locked = (!unlocksSet.Contains(aIndex)).CompareTo(!unlocksSet.Contains(bIndex));
        if (locked != 0) return locked;
        int rarity = b.rarity.CompareTo(a.rarity);
        if (rarity != 0) return rarity;
        return string.Compare(a.assetName, b.assetName, System.StringComparison.Ordinal);
    }

    // ── Build paths ───────────────────────────────────────────────────────────

    internal static void BuildVirtualScrollContent(MenuPageCosmetics page)
    {
        PreparePageForCustomBuild(page);
        CosmeticsSearchHelper.UpdateSearchFieldVisibility(
            CosmeticsMenuState.IsSearch(page.selectedCategory));

        BridgeFavoritesManager.EnsureLoaded();

        var selected = page.selectedCategory;
        bool isSelected = CosmeticsMenuState.IsSelected(selected);
        bool isSearch   = CosmeticsMenuState.IsSearch(selected);
        bool isFav      = CosmeticsMenuState.IsFavCategory(selected);
        bool isHide     = CosmeticsMenuState.IsHideCategory(selected);

        string search      = CosmeticsSearchHelper.FoldText(CosmeticsMenuState.SearchText?.Trim() ?? "");
        bool   applySearch = isSearch && search.Length > 0;
        bool   suppressHidden = !isHide && !isSelected && BridgeFavoritesManager.HasAnyHidden();

        if (MetaManager.instance == null || selected?.typeList == null)
        {
            HideEmptyState();
            StartRecalculate(page);
            return;
        }

        var equippedSet = new HashSet<int>(MetaManager.instance.cosmeticEquipped);
        var unlocksSet  = new HashSet<int>(MetaManager.instance.cosmeticUnlocks);

        float yPos        = 0f;
        int   totalVisible = 0;
        MenuElementCosmeticSection? lastSection = null;

        bool splitWorldFromHat = HhhCosmeticLoader.WorldAssetIds.Count > 0;
        var  wantedTypes = new HashSet<SemiFunc.CosmeticType>(selected.typeList);
        var  byType      = new Dictionary<SemiFunc.CosmeticType, List<(CosmeticAsset Asset, int Index)>>();

        var assets = MetaManager.instance.cosmeticAssets;
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            if (asset == null || !asset.prefab.IsValid()) continue;
            if (!wantedTypes.Contains(asset.type)) continue;
            if (splitWorldFromHat && HhhCosmeticLoader.IsWorldAsset(asset)) continue;
            if (!CosmeticsFilterPatch.Matches(asset, isSelected, isSearch, isFav, isHide,
                                              suppressHidden, applySearch, search,
                                              equippedSet, unlocksSet))
                continue;

            if (!byType.TryGetValue(asset.type, out var list))
                byType[asset.type] = list = new List<(CosmeticAsset, int)>();
            list.Add((asset, i));
        }

        foreach (var type in selected.typeList)
        {
            if (!byType.TryGetValue(type, out var list) || list.Count == 0)
                continue;

            list.Sort((a, b) => CompareForMenu(a.Asset, a.Index, b.Asset, b.Index, unlocksSet));

            var section = CreateSection(page, type, GetTypeLabel(type), list.Select(x => x.Asset), yPos);
            if (section == null) continue;

            page.sections.Add(section);
            CreateSubCategoryButton(page, type, GetTypeLabel(type));
            lastSection    = section;
            totalVisible  += list.Count;
            yPos -= section.GetComponent<RectTransform>().sizeDelta.y + CosmeticsFilterPatch.SectionSpacing;
        }

        if (isSearch || isSelected || isFav || isHide)
            CosmeticsSortHelper.SortFavoritesInCategory(page, hiddenAtEnd: isSelected, skipRebuild: true);

        int worldCount = splitWorldFromHat
            ? InjectWorldSection(page, yPos, isSelected, isSearch, isFav, isHide,
                                 suppressHidden, applySearch, search, equippedSet, unlocksSet)
            : 0;
        totalVisible += worldCount;

        ApplyStickyPadding(page, worldCount > 0 ? page.sections[^1] : lastSection);

        if (totalVisible == 0)
        {
            string msg = isFav  ? "Add a favorite with Ctrl+click :)"
                : isHide        ? "Hide cosmetics with Alt+click :P"
                : isSearch      ? (string.IsNullOrWhiteSpace(CosmeticsMenuState.SearchText)
                                    ? "Type to search cosmetics here :)"
                                    : "No cosmetics found :'(")
                :                 "Equip a cosmetic to see it here :3";
            ShowEmptyState(msg);
        }
        else
            HideEmptyState();

        RebuildScroll(page);
        StartRecalculate(page);
    }

    internal static void BuildWorldScrollContent(MenuPageCosmetics page)
    {
        PreparePageForCustomBuild(page);
        CosmeticsSearchHelper.UpdateSearchFieldVisibility(false);
        BridgeFavoritesManager.EnsureLoaded();

        if (MetaManager.instance == null)
        {
            HideEmptyState();
            StartRecalculate(page);
            return;
        }

        var  unlocksSet    = new HashSet<int>(MetaManager.instance.cosmeticUnlocks);
        bool suppressHidden = BridgeFavoritesManager.HasAnyHidden();

        var worldAssets = MetaManager.instance.cosmeticAssets
            .Where(a => a != null
                     && a.prefab.IsValid()
                     && HhhCosmeticLoader.IsWorldAsset(a)
                     && (!suppressHidden || !BridgeFavoritesManager.IsHidden(a)))
            .OrderBy(a => a.assetId != MiniSemibotCosmetic.AssetId) // Mini-Semibot pinned first
            .ThenBy(a => !unlocksSet.Contains(CosmeticsMenuState.GetAssetIndex(a)))
            .ThenByDescending(a => a.rarity)
            .ThenBy(a => a.assetName)
            .ToList();

        if (worldAssets.Count > 0)
        {
            var section = CreateSection(page, CosmeticsFilterPatch.WorldSubCategory,
                                        "WORLD", worldAssets, 0f);
            if (section != null)
            {
                page.sections.Add(section);
                CreateSubCategoryButton(page, CosmeticsFilterPatch.WorldSubCategory, "WORLD");
                CosmeticsSortHelper.SortFavoritesInCategory(page, skipRebuild: true);
                ApplyStickyPadding(page, section);
            }
        }

        HideEmptyState();
        RebuildScroll(page);
        StartRecalculate(page);
    }

    internal static void BuildVanillaScrollContent(MenuPageCosmetics page)
    {
        PreparePageForCustomBuild(page);
        CosmeticsSearchHelper.UpdateSearchFieldVisibility(false);
        BridgeFavoritesManager.EnsureLoaded();

        var selected = page.selectedCategory;
        if (MetaManager.instance == null || selected?.typeList == null)
        {
            HideEmptyState();
            StartRecalculate(page);
            return;
        }

        var  meta          = MetaManager.instance;
        var  unlocksSet    = new HashSet<int>(meta.cosmeticUnlocks);
        bool suppressHidden = BridgeFavoritesManager.HasAnyHidden();

        float yPos        = 0f;
        MenuElementCosmeticSection? lastSection = null;

        foreach (var subCategory in selected.typeList)
        {
            var list = new List<(CosmeticAsset Asset, int Index)>();
            for (int i = 0; i < meta.cosmeticAssets.Count; i++)
            {
                var asset = meta.cosmeticAssets[i];
                if (asset == null || asset.type != subCategory || !asset.prefab.IsValid()) continue;
                if (subCategory == SemiFunc.CosmeticType.Hat && HhhCosmeticLoader.IsWorldAsset(asset)) continue;
                if (suppressHidden && BridgeFavoritesManager.IsHidden(asset)) continue;
                list.Add((asset, i));
            }

            bool tintable = HasTintableMaterial(page, subCategory);
            if (list.Count == 0 && !tintable) continue;

            list.Sort((a, b) => CompareForMenu(a.Asset, a.Index, b.Asset, b.Index, unlocksSet));

            string label = GetTypeLabel(subCategory);
            CreateSubCategoryButton(page, subCategory, label);

            var section = CreateVanillaLikeSection(page, subCategory, label,
                                                   list.Select(x => x.Asset), yPos);
            page.sections.Add(section);
            lastSection = section;
            yPos -= section.GetComponent<RectTransform>().sizeDelta.y + CosmeticsFilterPatch.SectionSpacing;
        }

        CosmeticsSortHelper.SortFavoritesInCategory(page, skipRebuild: true);
        ApplyStickyPadding(page, lastSection);
        HideEmptyState();
        RebuildScroll(page);
        StartRecalculate(page);
    }

    // ── Section factory ───────────────────────────────────────────────────────

    /// Creates a minimal section for virtual / world categories (no highlight badge).
    /// Returns null when assetList is empty.
    internal static MenuElementCosmeticSection? CreateSection(
        MenuPageCosmetics page,
        SemiFunc.CosmeticType subCategory,
        string label,
        IEnumerable<CosmeticAsset> assets,
        float yPos)
    {
        var assetList = assets.ToList();
        if (assetList.Count == 0) return null;

        var sectionGO = Object.Instantiate(page.sectionPrefab, page.sectionRootTransform);
        sectionGO.transform.localPosition = new Vector3(
            sectionGO.transform.localPosition.x, yPos, sectionGO.transform.localPosition.z);

        var section = sectionGO.GetComponent<MenuElementCosmeticSection>();
        section.subCategory = subCategory;
        if (section.headerText != null)
        {
            section.headerText.text = label;
            section.headerText.ForceMeshUpdate();
        }
        if (section.highlightObj != null)
            section.highlightObj.gameObject.SetActive(false);

        var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(grid.padding.left, grid.padding.right, grid.padding.top, 0);

        int shown = PopulateSectionButtons(page, section, assetList);
        ApplySectionSize(section, shown);
        return section;
    }

    private static int PopulateSectionButtons(
        MenuPageCosmetics page, MenuElementCosmeticSection section, IEnumerable<CosmeticAsset> assets)
    {
        var collapsed = CosmeticGrouping.Collapse(assets);
        foreach (var (rep, members) in collapsed)
        {
            var btnGO = Object.Instantiate(page.sectionButtonPrefab, section.cosmeticListTransform);
            var btn   = btnGO.GetComponent<MenuElementCosmeticButton>();
            btn.cosmeticAsset = rep;
            if (members != null && members.Count > 1)
                CosmeticGroupButton.Attach(btn, members);
            FavHideMarkerHelper.UpdateMarker(btn);
        }
        return collapsed.Count;
    }

    /// Creates a vanilla-style section that preserves the highlight badge position.
    /// Used by BuildVanillaScrollContent.
    private static MenuElementCosmeticSection CreateVanillaLikeSection(
        MenuPageCosmetics page,
        SemiFunc.CosmeticType subCategory,
        string label,
        IEnumerable<CosmeticAsset> assets,
        float yPos)
    {
        var assetList = assets.ToList();
        var sectionGO = Object.Instantiate(page.sectionPrefab, page.sectionRootTransform);
        sectionGO.transform.localPosition = new Vector3(
            sectionGO.transform.localPosition.x, yPos, sectionGO.transform.localPosition.z);

        var section = sectionGO.GetComponent<MenuElementCosmeticSection>();
        section.subCategory = subCategory;
        if (section.headerText != null)
        {
            section.headerText.text = label;
            section.headerText.ForceMeshUpdate();
        }

        var highlightRect = section.highlightObj?.GetComponent<RectTransform>();
        if (highlightRect != null && section.headerText != null)
        {
            highlightRect.anchoredPosition = new Vector2(
                section.headerText.rectTransform.anchoredPosition.x
                + section.headerText.textBounds.max.x + 15f,
                highlightRect.anchoredPosition.y);
        }

        int shown = PopulateSectionButtons(page, section, assetList);
        ApplySectionSize(section, shown);
        return section;
    }

    // ── World section injection ────────────────────────────────────────────────

    /// Creates a World section at the bottom of the scroll view for virtual tabs.
    /// Destroys any stale World section left from a previous refresh.
    /// Returns the number of world items shown.
    internal static int InjectWorldSection(
        MenuPageCosmetics page, float yPos,
        bool isSelected, bool isSearch, bool isFav, bool isHide, bool suppressHidden,
        bool applySearch, string search,
        HashSet<int> equippedSet, HashSet<int> unlocksSet)
    {
        // Destroy stale World section from a previous refresh.
        for (int i = page.sections.Count - 1; i >= 0; i--)
        {
            if (page.sections[i] != null
                && page.sections[i].gameObject.name == CosmeticsFilterPatch.WorldSectionName)
            {
                Object.Destroy(page.sections[i].gameObject);
                page.sections.RemoveAt(i);
            }
        }

        var worldAssets = (from a in MetaManager.instance.cosmeticAssets
                           where a != null && a.prefab.IsValid()
                              && HhhCosmeticLoader.IsWorldAsset(a)
                              && CosmeticsFilterPatch.Matches(a,
                                     isSelected, isSearch, isFav, isHide, suppressHidden,
                                     applySearch, search, equippedSet, unlocksSet)
                           orderby a.assetId != MiniSemibotCosmetic.AssetId, // Mini-Semibot pinned first
                                   !unlocksSet.Contains(CosmeticsMenuState.GetAssetIndex(a)),
                                   a.rarity descending,
                                   a.assetName
                           select a).ToList();

        // SEARCH / SELECTED: sort favorites first (hidden last for SELECTED).
        if (isSearch || isSelected)
        {
            bool hiddenAtEnd = isSelected;
            worldAssets = worldAssets
                .Select((a, i) => (a, i))
                .OrderBy(t => BridgeFavoritesManager.IsFavorite(t.a)                       ? 0
                            : (hiddenAtEnd && BridgeFavoritesManager.IsHidden(t.a) ? 3 : 1))
                .ThenBy(t => t.i)
                .Select(t => t.a)
                .ToList();
        }

        if (worldAssets.Count == 0) return 0;

        var sectionGO  = Object.Instantiate(page.sectionPrefab, page.sectionRootTransform);
        sectionGO.name = CosmeticsFilterPatch.WorldSectionName;
        var section    = sectionGO.GetComponent<MenuElementCosmeticSection>();
        section.subCategory = CosmeticsFilterPatch.WorldSubCategory;

        if (section.headerText != null)
        {
            section.headerText.text = "WORLD";
            section.headerText.ForceMeshUpdate();
        }
        if (section.highlightObj != null)
            section.highlightObj.gameObject.SetActive(false);

        int shownWorld = PopulateSectionButtons(page, section, worldAssets);
        ApplySectionSize(section, shownWorld);

        var sectionRect = sectionGO.GetComponent<RectTransform>();
        sectionRect.localPosition = new Vector3(
            sectionRect.localPosition.x, yPos, sectionRect.localPosition.z);

        page.sections.Add(section);
        CreateSubCategoryButton(page, CosmeticsFilterPatch.WorldSubCategory, "WORLD");
        return worldAssets.Count;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static void ShowEmptyState(string message)
    {
        var go = CosmeticsMenuState.EmptyStateLabel;
        if (go == null) return;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = message;
        if (!go.activeSelf) go.SetActive(true);
    }

    internal static void HideEmptyState()
    {
        var go = CosmeticsMenuState.EmptyStateLabel;
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    internal static void RebuildScroll(MenuPageCosmetics page)
    {
        var scrollRect = page.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect?.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
    }

    internal static void CreateSubCategoryButton(
        MenuPageCosmetics page, SemiFunc.CosmeticType subCategory, string label)
    {
        var obj = Object.Instantiate(page.categoryButtonPrefab, page.subCategoriesTransform);
        var btn = obj.GetComponent<MenuElementButtonCosmeticCategory>();
        btn.subCategory = subCategory;
        btn.buttonType  = MenuElementButtonCosmeticCategory.ButtonType.SubCategory;

        if (btn.canvasGroup != null)
            btn.canvasGroup.alpha = 0f;

        var tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.fontSize = 16f;
            tmp.text     = label;
        }
    }

    internal static void ApplyStickyPadding(MenuPageCosmetics page, MenuElementCosmeticSection? section)
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
        // RebuildScroll (called after ApplyStickyPadding for all custom build paths) handles layout.
    }

    internal static string GetTypeLabel(SemiFunc.CosmeticType type)
    {
        if (MetaManager.instance != null)
        {
            var typeAsset = MetaManager.instance.cosmeticTypeAssets
                .FirstOrDefault(x => x != null && x.type == type);
            if (typeAsset != null)
                return typeAsset.localizedName?.GetLocalizedString() ?? typeAsset.typeName;
        }
        return type.ToString();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    // Resets page state shared by all three Build* paths.
    private static void PreparePageForCustomBuild(MenuPageCosmetics page)
    {
        page.subCategoriesReady = false;

        var scrollerGroup = page.menuScrollBox?.scroller?.GetComponent<CanvasGroup>();
        if (scrollerGroup != null)
            scrollerGroup.alpha = 0f;

        foreach (Transform child in page.subCategoriesTransform)
            Object.Destroy(child.gameObject);

        foreach (var section in page.sections.ToList())
            if (section != null) Object.Destroy(section.gameObject);
        page.sections.Clear();

        page.scrollGradientTopHidden = true;
        if (TopDividerRestingPositionEnd?.GetValue(page) is Vector2 topEnd)
            TopDividerRestingPositionEndNew?.SetValue(page, new Vector2(topEnd.x, 311f));
        if (page.scrollGradientTopCanvasGroup != null)
        {
            var rt = page.scrollGradientTopCanvasGroup.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -133f);
        }
    }

    private static void ApplySectionSize(MenuElementCosmeticSection section, int count)
    {
        var grid    = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
        int columns = Mathf.Max(1, grid.constraintCount);
        int rows    = Mathf.Max(1, Mathf.CeilToInt((float)(count + 1) / columns));
        float gridH = grid.cellSize.y * rows
                    + grid.spacing.y  * (rows - 1)
                    + grid.padding.top + grid.padding.bottom;
        float sectionH = CosmeticsFilterPatch.SectionHeader + gridH;

        var sectionRect = section.GetComponent<RectTransform>();
        sectionRect.sizeDelta = new Vector2(sectionRect.sizeDelta.x, sectionH);

        var listRect = section.cosmeticListTransform.GetComponent<RectTransform>();
        listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, gridH);
    }

    private static void StartRecalculate(MenuPageCosmetics page)
    {
        if (RecalculateScrollHeightAfterFrame?.Invoke(page, null) is IEnumerator routine)
            page.StartCoroutine(routine);
    }

    private static bool IsVanillaTabCategory(CosmeticCategoryAsset? category)
    {
        if (category?.typeList == null) return false;
        if (CosmeticsMenuState.IsVirtual(category)) return false;
        if (WorldCosmeticsMenuState.IsWorldCategory(category)) return false;
        if (CosmeticsMenuState.IsPresetsCategory(category)) return false;
        return category.typeList.Any(t => VanillaTabTypes.Contains(t));
    }

    private static readonly HashSet<SemiFunc.CosmeticType> VanillaTabTypes = new()
    {
        SemiFunc.CosmeticType.Hat,
        SemiFunc.CosmeticType.HeadBottom,
        SemiFunc.CosmeticType.Ears,
        SemiFunc.CosmeticType.Eyewear,
        SemiFunc.CosmeticType.FaceTop,
        SemiFunc.CosmeticType.FaceBottom,
        SemiFunc.CosmeticType.BodyTop,
        SemiFunc.CosmeticType.BodyBottom,
        SemiFunc.CosmeticType.BodyTopOverlay,
        SemiFunc.CosmeticType.BodyBottomOverlay,
        SemiFunc.CosmeticType.ArmRight,
        SemiFunc.CosmeticType.ArmLeft,
        SemiFunc.CosmeticType.ArmRightOverlay,
        SemiFunc.CosmeticType.ArmLeftOverlay,
        SemiFunc.CosmeticType.LegRight,
        SemiFunc.CosmeticType.LegLeft,
        SemiFunc.CosmeticType.FootRight,
        SemiFunc.CosmeticType.FootLeft,
        SemiFunc.CosmeticType.LegRightOverlay,
        SemiFunc.CosmeticType.LegLeftOverlay,
    };

    private static bool HasTintableMaterial(MenuPageCosmetics page, SemiFunc.CosmeticType subCategory)
    {
        if (page.menuPage?.playerAvatarMenu == null) return false;
        foreach (var pm in page.menuPage.playerAvatarMenu.playerCosmetics.playerMaterials)
        {
            if (pm != null && pm.cosmeticType == subCategory && pm.tintable)
                return true;
        }
        return false;
    }
}
