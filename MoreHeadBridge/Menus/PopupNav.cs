// Controller and keyboard navigation for the bridge's MenuLib popups: explicit row links (Automatic mode would escape to the page underneath), first row focused on open, previous selection restored on close.

using System.Collections.Generic;
using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal sealed class PopupNav : MonoBehaviour
{
    private const float RowEpsilon = 2f;   // same tolerance as MenuPage.WireVerticalNavigation

    private REPOPopupPage _popup = null!;
    private string _previousSelection = "";
    private bool _dirty;
    private bool _focused;
    private bool _restored;

    private static bool Navigating => SemiFunc.InputMode() == InputModes.Gamepad || MenuManager.instance.isKeyboardNavigating;

    internal static void Attach(REPOPopupPage popup)
    {
        var nav = popup.GetComponent<PopupNav>();
        if (nav == null) nav = popup.gameObject.AddComponent<PopupNav>();

        nav._popup = popup;
        nav._previousSelection = MenuManager.instance != null ? MenuManager.instance.lastHoverMenuID : "";
        nav._dirty = true;
        nav._focused = false;
        nav._restored = false;
    }

    // Rows were shown, hidden or moved.
    internal void MarkDirty() => _dirty = true;

    private void LateUpdate()
    {
        var state = _popup.menuPage.currentPageState;

        if (state == MenuPage.PageState.Closing)
        {
            if (!_restored && Navigating) MenuManager.instance.lastHoverMenuID = _previousSelection;
            _restored = true;
            return;
        }

        if (_dirty && state == MenuPage.PageState.Active)
        {
            _dirty = false;
            Wire();
        }
    }

    private void Wire()
    {
        // Slider bars and other non-button selectables only react to the mouse: taken out of navigation.
        foreach (var element in _popup.menuPage.selectableElements)
        {
            if (element == null) continue;
            var nav = element.navigation;
            nav.mode = Navigation.Mode.None;
            element.navigation = nav;
        }

        var rows = BuildRows();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int i = 0; i < row.Count; i++)
            {
                row[i].navigation = new Navigation
                {
                    mode          = Navigation.Mode.Explicit,
                    selectOnUp    = r > 0 ? ClosestByX(rows[r - 1], row[i]) : null,
                    selectOnDown  = r < rows.Count - 1 ? ClosestByX(rows[r + 1], row[i]) : null,
                    selectOnLeft  = i > 0 ? row[i - 1] : null,
                    selectOnRight = i < row.Count - 1 ? row[i + 1] : null,
                };
            }
        }

        if (!_focused && rows.Count > 0 && Navigating)
            MenuManager.instance.lastHoverMenuID = rows[0][0].menuID;
        _focused = true;
    }

    // Visible buttons grouped by height, top to bottom and left to right.
    private List<List<MenuSelectableElement>> BuildRows()
    {
        var rows = new List<List<MenuSelectableElement>>();
        foreach (var button in GetComponentsInChildren<MenuButton>(true))
        {
            var element = button.menuSelectableElement;
            if (element == null || !element.IsInteractable() || !IsShown(element)) continue;

            float y = element.rectTransform.position.y;
            var row = rows.Find(r => Mathf.Abs(r[0].rectTransform.position.y - y) <= RowEpsilon);
            if (row == null) rows.Add(row = new List<MenuSelectableElement>());
            if (!row.Contains(element)) row.Add(element);
        }

        rows.Sort((a, b) => b[0].rectTransform.position.y.CompareTo(a[0].rectTransform.position.y));
        foreach (var row in rows)
            row.Sort((a, b) => a.rectTransform.position.x.CompareTo(b.rectTransform.position.x));
        return rows;
    }

    private static bool IsShown(MenuSelectableElement element)
    {
        var row = element.GetComponentInParent<REPOScrollViewElement>(true);
        if (row == null) return element.gameObject.activeInHierarchy;
        if (!row.visibility) return false;

        // REPOScrollView deactivates rows outside the mask, so only what's below the row itself counts.
        for (var t = element.transform; t != row.transform; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }

    private static MenuSelectableElement ClosestByX(List<MenuSelectableElement> row, MenuSelectableElement from)
    {
        var best = row[0];
        float bestDistance = float.MaxValue;
        foreach (var candidate in row)
        {
            float distance = Mathf.Abs(candidate.rectTransform.position.x - from.rectTransform.position.x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }
}
