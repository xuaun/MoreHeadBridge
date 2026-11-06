// Gamepad/keyboard Navigation for the color page: the palette grid plus the injected M / C / A buttons and the slot-selector row.

using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class ColorPageNav
{
    // Color grid geometry (see MenuPageColor.Start).
    private const float CellWidth = 38f;
    private const float CellHeight = 30f;
    private const float FirstRowY = 224f;

    private static readonly System.Reflection.MethodInfo? SetupGridNavigation =
        AccessTools.Method(typeof(MenuPageColor), "SetupGridNavigation");

    // Re-runs vanilla's grid wiring over every button in the holder.
    internal static void RewireGrid(MenuPageColor page)
    {
        if (SetupGridNavigation == null) return;
        var grid = GridOf(page);
        if (grid.Count == 0) return;

        SetupGridNavigation.Invoke(page, new object[]
        {
            grid.Select(c => c.Element).ToList(),
            grid.Select(c => c.Cell).ToList(),
        });
    }

    // Places the slot row between the grid and Confirm.
    internal static void LinkSlotRow(MenuPageColor page, List<MenuSelectableElement> row)
    {
        var grid = GridOf(page);
        if (row.Count == 0 || grid.Count == 0) return;

        var confirm = FindConfirm(page);
        var cells = new HashSet<Vector2Int>(grid.Select(c => c.Cell));
        int lastRow = grid.Max(c => c.Cell.y);
        var bottom = grid.Where(c => c.Cell.y == lastRow).Select(c => c.Element).ToList();

        for (int i = 0; i < row.Count; i++)
        {
            row[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = Closest(bottom, row[i]),
                selectOnDown = confirm,
                selectOnLeft = i > 0 ? row[i - 1] : null,
                selectOnRight = i < row.Count - 1 ? row[i + 1] : null,
            };
        }

        foreach (var (element, cell) in grid)
        {
            if (cells.Contains(new Vector2Int(cell.x, cell.y + 1))) continue;
            var nav = element.navigation;
            nav.selectOnDown = Closest(row, element);
            element.navigation = nav;
        }

        if (confirm != null)
        {
            var nav = confirm.navigation;
            nav.selectOnUp = row[0];
            confirm.navigation = nav;
        }
    }

    private static List<(MenuSelectableElement Element, Vector2Int Cell)> GridOf(MenuPageColor page)
    {
        var grid = new List<(MenuSelectableElement, Vector2Int)>();
        if (page.colorButtonHolder == null) return grid;

        foreach (Transform child in page.colorButtonHolder)
        {
            if (!child.gameObject.activeSelf || child.gameObject == page.colorButtonPrefab) continue;
            var button = child.GetComponent<MenuButton>();
            if (button == null || button.menuSelectableElement == null) continue;

            var pos = ((RectTransform)child).anchoredPosition;
            grid.Add((button.menuSelectableElement, new Vector2Int(
                Mathf.RoundToInt(pos.x / CellWidth),
                Mathf.RoundToInt((FirstRowY - pos.y) / CellHeight))));
        }
        return grid;
    }

    // Same lookup as vanilla's SetupGridNavigation: first button outside the holder.
    private static MenuSelectableElement? FindConfirm(MenuPageColor page)
    {
        foreach (Transform child in page.transform)
        {
            if (child == page.colorButtonHolder.transform || child.gameObject == page.colorButtonPrefab) continue;
            var button = child.GetComponent<MenuButton>();
            if (button != null) return button.menuSelectableElement;
        }
        return null;
    }

    private static MenuSelectableElement Closest(List<MenuSelectableElement> candidates, MenuSelectableElement to)
        => candidates.OrderBy(c => Mathf.Abs(c.rectTransform.position.x - to.rectTransform.position.x)).First();
}

// Runs last, after OriginalColorButtonPatch injected its buttons; also focuses M / C when that's the active choice.
[HarmonyPatch(typeof(MenuPageColor), "Start")]
internal static class ColorPageNavPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MenuPageColor __instance)
    {
        ColorPageNav.RewireGrid(__instance);

        if (__instance.colorButtonHolder == null || MenuManager.instance == null) return;
        foreach (Transform child in __instance.colorButtonHolder)
        {
            bool selected = (child.TryGetComponent<BridgeOriginalColorButton>(out var original) && original.initiallySelected)
                         || (child.TryGetComponent<BridgeCustomColorButton>(out var custom) && custom.initiallySelected);
            if (!selected) continue;

            var button = child.GetComponent<MenuButton>();
            if (button != null && button.menuSelectableElement != null)
                MenuManager.instance.lastHoverMenuID = button.menuSelectableElement.menuID;
            break;
        }
    }
}
