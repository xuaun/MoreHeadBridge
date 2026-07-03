using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Shared runtime state for the cosmetics-menu enhancements: the two virtual category ScriptableObjects (SEARCH, SELECTED), hover tooltip, search field and search mode.
internal static class CosmeticsMenuState
{
    // ── Virtual categories ──────────────────────────────────────────────────

    internal static CosmeticCategoryAsset? SelectedCategory  { get; private set; }
    internal static CosmeticCategoryAsset? SearchCategory    { get; private set; }
    internal static CosmeticCategoryAsset? FavoritesCategory { get; private set; }
    internal static CosmeticCategoryAsset? HiddenCategory    { get; private set; }

    // True for any category this mod injected (not a vanilla one).
    internal static bool IsVirtual(CosmeticCategoryAsset? c) =>
        c != null && (c == SelectedCategory || c == SearchCategory
                   || c == FavoritesCategory || c == HiddenCategory);

    internal static bool IsSelected    (CosmeticCategoryAsset? c) => c != null && c == SelectedCategory;
    internal static bool IsSearch      (CosmeticCategoryAsset? c) => c != null && c == SearchCategory;
    internal static bool IsFavCategory (CosmeticCategoryAsset? c) => c != null && c == FavoritesCategory;
    internal static bool IsHideCategory(CosmeticCategoryAsset? c) => c != null && c == HiddenCategory;

    internal static void EnsureCategories()
    {
        SelectedCategory  ??= MakeCategory("MHB_Selected",  "SELECTED");
        SearchCategory    ??= MakeCategory("MHB_Search",    "SEARCH");
        FavoritesCategory ??= MakeCategory("MHB_Favorites", "FAV");
        HiddenCategory    ??= MakeCategory("MHB_Hidden",    "HIDE");
    }

    // ── UI elements injected into MenuPageCosmetics ─────────────────────────

    // Thin bar just above scroll content: shows hovered cosmetic name / "Locked".
    internal static TextMeshProUGUI? StatusLabel      { get; private set; }
    // CanvasGroup on the panel — lets LateUpdate fade both background and text as one unit.
    internal static CanvasGroup?     StatusLabelGroup { get; private set; }

    // Input field shown when SEARCH tab is active.
    internal static TMP_InputField? SearchField { get; private set; }

    // "No items selected" text shown when SELECTED is active and nothing is equipped.
    internal static GameObject? EmptyStateLabel { get; private set; }

    internal static void SetStatusLabel(TextMeshProUGUI? v)  => StatusLabel = v;
    internal static void SetStatusLabelGroup(CanvasGroup? v) => StatusLabelGroup = v;
    internal static void SetSearchField(TMP_InputField? v)   => SearchField = v;
    internal static void SetEmptyStateLabel(GameObject? v)   => EmptyStateLabel = v;

    // ── Search state ────────────────────────────────────────────────────────

    internal static bool   SearchMode { get; private set; }
    internal static string SearchText { get; private set; } = "";

    internal static void SetSearchMode(bool v)   => SearchMode = v;
    internal static void SetSearchText(string v) => SearchText = v;

    internal static void ClearSearch()
    {
        SearchText = "";
        SearchMode = false;
        if (SearchField != null) SearchField.SetTextWithoutNotify("");
    }

    // ── Search debounce ─────────────────────────────────────────────────────

    private static Coroutine? _searchDebounce;
    private const float SearchDebounceDelay = 0.25f;

    // Call instead of ActivePage.RefreshScrollContent() from the search listener — cancels any pending refresh and schedules a new one after a short delay.
    internal static void ScheduleSearchRefresh()
    {
        var page = ActivePage;
        if (page == null) return;
        if (_searchDebounce != null)
            page.StopCoroutine(_searchDebounce);
        _searchDebounce = page.StartCoroutine(SearchRefreshCoroutine(page));
    }

    private static IEnumerator SearchRefreshCoroutine(MenuPageCosmetics page)
    {
        yield return new WaitForSeconds(SearchDebounceDelay);
        _searchDebounce = null;
        page.RefreshScrollContent();
    }

    // ── Active page ─────────────────────────────────────────────────────────

    internal static MenuPageCosmetics? ActivePage { get; private set; }
    internal static void SetActivePage(MenuPageCosmetics? v) => ActivePage = v;

    // Call from the MenuPageCosmetics.OnDestroy patch to avoid stale references; stops any pending search debounce on the dying page before nulling it.
    internal static void OnMenuClosed()
    {
        if (_searchDebounce != null && ActivePage != null)
            ActivePage.StopCoroutine(_searchDebounce);
        _searchDebounce  = null;
        ActivePage       = null;
        StatusLabelGroup = null; // destroyed with the page
        _assetIndexCache = null; // invalidate on menu close (MetaManager may reload)
        SearchText             = "";   // discard stale query so next open starts clean
        SearchMode             = false;
    }

    // ── Asset-index cache ────────────────────────────────────────────────────
    // Built lazily on first call per menu session and reused every LateUpdate frame instead of O(n) List.IndexOf per hovered cosmetic.

    private static Dictionary<CosmeticAsset, int>? _assetIndexCache;

    // Returns the index of `asset` in MetaManager.cosmeticAssets, or -1. Cached for the current menu session.
    internal static int GetAssetIndex(CosmeticAsset asset)
    {
        if (MetaManager.instance == null) return -1;

        if (_assetIndexCache == null)
        {
            var list = MetaManager.instance.cosmeticAssets;
            _assetIndexCache = new Dictionary<CosmeticAsset, int>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    _assetIndexCache[list[i]] = i;
            }
        }

        return _assetIndexCache.TryGetValue(asset, out int idx) ? idx : -1;
    }

    // ── Shared predicates ────────────────────────────────────────────────────

    // True for the vanilla PRESETS / OUTFITS tab — used by patches to decide whether to skip cosmetics-specific logic.
    internal static bool IsPresetsCategory(CosmeticCategoryAsset? cat)
    {
        if (cat == null) return false;
        string name = (cat.categoryName ?? cat.name ?? "").ToUpperInvariant();
        return name.Contains("PRESET") || name.Contains("OUTFIT");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // Creates a virtual CosmeticCategoryAsset including every cosmetic type so vanilla's Start() filter (skips categories with no unlocked types) passes.
    private static CosmeticCategoryAsset MakeCategory(string id, string label)
    {
        var cat = ScriptableObject.CreateInstance<CosmeticCategoryAsset>();
        cat.name         = id;
        cat.categoryName = label;
        cat.typeList     = Enum.GetValues(typeof(SemiFunc.CosmeticType))
                               .Cast<SemiFunc.CosmeticType>()
                               .ToList();
        return cat;
    }
}
