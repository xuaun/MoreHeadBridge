using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

// Shared runtime state for cosmetics-menu enhancements.
// Owns the two virtual category ScriptableObjects (SEARCH, SELECTED),
// the hover tooltip label, the search input field, and search mode.
internal static class CosmeticsMenuState
{
    // ── Virtual categories ──────────────────────────────────────────────────

    internal static CosmeticCategoryAsset? SelectedCategory { get; private set; }
    internal static CosmeticCategoryAsset? SearchCategory   { get; private set; }

    // True for any category this mod injected (not a vanilla one).
    internal static bool IsVirtual(CosmeticCategoryAsset? c) =>
        c != null && (c == SelectedCategory || c == SearchCategory);

    internal static bool IsSelected(CosmeticCategoryAsset? c) => c != null && c == SelectedCategory;
    internal static bool IsSearch(CosmeticCategoryAsset? c)   => c != null && c == SearchCategory;

    internal static void EnsureCategories()
    {
        SelectedCategory ??= MakeCategory("MHB_Selected", "SELECTED");
        SearchCategory   ??= MakeCategory("MHB_Search",   "SEARCH");
    }

    // ── UI elements injected into MenuPageCosmetics ─────────────────────────

    // Thin bar just above scroll content: shows hovered cosmetic name / "Locked".
    internal static TextMeshProUGUI? StatusLabel { get; private set; }

    // Input field shown when SEARCH tab is active.
    internal static TMP_InputField? SearchField { get; private set; }

    // "No items selected" text shown when SELECTED is active and nothing is equipped.
    internal static GameObject? EmptyStateLabel { get; private set; }

    internal static void SetStatusLabel(TextMeshProUGUI? v)   => StatusLabel = v;
    internal static void SetSearchField(TMP_InputField? v)    => SearchField = v;
    internal static void SetEmptyStateLabel(GameObject? v)    => EmptyStateLabel = v;

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

    // Call instead of ActivePage.RefreshScrollContent() from the search listener.
    // Cancels any pending refresh and schedules a new one after a short delay.
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

    // ── Helpers ─────────────────────────────────────────────────────────────

    // Creates a virtual CosmeticCategoryAsset that includes every cosmetic type
    // so vanilla's Start() filter (which skips categories with no unlocked types) passes.
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
