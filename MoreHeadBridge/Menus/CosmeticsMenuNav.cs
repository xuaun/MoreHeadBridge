// Gamepad/keyboard Navigation for the cosmetics page: mirrors vanilla's wiring so it follows the items, tabs and buttons the bridge builds, reorders or injects.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class CosmeticsMenuNav
{
    private const string Feature = "cosmetics-menu navigation";
    private static readonly LazyFieldRef<MenuPageCosmetics, MenuSelectableElement> _confirm = new("confirmButtonSelectable", Feature);
    private static readonly LazyFieldRef<MenuPageCosmetics, MenuSelectableElement> _reset = new("resetButtonSelectable", Feature);

    // One frame later: buttons of the previous build are still alive until Destroy runs at the end of this frame.
    internal static IEnumerator WireSectionsNextFrame(MenuPageCosmetics page)
    {
        yield return null;
        if (page != null) WireSections(page);
    }

    // Item grid of each section, section-to-section links, sub-category row, and the toolbar/tab entry points.
    internal static void WireSections(MenuPageCosmetics page)
    {
        _confirm.TryGet(page, out var confirm);
        _reset.TryGet(page, out var reset);
        Selectable? leftEdge = confirm != null ? confirm : reset;

        var subRow = SubCategoryRow(page);
        var firstSub = subRow.FirstOrDefault();
        var selectedTab = SelectedTab(page);
        var search = SearchNav();

        // Virtual tabs have no sub-category row: the top of the list goes back to the tab.
        Selectable? aboveList = firstSub != null ? firstSub : selectedTab;

        MenuSelectableElement? firstItem = null;
        List<MenuSelectableElement>? prevBottom = null;

        foreach (var section in page.sections)
        {
            if (section == null || section.isStickyHeader || section.cosmeticListTransform == null) continue;

            // Slots of rows not shown yet are empty, and an empty target just doesn't move the selection until the row appears.
            var lazy = section.GetComponent<LazyCosmeticGrid>();

            // activeSelf, not activeInHierarchy: the scroll box deactivates off-screen sections, and hidden cosmetics are inactive buttons.
            var items = lazy != null ? null : section.cosmeticListTransform.GetComponentsInChildren<MenuElementCosmeticButton>(true)
                .Where(b => b.gameObject.activeSelf && b.menuButton != null && b.menuButton.menuSelectableElement != null)
                .ToList();
            int count = lazy != null ? lazy.Slots : items!.Count;
            if (count == 0) continue;

            var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
            int cols = Mathf.Max(1, grid != null ? grid.constraintCount : 1);
            int rows = (count + cols - 1) / cols;

            MenuElementCosmeticButton? ItemAt(int i)
            {
                if (lazy != null) return lazy.ButtonAt(i);
                return i < items!.Count ? items[i] : null;
            }

            MenuSelectableElement? At(int x, int y)
            {
                if (x < 0 || x >= cols || y < 0) return null;
                var item = ItemAt(x + y * cols);
                return item != null && item.menuButton != null ? item.menuButton.menuSelectableElement : null;
            }

            var color = section.colorButton != null ? section.colorButton.menuSelectableElement : null;
            Selectable? previous = prevBottom == null ? aboveList : prevBottom.Count > 0 ? prevBottom[0] : null;
            Selectable? sectionTop = color != null ? color : previous;

            for (int i = 0; i < count; i++)
            {
                int x = i % cols, y = i / cols;
                var main = At(x, y);
                if (main == null) continue;

                var item = ItemAt(i)!;
                var tint = item.tintableButton != null ? item.tintableButton.menuSelectableElement : null;
                Selectable? up = y == 0 ? sectionTop : At(x, y - 1);

                main.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = tint != null ? tint : up,
                    selectOnDown = At(x, y + 1) ?? (y + 1 < rows ? At((count - 1) % cols, rows - 1) : null),
                    selectOnLeft = x == 0 ? leftEdge : At(x - 1, y),
                    selectOnRight = At(x + 1, y),
                };

                if (tint != null)
                {
                    tint.navigation = new Navigation
                    {
                        mode = Navigation.Mode.Explicit,
                        selectOnUp = up,
                        selectOnDown = main,
                        selectOnLeft = main,
                        selectOnRight = At(x + 1, y),
                    };
                }
            }

            if (color != null)
            {
                var nav = color.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnDown = At(0, 0);
                nav.selectOnUp = previous;
                nav.selectOnLeft = leftEdge;
                color.navigation = nav;
            }

            Selectable entry = color != null ? color : At(0, 0)!;
            if (prevBottom != null)
                foreach (var s in prevBottom) SetDown(s, entry);

            firstItem ??= At(0, 0);
            prevBottom = Enumerable.Range(0, cols)
                .Select(c => At(c, rows - 1))
                .Where(s => s != null)
                .Cast<MenuSelectableElement>()
                .ToList();
        }

        // Bottom row of the last section stays put when pressing down.
        if (prevBottom != null)
            foreach (var s in prevBottom) SetDown(s, s);

        bool searchOnTop = search != null && Plugin.SearchFieldPosition.Value == SearchBarPosition.Top;
        var randomize = page.randomizePopup != null && page.randomizePopup.toggleButton != null
            ? page.randomizePopup.toggleButton.menuSelectableElement
            : null;

        if (confirm != null)
        {
            var nav = confirm.navigation;
            if (firstItem != null) nav.selectOnRight = firstItem;
            nav.selectOnUp = search != null && !searchOnTop ? search : randomize;
            confirm.navigation = nav;
        }

        LinkRow(subRow, null);
        if (selectedTab != null)
        {
            foreach (var s in subRow)
            {
                var nav = s.navigation;
                nav.selectOnUp = selectedTab;
                s.navigation = nav;
            }
        }

        Selectable? tabsDown = firstSub != null ? firstSub : firstItem != null ? firstItem : search;
        foreach (var tab in CategoryTabs(page))
        {
            if (tabsDown != null) SetDown(tab, tabsDown);
            var nav = tab.navigation;
            nav.selectOnUp = searchOnTop ? search : null;
            tab.navigation = nav;
        }

        // On top the field sits above the tabs; at the bottom, between the toolbar and Confirm.
        if (search != null)
        {
            search.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = searchOnTop ? null : randomize,
                selectOnDown = searchOnTop ? selectedTab : confirm,
                selectOnRight = searchOnTop ? null : firstItem,
            };
        }
    }

    // The SEARCH field's navigation target, while that tab is open.
    private static MenuSelectableElement? SearchNav()
    {
        var field = CosmeticsMenuState.SearchField;
        if (!CosmeticsMenuState.SearchMode || field == null || !field.gameObject.activeSelf) return null;
        var nav = field.GetComponentInChildren<SearchFieldGamepad>(true);
        return nav != null ? nav.Element : null;
    }

    // Toolbar row (vanilla popups + Tools button) and category tabs, in their final left-to-right order.
    internal static void WireToolbarAndTabs(MenuPageCosmetics page)
    {
        _confirm.TryGet(page, out var confirm);

        var toolsPopup = CosmeticsMenuStartPatch._toolsPopupRef;
        if (toolsPopup != null && confirm != null)
            toolsPopup.selectOnDownWhenClosed = confirm;

        var toolbar = new[] { page.colorPopup, page.randomizePopup, page.resetPopup, toolsPopup }
            .Where(p => p != null && p.toggleButton != null && p.toggleButton.menuSelectableElement != null)
            .Select(p => p!.toggleButton.menuSelectableElement)
            .OrderBy(s => s.rectTransform.position.x)
            .ToList();

        var tabs = CategoryTabs(page);

        LinkRow(toolbar, null);
        LinkRow(tabs, toolbar.LastOrDefault());

        if (toolbar.Count > 0 && tabs.Count > 0)
        {
            var nav = toolbar[^1].navigation;
            nav.selectOnRight = tabs[0];
            toolbar[^1].navigation = nav;
        }
    }

    private static List<MenuSelectableElement> SubCategoryRow(MenuPageCosmetics page)
        => page.subCategoriesTransform.GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .Where(b => b.buttonType == MenuElementButtonCosmeticCategory.ButtonType.SubCategory && b.gameObject.activeSelf)
            .Select(SelectableOf)
            .Where(s => s != null)
            .Cast<MenuSelectableElement>()
            .ToList();

    private static List<MenuSelectableElement> CategoryTabs(MenuPageCosmetics page)
        => page.categoriesTransform.GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .Select(SelectableOf)
            .Where(s => s != null)
            .Cast<MenuSelectableElement>()
            .ToList();

    private static MenuSelectableElement? SelectedTab(MenuPageCosmetics page)
    {
        foreach (var tab in page.categoriesTransform.GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true))
            if (tab.category == page.selectedCategory) return SelectableOf(tab);
        return null;
    }

    private static MenuSelectableElement? SelectableOf(MenuElementButtonCosmeticCategory tab)
    {
        var button = tab.GetComponent<MenuButton>();
        return button != null ? button.menuSelectableElement : null;
    }

    private static void LinkRow(List<MenuSelectableElement> row, Selectable? leftEdge)
    {
        for (int i = 0; i < row.Count; i++)
        {
            var nav = row[i].navigation;
            nav.selectOnLeft = i > 0 ? row[i - 1] : (leftEdge != null ? leftEdge : row[i]);
            nav.selectOnRight = i < row.Count - 1 ? row[i + 1] : row[i];
            row[i].navigation = nav;
        }
    }

    private static void SetDown(Selectable selectable, Selectable target)
    {
        var nav = selectable.navigation;
        nav.selectOnDown = target;
        selectable.navigation = nav;
    }
}

// Runs last on both paths (vanilla build or our Build*), after favorites/hidden sorting changed the visual order.
[HarmonyPatch(typeof(MenuPageCosmetics), nameof(MenuPageCosmetics.RefreshScrollContent))]
internal static class CosmeticsNavRefreshPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;
        if (__instance.isActiveAndEnabled)
            __instance.StartCoroutine(CosmeticsMenuNav.WireSectionsNextFrame(__instance));
    }
}

// Runs after every Start injection (virtual tabs, WORLD tab, Tools button).
[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class CosmeticsNavStartPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (__instance.selectedTab == MenuPageCosmetics.CosmeticPageTab.Cosmetics)
            CosmeticsMenuNav.WireToolbarAndTabs(__instance);
    }
}
