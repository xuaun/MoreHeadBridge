using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// A section's buttons with FixCosmeticsMenuPerformance on: each one is created when its row comes near the viewport, and its row is hidden when it leaves.
// Slot 0 is the section's unequip button. GridLayoutGroup stays off, because it skips inactive children and would close the gaps.
internal sealed class LazyCosmeticGrid : MonoBehaviour
{
    private const int MarginRows = 2;

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly LazyFieldRef<MenuScrollBox, float> _minSelectablePos = new("minSelectablePos", "controller scrolling to the end of the cosmetics list");
    private static readonly LazyFieldRef<MenuScrollBox, float> _scrollHandleTarget = new("scrollHandleTargetPosition", "keeping the cosmetics list in place when a mod hides buttons");
    private static readonly LazyFieldRef<MenuScrollBox, float> _cullScrollerY = new("lastScrollerY", "keeping the cosmetics list in place when a mod hides buttons");

    // Cosmetics whose button another mod hid: later builds leave them out, so the gap doesn't happen again.
    private static readonly HashSet<CosmeticAsset> _hiddenByMods = [];

    private static MenuPageCosmetics? _activePage;
    private static int _builtFrame;
    private static float _lastScrollerY;
    private static bool _dirty, _settling, _reflow;

    private readonly List<MenuElementCosmeticButton?> _buttons = [];
    private MenuPageCosmetics _page = null!;
    private MenuElementCosmeticSection _section = null!;
    private RectTransform _list = null!;
    private GridLayoutGroup _grid = null!;
    private List<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)> _items = null!;
    private int _firstRow, _lastRow = -1;
    private int _bottomPadding;   // ApplyStickyPadding replaces it on the tab's last section

    internal IReadOnlyList<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)> Items => _items;
    internal int Count => _items.Count;
    internal int Slots => _buttons.Count;

    private int Columns => Mathf.Max(1, _grid.constraintCount);
    private int Rows => (Slots + Columns - 1) / Columns;

    internal static LazyCosmeticGrid Attach(
        MenuPageCosmetics page,
        MenuElementCosmeticSection section,
        List<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)> items)
    {
        items.RemoveAll(item => _hiddenByMods.Contains(item.rep));

        var lazy = section.gameObject.AddComponent<LazyCosmeticGrid>();
        lazy._page = page;
        lazy._section = section;
        lazy._list = (RectTransform)section.cosmeticListTransform;
        lazy._grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
        lazy._grid.enabled = false;
        lazy._bottomPadding = lazy._grid.padding.bottom;
        lazy._items = items;

        lazy._buttons.Add(section.unequipButton);
        foreach (var _ in items) lazy._buttons.Add(null);
        lazy.Place((RectTransform)section.unequipButton.transform, 0);

        _activePage = page;
        _builtFrame = Time.frameCount;
        _dirty = true;
        _reflow = false;
        return lazy;
    }

    // Only while the section is being built: no button exists yet.
    internal void SetOrder(List<(CosmeticAsset rep, List<CosmeticGrouping.Member>? members)> items) => _items = items;

    // The button at a slot if navigation can reach it: slot 0 always, the others while their row is shown.
    internal MenuElementCosmeticButton? ButtonAt(int slot)
    {
        if (slot < 0 || slot >= Slots) return null;
        if (slot == 0) return _buttons[0];

        int row = slot / Columns;
        return row >= _firstRow && row <= _lastRow ? _buttons[slot] : null;
    }

    internal static void AfterScrollBoxLateUpdate(MenuScrollBox box)
    {
        var page = _activePage;
        if (page == null || page.menuScrollBox != box) return;

        ShowSelectedRow();

        // RecalculateScrollHeightAfterFrame only puts the scroller in place on the frame after the build.
        if (Time.frameCount == _builtFrame) return;

        if (_reflow)
        {
            _reflow = false;
            Reflow(page, box);
        }

        float y = box.scroller.localPosition.y;
        bool moved = !Mathf.Approximately(y, _lastScrollerY);
        if (!moved && !_dirty && !_settling) return;

        // New buttons run Start on the next frame, and a mod can hide them there.
        _settling = moved || _dirty;
        _lastScrollerY = y;
        _dirty = false;

        var viewport = page.stickyHeader != null ? page.stickyHeader.viewport : null;
        if (viewport == null) return;

        bool changed = false;
        foreach (var section in page.sections)
        {
            if (section == null || !section.gameObject.activeInHierarchy) continue;

            var lazy = section.GetComponent<LazyCosmeticGrid>();
            if (lazy != null) changed |= lazy.Refresh(viewport);
        }
        if (changed)
        {
            _settling = true;
            CosmeticsMenuNav.WireSections(page);
        }
    }

    // After a drop that changed a section's height: closes the gaps, as the build would have. A virtual tab only lists sections with something in them.
    private static void Reflow(MenuPageCosmetics page, MenuScrollBox box)
    {
        if (page.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;

        bool isVirtual = CosmeticsMenuState.IsVirtual(page.selectedCategory);
        bool removed = false;
        float y = 0f;
        MenuElementCosmeticSection? last = null;

        for (int i = 0; i < page.sections.Count; i++)
        {
            var section = page.sections[i];
            if (section == null) continue;

            var lazy = section.GetComponent<LazyCosmeticGrid>();
            if (lazy == null) continue;

            if (isVirtual && lazy.Count == 0)
            {
                page.sections.RemoveAt(i--);
                foreach (var button in page.subCategoriesTransform.GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true))
                    if (button.subCategory == section.subCategory) Destroy(button.gameObject);
                Destroy(section.gameObject);
                removed = true;
                continue;
            }

            lazy._grid.padding.bottom = lazy._bottomPadding;
            CosmeticsScrollBuilder.ApplySectionSize(section, lazy.Count);

            var rect = (RectTransform)section.transform;
            rect.localPosition = new Vector3(rect.localPosition.x, y, rect.localPosition.z);
            y -= rect.sizeDelta.y + CosmeticsFilterPatch.SectionSpacing;
            last = section;
        }

        // The tab's empty message comes from the build.
        if (page.sections.Count == 0)
        {
            page.RefreshScrollContent();
            return;
        }

        CosmeticsScrollBuilder.ApplyStickyPadding(page, last);
        RecalculateKeepingPosition(box);
        _dirty = true;

        if (removed)
        {
            if (page.stickyHeader != null) page.stickyHeader.SetSections();
            page.StartCoroutine(CosmeticsMenuNav.WireSectionsNextFrame(page));
        }
    }

    // RecalculateScrollHeight puts the list back at the top. This keeps it where it was, as far as the new height allows.
    private static void RecalculateKeepingPosition(MenuScrollBox box)
    {
        float y = box.scrollBar.activeSelf
            ? Mathf.Lerp(box.scrollerStartPosition, box.scrollerEndPosition, box.scrollAmount)
            : box.scrollerEndPosition;

        box.RecalculateScrollHeight();

        // Sections that moved into view are still off: MenuScrollBox only checks them when the scroller moves.
        _cullScrollerY.TrySet(box, float.NaN);

        if (!box.scrollBar.activeSelf)
        {
            var scrollerPos = box.scroller.localPosition;
            box.scroller.localPosition = new Vector3(scrollerPos.x, box.scrollerEndPosition, scrollerPos.z);
            return;
        }

        float amount = Mathf.InverseLerp(box.scrollerStartPosition, box.scrollerEndPosition, y);
        float halfHandle = box.scrollHandle.sizeDelta.y / 2f;
        float handleY = Mathf.Lerp(halfHandle, box.scrollBarBackground.rect.height - halfHandle, amount);

        var handlePos = box.scrollHandle.localPosition;
        box.scrollHandle.localPosition = new Vector3(handlePos.x, handleY, handlePos.z);
        _scrollHandleTarget.TrySet(box, handleY);
        box.scrollAmount = amount;

        // A navigation's scroll was on its way to where the selection used to be.
        if (box.isAnimating)
        {
            box.isAnimating = false;
            var selected = MenuManager.instance != null ? MenuManager.instance.currentMenuIDTransform : null;
            if (selected != null && selected.IsChildOf(box.scroller)) box.ScrollToChild(selected);
        }
    }

    // RecalculateScrollHeight only sees the rows that exist, and would take the last one created for the end of the list.
    internal static void FixScrollEdge(MenuScrollBox box)
    {
        var page = _activePage;
        if (page == null || page.menuScrollBox != box) return;
        if (!_minSelectablePos.TryGet(box, out float lowest)) return;

        foreach (var section in page.sections)
        {
            if (section == null) continue;

            var lazy = section.GetComponent<LazyCosmeticGrid>();
            if (lazy != null) lowest = Mathf.Min(lowest, lazy.BottomSelectablePos(box.scroller));
        }
        _minSelectablePos.TrySet(box, lowest);
    }

    // The game can restore a remembered selection on a hidden row, and a hidden button can't stay selected.
    private static void ShowSelectedRow()
    {
        var selected = MenuManager.instance != null ? MenuManager.instance.currentMenuIDTransform : null;
        if (selected == null || selected.gameObject.activeInHierarchy) return;

        var button = selected.GetComponentInParent<MenuElementCosmeticButton>(true);
        if (button == null || button.gameObject.activeSelf) return;

        var lazy = button.GetComponentInParent<LazyCosmeticGrid>(true);
        int slot = lazy != null ? lazy._buttons.IndexOf(button) : -1;
        if (slot > 0) lazy!.ShowRow(slot / lazy.Columns);
    }

    // Returns whether anything navigation depends on changed.
    private bool Refresh(RectTransform viewport)
    {
        bool dropped = DropButtonsHiddenByMods();

        viewport.GetWorldCorners(Corners);
        int first = Mathf.Clamp(RowAt(Corners[1]) - MarginRows, 0, Rows - 1);
        int last = Mathf.Clamp(RowAt(Corners[0]) + MarginRows, 0, Rows - 1);
        if (first == _firstRow && last == _lastRow) return dropped;

        for (int row = _firstRow; row <= _lastRow; row++)
            if (row < first || row > last) HideRow(row);
        for (int row = first; row <= last; row++)
            if (row < _firstRow || row > _lastRow) ShowRow(row);

        _firstRow = first;
        _lastRow = last;
        return true;
    }

    private int RowAt(Vector3 worldPoint)
    {
        float belowTop = _list.rect.yMax - _list.InverseTransformPoint(worldPoint).y - _grid.padding.top;
        return Mathf.FloorToInt(belowTop / (_grid.cellSize.y + _grid.spacing.y));
    }

    private void ShowRow(int row)
    {
        int cols = Columns;
        for (int slot = Mathf.Max(1, row * cols); slot < (row + 1) * cols && slot < Slots; slot++)
        {
            var button = _buttons[slot];
            if (button == null) _buttons[slot] = Create(slot);
            else button.gameObject.SetActive(true);
        }
    }

    private void HideRow(int row)
    {
        int cols = Columns;
        for (int slot = Mathf.Max(1, row * cols); slot < (row + 1) * cols && slot < Slots; slot++)
        {
            var button = _buttons[slot];
            if (button != null) button.gameObject.SetActive(false);
        }
    }

    private MenuElementCosmeticButton Create(int slot)
    {
        var (rep, members) = _items[slot - 1];
        var button = Instantiate(_page.sectionButtonPrefab, _list).GetComponent<MenuElementCosmeticButton>();
        button.cosmeticAsset = rep;
        if (members != null && members.Count > 1)
            CosmeticGroupButton.Attach(button, members);
        FavHideMarkerHelper.UpdateMarker(button);
        Place((RectTransform)button.transform, slot);

        // Start only runs on the next frame. Until then, a row that appears on screen would show the prefab's blue border and color button.
        button.bgBorder.color = Color.clear;
        button.tintableButton.gameObject.SetActive(false);
        return button;
    }

    // Same math as GridLayoutGroup.
    private void Place(RectTransform rect, int slot)
    {
        int cols = Columns;
        rect.anchorMin = rect.anchorMax = Vector2.up;
        rect.sizeDelta = _grid.cellSize;
        rect.anchoredPosition = new Vector2(
            _grid.padding.left + slot % cols * (_grid.cellSize.x + _grid.spacing.x) + _grid.cellSize.x * rect.pivot.x,
            -_grid.padding.top - slot / cols * (_grid.cellSize.y + _grid.spacing.y) - _grid.cellSize.y * (1f - rect.pivot.y));
    }

    // A mod can hide a button in its Start, as YoshiCarry does with duplicates. This closes the gap, as GridLayoutGroup would.
    private bool DropButtonsHiddenByMods()
    {
        int cols = Columns;
        int rowsBefore = Rows;
        bool dropped = false;
        for (int slot = Mathf.Min((_lastRow + 1) * cols, Slots) - 1; slot >= Mathf.Max(1, _firstRow * cols); slot--)
        {
            var button = _buttons[slot];
            if (button == null || button.gameObject.activeSelf) continue;

            _hiddenByMods.Add(button.cosmeticAsset);
            Destroy(button.gameObject);
            _buttons.RemoveAt(slot);
            _items.RemoveAt(slot - 1);
            dropped = true;
        }
        if (!dropped) return false;

        for (int slot = 1; slot < Slots; slot++)
        {
            var button = _buttons[slot];
            if (button == null) continue;

            int row = slot / cols;
            Place((RectTransform)button.transform, slot);
            button.gameObject.SetActive(row >= _firstRow && row <= _lastRow);
        }
        for (int row = _firstRow; row <= _lastRow; row++)
            ShowRow(row);

        // On the next frame: RecalculateScrollHeight would still count the buttons destroyed in this one.
        _reflow |= Rows != rowsBefore || Count == 0;
        return true;
    }

    // Where RecalculateScrollHeight would find the bottom row if it existed: the unequip button's position, moved down by the rows.
    private float BottomSelectablePos(Transform scroller)
    {
        var menuButton = _section.unequipButton.menuButton;
        if (menuButton == null || menuButton.menuSelectableElement == null) return float.MaxValue;

        var element = (RectTransform)menuButton.menuSelectableElement.transform;
        float y = element.localPosition.y - element.rect.height * element.pivot.y;
        for (var parent = element.parent; parent != null && parent != scroller; parent = parent.parent)
            y += parent.localPosition.y;

        return y - (Rows - 1) * (_grid.cellSize.y + _grid.spacing.y);
    }
}
