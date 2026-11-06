// Controller and keyboard navigation for the bridge's MenuLib popups: explicit row links (Automatic mode would escape to the page underneath), first row focused on open, previous selection restored on close, scroll fitted to the rows shown, and Back and ESC answered by the popup on top.

using System.Collections.Generic;
using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal sealed class PopupNav : MonoBehaviour
{
    private const float RowEpsilon = 2f;      // same tolerance as MenuPage.WireVerticalNavigation
    private const float ScrollMargin = 10f;   // same as MenuScrollBox.RecalculateScrollHeight

    private const string ScrollFeature = "popup scrolling to the last row";
    private static readonly LazyFieldRef<MenuScrollBox, float> _scrollHeight = new("scrollHeight", ScrollFeature);
    private static readonly LazyFieldRef<MenuScrollBox, float> _minChildPos = new("minChildPos", ScrollFeature);
    private static readonly LazyFieldRef<MenuScrollBox, float> _maxChildPos = new("maxChildPos", ScrollFeature);
    private static readonly LazyFieldRef<MenuScrollBox, float> _minSelectablePos = new("minSelectablePos", ScrollFeature);
    private static readonly LazyFieldRef<MenuScrollBox, float> _maxSelectablePos = new("maxSelectablePos", ScrollFeature);

    private static readonly Vector3[] Corners = new Vector3[4];

    // Open popups, the one on top last.
    private static readonly List<PopupNav> _open = [];
    private static bool _readingBack;

    private REPOPopupPage _popup = null!;
    private REPOScrollViewElement[] _scrollRows = [];
    private string _previousSelection = "";
    private bool _dirty;
    private bool _focused;
    private bool _restored;

    internal static bool Navigating => SemiFunc.InputMode() == InputModes.Gamepad || MenuManager.instance.isKeyboardNavigating;

    // Back reaches every open page in the same frame, so the popup and the pages under it would all close. While a popup is open, only the one on top reads it.
    internal static bool HoldsBack => _open.Count > 0 && !_readingBack;

    internal static void Attach(REPOPopupPage popup)
    {
        var nav = popup.GetComponent<PopupNav>();
        if (nav == null) nav = popup.gameObject.AddComponent<PopupNav>();

        nav._popup = popup;
        nav._previousSelection = MenuManager.instance != null ? MenuManager.instance.lastHoverMenuID : "";
        nav._dirty = true;
        nav._focused = false;
        nav._restored = false;

        _open.Remove(nav);
        _open.Add(nav);
    }

    // Rows were shown, hidden or moved.
    internal void MarkDirty() => _dirty = true;

    internal void ShowAllRows()
    {
        foreach (var row in _scrollRows)
            if (row != null && row.visibility && !row.gameObject.activeSelf)
                row.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_open.Count == 0 || _open[_open.Count - 1] != this) return;
        if (_popup.menuPage.currentPageState == MenuPage.PageState.Closing) return;

        _readingBack = true;
        bool pressed = SemiFunc.InputDown(InputKey.Back);
        _readingBack = false;
        if (!pressed) return;

        var marker = GetComponentInChildren<PopupBackButton>(true);
        var button = marker != null ? marker.GetComponent<REPOButton>() : null;
        if (button != null) button.onClick?.Invoke();
        else _popup.ClosePage(false);
    }

    private void LateUpdate()
    {
        var state = _popup.menuPage.currentPageState;

        if (state == MenuPage.PageState.Closing)
        {
            if (!_restored && Navigating) MenuManager.instance.lastHoverMenuID = _previousSelection;
            _restored = true;
            _open.Remove(this);
            return;
        }

        // A popup over the color page goes Inactive right after opening, because the color page stays the current one.
        if (_dirty && state is MenuPage.PageState.Active or MenuPage.PageState.Inactive)
        {
            _dirty = false;
            Wire();
        }
    }

    private void OnDisable() => _open.Remove(this);

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

        _scrollRows = GetComponentsInChildren<REPOScrollViewElement>(true);
        FitScroll(rows);

        if (!_focused && rows.Count > 0 && Navigating)
            MenuManager.instance.lastHoverMenuID = rows[0][0].menuID;
        _focused = true;
    }

    // MenuScrollBox measures once, hidden rows included, and turns its bar off when the overflow is shorter than the bar. REPOScrollView measures the middle of a button row. Neither finds the end of what's shown.
    private void FitScroll(List<List<MenuSelectableElement>> rows)
    {
        if (rows.Count == 0) return;

        var box = _popup.menuScrollBox;
        var scroller = box.scroller;

        float lowestButton = float.MaxValue, highestButton = float.MinValue;
        foreach (var row in rows)
        {
            foreach (var element in row)
            {
                float bottom = BottomOf(element.rectTransform, scroller);
                lowestButton = Mathf.Min(lowestButton, bottom);
                highestButton = Mathf.Max(highestButton, bottom);
            }
        }

        float lowest = lowestButton;
        foreach (var row in _scrollRows)
            if (row != null && row.visibility)
                lowest = Mathf.Min(lowest, BottomOf(row.rectTransform, scroller));

        // How far the lowest row hangs below the mask with the scroller at the top.
        _popup.maskRectTransform.GetWorldCorners(Corners);
        float overflow = scroller.InverseTransformPoint(Corners[0]).y + scroller.localPosition.y - lowest;

        bool scrolls = overflow > 1f;
        if (box.scrollBar.activeSelf != scrolls) box.scrollBar.SetActive(scrolls);
        if (!scrolls)
        {
            var position = scroller.localPosition;
            scroller.localPosition = new Vector3(position.x, 0f, position.z);
            return;
        }

        box.scrollerStartPosition = overflow + ScrollMargin;
        _scrollHeight.TrySet(box, overflow + ScrollMargin);

        // These decide which button counts as the end of the list, and so scrolls all the way.
        _minChildPos.TrySet(box, lowestButton);
        _maxChildPos.TrySet(box, highestButton);
        _minSelectablePos.TrySet(box, lowestButton);
        _maxSelectablePos.TrySet(box, highestButton);
    }

    // Same measure as MenuScrollBox.GetChildScrollerPos.
    private static float BottomOf(RectTransform rect, Transform scroller)
        => scroller.InverseTransformPoint(rect.position).y - rect.rect.height * rect.pivot.y;

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
