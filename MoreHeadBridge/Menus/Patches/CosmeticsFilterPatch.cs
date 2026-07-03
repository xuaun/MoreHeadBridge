// Prefix + Postfix on MenuPageCosmetics.RefreshScrollContent: the Prefix builds the virtual categories + World section via CosmeticsScrollBuilder (and vanilla tabs when FixCosmeticsMenuPerformance is on), setting __state when it handled the rebuild.
// Otherwise the Postfix runs on vanilla's layout: favorites first, hidden items suppressed.

using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "RefreshScrollContent")]
internal static class CosmeticsFilterPatch
{
    internal const float SectionSpacing = 10f;
    internal const float SectionHeader  = 40f;
    internal const string WorldSectionName = "MHB_WorldSection";
    // Synthetic CosmeticType for the injected World section in virtual categories. Used ONLY as a sentinel
    // (equality compares, never an index or arithmetic operand) — int.MaxValue-1 stays clear of the game's
    // sequential CosmeticType values and any other mod's low magic numbers.
    internal const SemiFunc.CosmeticType WorldSubCategory = (SemiFunc.CosmeticType)(int.MaxValue - 1);

    [HarmonyPrefix]
    private static bool Prefix(MenuPageCosmetics __instance, ref bool __state)
    {
        // A takeover companion failed to apply — never build content for tabs/UI that don't exist.
        if (BridgePatcher.MenuTakeoverBroken) return true;
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return true;

        if (WorldCosmeticsMenuState.IsWorldCategory(__instance.selectedCategory))
        {
            CosmeticsScrollBuilder.BuildWorldScrollContent(__instance);
            __state = true;
            return false;
        }
        if (CosmeticsMenuState.IsVirtual(__instance.selectedCategory))
        {
            CosmeticsScrollBuilder.BuildVirtualScrollContent(__instance);
            __state = true;
            return false;
        }
        if (CosmeticsScrollBuilder.ShouldUseVanillaPerfPath(__instance.selectedCategory))
        {
            CosmeticsScrollBuilder.BuildVanillaScrollContent(__instance);
            __state = true;
            return false;
        }
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance, bool __state)
    {
        // Prefix already handled this rebuild — nothing left to do.
        if (__state) return;

        // selectedTab is authoritative — selectedCategory may still point to our virtual category from a previous visit even when Presets is active.
        if (__instance.selectedTab == MenuPageCosmetics.CosmeticPageTab.Presets)
        {
            CosmeticsSearchHelper.UpdateSearchFieldVisibility(false);
            CosmeticsScrollBuilder.HideEmptyState();
            return;
        }

        var selected = __instance.selectedCategory;
        if (selected == null) return;
        if (CosmeticsMenuState.IsPresetsCategory(selected))
        {
            CosmeticsSearchHelper.UpdateSearchFieldVisibility(false);
            CosmeticsScrollBuilder.HideEmptyState();
            return;
        }

        bool isSelected = CosmeticsMenuState.IsSelected(selected);
        bool isSearch   = CosmeticsMenuState.IsSearch(selected);
        bool isFav      = CosmeticsMenuState.IsFavCategory(selected);
        bool isHide     = CosmeticsMenuState.IsHideCategory(selected);
        bool isVirtual  = CosmeticsMenuState.IsVirtual(selected);   // covers all four

        // Clear SearchText before reading it so switching FROM the SEARCH tab doesn't carry the stale query into the new tab's Matches().
        CosmeticsSearchHelper.UpdateSearchFieldVisibility(isSearch);

        string search     = CosmeticsSearchHelper.FoldText(CosmeticsMenuState.SearchText?.Trim() ?? "");
        bool applySearch  = isSearch && search.Length > 0; // filter only inside the SEARCH tab

        BridgeFavoritesManager.EnsureLoaded();

        // Suppress hidden items except in the HIDE and SELECTED tabs.
        bool suppressHidden = !isHide && !isSelected && BridgeFavoritesManager.HasAnyHidden();

        // Vanilla categories with no special filtering: sort favorites first, update markers, exit.
        if (!isVirtual && !applySearch && !suppressHidden)
        {
            CosmeticsSortHelper.SortFavoritesInCategory(__instance);
            CosmeticsScrollBuilder.HideEmptyState();
            return;
        }

        if (MetaManager.instance == null) return;

        var equippedSet = new HashSet<int>(MetaManager.instance.cosmeticEquipped);
        var unlocksSet  = new HashSet<int>(MetaManager.instance.cosmeticUnlocks);

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

        float yPos        = 0f;
        int   totalVisible = 0;

        bool splitWorldFromHat = isVirtual && HhhCosmeticLoader.WorldAssetIds.Count > 0;
        MenuElementCosmeticSection? lastReflowedSection = null;

        foreach (var section in __instance.sections.ToList())
        {
            if (section.isStickyHeader) continue;

            // In virtual categories, world assets go into a dedicated section injected below.
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
                    show = false;
                }
                else if (!isVirtual && suppressHidden)
                {
                    // Non-virtual: only HIDE hidden items; never force-show.
                    if (BridgeFavoritesManager.IsHidden(btn.cosmeticAsset) && btn.gameObject.activeSelf)
                        btn.gameObject.SetActive(false);
                    if (btn.gameObject.activeSelf)
                        FavHideMarkerHelper.UpdateMarker(btn);
                    continue;
                }
                else
                {
                    show = Matches(btn.cosmeticAsset,
                        isSelected, isSearch, isFav, isHide, suppressHidden,
                        applySearch, search, equippedSet, unlocksSet);
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

            // Only reflow for virtual categories — vanilla handles non-virtual layout.
            if (!isVirtual) continue;

            if (!section.gameObject.activeSelf)
                section.gameObject.SetActive(true);

            if (section.highlightObj != null)
                section.highlightObj.gameObject.SetActive(false);

            var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
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

            lastReflowedSection = section;
            yPos -= sectionH + SectionSpacing;
        }

        if (!isVirtual || isSearch || isSelected || isFav || isHide)
            CosmeticsSortHelper.SortFavoritesInCategory(__instance,
                hiddenAtEnd: isSelected, skipRebuild: isVirtual);

        int worldCount = splitWorldFromHat
            ? CosmeticsScrollBuilder.InjectWorldSection(__instance, yPos,
                isSelected, isSearch, isFav, isHide, suppressHidden,
                applySearch, search, equippedSet, unlocksSet)
            : 0;
        totalVisible += worldCount;

        if (isVirtual)
        {
            var lastSection = worldCount > 0
                ? __instance.sections[^1]
                : lastReflowedSection;
            CosmeticsScrollBuilder.ApplyStickyPadding(__instance, lastSection);
        }

        if (isVirtual && totalVisible == 0)
        {
            string msg;
            if (isFav)       msg = "Add a favorite with Ctrl+click :)";
            else if (isHide) msg = "Hide cosmetics with Alt+click :P";
            else if (isSearch)
                msg = string.IsNullOrWhiteSpace(CosmeticsMenuState.SearchText)
                    ? "Type to search cosmetics here :)"
                    : "No cosmetics found :'(";
            else             msg = "Equip a cosmetic to see it here :3";
            CosmeticsScrollBuilder.ShowEmptyState(msg);
        }
        else
            CosmeticsScrollBuilder.HideEmptyState();

        if (isVirtual)
            CosmeticsScrollBuilder.RebuildScroll(__instance);
    }

    // ── Filter predicate ──────────────────────────────────────────────────────

    internal static bool Matches(
        CosmeticAsset asset,
        bool isSelected, bool isSearch, bool isFav, bool isHide, bool suppressHidden,
        bool applySearch, string search,
        HashSet<int> equippedSet, HashSet<int> unlocksSet)
    {
        // SEARCH with empty field shows nothing — user hasn't typed yet.
        if (isSearch && !applySearch) return false;

        // assetIndexMap covers only vanilla-registered assets; bridge-only assets return -1.
        int idx = CosmeticsMenuState.GetAssetIndex(asset);

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
            if (!MatchesSearch(asset, search)) return false;
        }

        return true;
    }

    // Caches the folded (lowercase, accent-stripped) display name so FoldText runs at most once per asset.
    private static readonly Dictionary<CosmeticAsset, string> _lowerNameCache = new();

    private static string GetLowerName(CosmeticAsset asset)
    {
        if (_lowerNameCache.TryGetValue(asset, out var cached)) return cached;
        var lower = CosmeticsSearchHelper.FoldText(asset.assetName ?? asset.name ?? "");
        _lowerNameCache[asset] = lower;
        return lower;
    }

    // The search matches the English asset name and, when a translation overlay (XUnity AutoTranslator) renames cosmetics, the translated name too.
    private static bool MatchesSearch(CosmeticAsset asset, string search)
    {
        if (GetLowerName(asset).Contains(search)) return true;
        var translated = GetLowerTranslatedName(asset);
        return translated != null && translated.Contains(search);
    }

    // ── XUnity AutoTranslator soft-dependency (resolved once via reflection) ──
    private static bool _xuResolved;
    private static object? _xuTranslator;
    private static System.Reflection.MethodInfo? _xuTryTranslate;
    private static readonly Dictionary<CosmeticAsset, string> _lowerTranslatedCache = new();

    private static string? GetLowerTranslatedName(CosmeticAsset asset)
    {
        if (_lowerTranslatedCache.TryGetValue(asset, out var cached)) return cached;

        if (!_xuResolved)
        {
            _xuResolved = true;
            try
            {
                _xuTranslator = System.Type.GetType(
                        "XUnity.AutoTranslator.Plugin.Core.AutoTranslator, XUnity.AutoTranslator.Plugin.Core")
                    ?.GetProperty("Default")?.GetValue(null);
                var iface = System.Type.GetType(
                    "XUnity.AutoTranslator.Plugin.Core.ITranslator, XUnity.AutoTranslator.Plugin.Core");
                _xuTryTranslate = (iface ?? _xuTranslator?.GetType())?.GetMethod(
                    "TryTranslate", new[] { typeof(string), typeof(string).MakeByRefType() });
            }
            catch { _xuTranslator = null; _xuTryTranslate = null; }
        }
        if (_xuTranslator == null || _xuTryTranslate == null) return null;

        string name = asset.assetName ?? asset.name ?? "";
        if (name.Length == 0) return null;
        try
        {
            var args = new object?[] { name, null };
            if (_xuTryTranslate.Invoke(_xuTranslator, args) is true
                && args[1] is string translated && translated.Length > 0 && translated != name)
            {
                // Only successful translations are cached — translation files can load late.
                var lower = CosmeticsSearchHelper.FoldText(translated);
                _lowerTranslatedCache[asset] = lower;
                return lower;
            }
        }
        catch { _xuTryTranslate = null; }   // API shape mismatch — stop trying
        return null;
    }

    internal static bool IsUnlocked(MenuElementCosmeticButton btn)
    {
        if (MetaManager.instance == null) return true;
        int idx = CosmeticsMenuState.GetAssetIndex(btn.cosmeticAsset);
        if (idx < 0) return true;
        return MetaManager.instance.cosmeticUnlocks.Contains(idx);
    }
}
