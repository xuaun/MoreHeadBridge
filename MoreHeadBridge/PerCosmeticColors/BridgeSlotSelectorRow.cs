using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Horizontal slot-selector row injected below the colour palette (by OriginalColorButtonPatch.Postfix) for bridge cosmetics exposing more than one material slot across all BridgeTintMaterial components.
// Layout:   [<]  ALL  1  2  3 … 7  [>]   — ALL = whole-asset (ActiveSlot -1); 1…N = that material slot (ActiveSlot = flatSlot); [<]/[>] appear only when N > MaxVisibleSlots.
// Button background = that slot's colour (selected full, unselected dimmed): stored palette override wins, else the slot's OWN original colour (ALL shows the first slot's). ActiveSlot is reset to -1 by ColorPageClosePatch when the page is destroyed.
internal sealed class BridgeSlotSelectorRow : MonoBehaviour
{
    // Scroll arrows appear only when there are more than this many numbered slots.
    private const int MaxVisibleSlots = 7;

    // Button geometry (matches the vanilla colour grid spacing).
    private const float ButtonWidth = 38f;
    private const float ButtonGap = 0f;
    private const float ArrowWidth = 20f;
    internal const float SlotSelectorH = 30f;
    private const float AllLabelFontSize = 11.5f;
    private const float NumberLabelFontSize = 13.5f;
    private const float ArrowLabelFontSize = 22f;
    // The vanilla colour-chip glyph renders below the TMP rect's centre — this offset shifts the TMP/overlay up for visual centring; it does NOT affect the hit/hover rect.
    private const float GlyphYOffset = 5f;
    private const float NumberOutlineWidth = 0.50f;
    private const float AllOutlineWidth = 0.36f;
    private const float ArrowOutlineWidth = 0.24f;

    // The currently-visible slot selector row; kept so CosmeticColorSetPatch can Refresh() it right after a colour is picked, updating button tints.
    internal static BridgeSlotSelectorRow? Active;

    private void OnEnable() { if (Active == null) Active = this; }
    private void OnDestroy() { if (Active == this) Active = null; }

    // Set by OriginalColorButtonPatch before Start() fires.
    internal int slotCount;
    internal float containerWidth;
    internal CosmeticAsset? cosmeticAsset;
    internal GameObject? buttonTemplate;
    internal MenuPageColor? menuPageColor;

    // [0] = ALL, [1..slotCount] = slot 1..N
    private readonly List<(GameObject go, MenuButton? btn)> _slotButtons = new();
    private GameObject? _arrowLeft;
    private GameObject? _arrowRight;

    // First visible numbered-slot index (0-based). 0 means slot-button 1 is the leftmost.
    private int _scrollOffset;

    private void Start()
    {
        if (buttonTemplate == null) return;
        BuildButtons();
        Refresh();
    }

    // ── Button construction ────────────────────────────────────────────────────

    private void BuildButtons()
    {
        var parent = transform;

        // ALL button (always visible, slot index -1)
        _slotButtons.Add(MakeSlotButton(parent, "ALL", -1));

        // Numbered slot buttons (slot index 0 = first material slot)
        for (int i = 0; i < slotCount; i++)
            _slotButtons.Add(MakeSlotButton(parent, (i + 1).ToString(), i));

        // Scroll arrows (conditionally visible)
        _arrowLeft = MakeArrow(parent, false);
        _arrowRight = MakeArrow(parent, true);
    }

    private (GameObject go, MenuButton? btn) MakeSlotButton(Transform parent, string label, int slotIdx)
    {
        var go = Object.Instantiate(buttonTemplate!, parent);
        go.name = $"SlotBtn_{label}";

        // Suppress the vanilla colour-selection logic before it can run Start()/LateStart().
        var vanilla = go.GetComponent<MenuButtonColor>();
        if (vanilla != null) { vanilla.enabled = false; Object.Destroy(vanilla); }

        ResetVanillaButton(go);
        var labelRT = AddOverlayLabel(go, label);

        var proxy = go.AddComponent<SlotButtonProxy>();
        proxy.row = this;
        proxy.slotIndex = slotIdx;
        proxy.labelRT = labelRT;
        proxy.baseY = 0f;

        return (go, go.GetComponent<MenuButton>());
    }

    private GameObject MakeArrow(Transform parent, bool right)
    {
        var go = Object.Instantiate(buttonTemplate!, parent);
        go.name = right ? "SlotArrowRight" : "SlotArrowLeft";

        var vanilla = go.GetComponent<MenuButtonColor>();
        if (vanilla != null) { vanilla.enabled = false; Object.Destroy(vanilla); }

        ResetVanillaButton(go);
        var labelRT = AddOverlayLabel(go, right ? ">" : "<");

        var proxy = go.AddComponent<ArrowButtonProxy>();
        proxy.row = this;
        proxy.right = right;
        proxy.labelRT = labelRT;
        proxy.baseY = 0f;

        return go;
    }

    // Keep the prefab intact with a separate TMP overlay, copying the original TMP font/material so the label keeps the vanilla look.
    private static void ResetVanillaButton(GameObject go)
    {
        var menuBtn = go.GetComponent<MenuButton>();
        if (menuBtn != null)
            menuBtn.customColors = false;
    }

    private static RectTransform AddOverlayLabel(GameObject go, string text)
    {
        var templateLabel = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (templateLabel == null) return go.GetComponent<RectTransform>()!;

        var labelGO = new GameObject("SlotLabel");
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = templateLabel.font;
        tmp.fontSize = text == "ALL"
            ? AllLabelFontSize
            : text == ">" || text == "<"
                ? ArrowLabelFontSize
                : NumberLabelFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.85f);
        tmp.raycastTarget = false;

        ApplyOutline(tmp, templateLabel, text == "ALL"
            ? AllOutlineWidth
            : text == ">" || text == "<"
                ? ArrowOutlineWidth
                : NumberOutlineWidth);

        // Explicit centre-anchor with fixed size, NOT stretch anchors: after Place() changes the button pivot to (0, 0.5), stretch-anchored positions shift and push the label out of the rect.
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0.5f, 0.5f);
        labelRT.anchorMax = new Vector2(0.5f, 0.5f);
        labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta = new Vector2(ButtonWidth, SlotSelectorH);
        return labelRT;
    }

    internal static void ApplyOutline(TextMeshProUGUI target, TextMeshProUGUI templateLabel, float outlineWidth)
    {
        if (templateLabel.fontSharedMaterial == null) return;

        var outline = Object.Instantiate(templateLabel.fontSharedMaterial);
        outline.SetFloat("_OutlineWidth", outlineWidth);
        outline.SetColor("_OutlineColor", Color.black);
        target.fontSharedMaterial = outline;

        var destroyer = target.gameObject.AddComponent<MaterialDestroyer>();
        destroyer.material = outline;
    }

    // ── Layout & styling ───────────────────────────────────────────────────────

    // Called from Start() and from proxies after every interaction.
    internal void Refresh()
    {
        int activeSlot = PerCosmeticColors.ActiveSlot;
        bool needArrows = slotCount > MaxVisibleSlots;

        // Clamp scroll offset so the visible window is always full (or fits remaining slots).
        int maxOffset = Mathf.Max(0, slotCount - MaxVisibleSlots);
        _scrollOffset = Mathf.Clamp(_scrollOffset, 0, maxOffset);

        bool showLeft = needArrows && _scrollOffset > 0;
        bool showRight = needArrows && _scrollOffset < maxOffset;

        float x = 0f;

        // Left arrow — space is always reserved so ALL/numbered buttons start at a constant X.
        if (_arrowLeft != null)
        {
            _arrowLeft.SetActive(showLeft);
            Place(_arrowLeft, ref x, ArrowWidth);
        }

        // ALL button (index 0 in _slotButtons, always shown)
        {
            var (go, btn) = _slotButtons[0];
            go.SetActive(true);
            Place(go, ref x, ButtonWidth);
            Style(btn, activeSlot == -1, GetSlotTintColor(-1));
        }

        // Numbered slot buttons (1-based label = flatSlot+1)
        for (int i = 0; i < slotCount; i++)
        {
            var (go, btn) = _slotButtons[i + 1];

            bool inWindow = !needArrows
                         || (i >= _scrollOffset && i < _scrollOffset + MaxVisibleSlots);

            go.SetActive(inWindow);
            if (inWindow)
            {
                Place(go, ref x, ButtonWidth);
                Style(btn, activeSlot == i, GetSlotTintColor(i));
            }
        }

        // Right arrow
        if (_arrowRight != null)
        {
            _arrowRight.SetActive(showRight);
            if (showRight) Place(_arrowRight, ref x, ArrowWidth);
        }
    }

    // Original material colour of each flat slot, built once per page session (constant while the picker is open) so each slot button previews its own original colour.
    private Color[]? _slotOriginals;

    // Background colour for a slot button: stored palette override wins (per-slot, or whole-asset for ALL); otherwise the slot's OWN original material colour (ALL = first slot).
    private Color? GetSlotTintColor(int slotIndex)
    {
        if (cosmeticAsset == null || MetaManager.instance?.colors == null) return null;
        string assetId = cosmeticAsset.assetId;
        var colors = MetaManager.instance.colors;

        // ALL button: whole animation > whole custom > whole palette > first slot's original.
        if (slotIndex < 0)
        {
            if (PerCosmeticColors.TryGetAnimation(assetId, out var allAnim)) return AnimRepresentative(allAnim);
            if (PerCosmeticColors.TryGetCustomColor(assetId, out var allCustom)) return allCustom;
            if (PerCosmeticColors.TryGetColor(assetId, out int allIdx)
                && allIdx != PerCosmeticColors.OriginalColorSentinel
                && allIdx >= 0 && allIdx < colors.Count)
                return colors[allIdx].color;
            return SlotOriginalColor(0);
        }

        // Numbered slot — same priority as the apply: per-slot animation > per-slot custom > per-slot index > whole animation > whole custom > whole index > this slot's original.
        if (PerCosmeticColors.TryGetSlotAnimation(assetId, slotIndex, out var slotAnim))
            return AnimRepresentative(slotAnim);

        if (PerCosmeticColors.TryGetCustomSlotColor(assetId, slotIndex, out var slotCustom))
            return slotCustom;

        if (PerCosmeticColors.TryGetSlotColor(assetId, slotIndex, out int slotIdx))
        {
            if (slotIdx == PerCosmeticColors.OriginalColorSentinel)
                return SlotOriginalColor(slotIndex);
            if (slotIdx >= 0 && slotIdx < colors.Count)
                return colors[slotIdx].color;
        }

        if (PerCosmeticColors.TryGetAnimation(assetId, out var wholeAnim)) return AnimRepresentative(wholeAnim);
        if (PerCosmeticColors.TryGetCustomColor(assetId, out var wholeCustom)) return wholeCustom;
        if (PerCosmeticColors.TryGetColor(assetId, out int wholeIdx)
            && wholeIdx != PerCosmeticColors.OriginalColorSentinel
            && wholeIdx >= 0 && wholeIdx < colors.Count)
            return colors[wholeIdx].color;

        return SlotOriginalColor(slotIndex);
    }

    // Solid stand-in for an animation on a slot button (can't show motion): first palette entry for Cycle, or a vivid hue for Rainbow.
    private static Color AnimRepresentative(ColorAnimation spec)
    {
        if (spec.Mode == ColorAnimMode.Rainbow) return Color.HSVToRGB(0.83f, 0.85f, 1f);
        var pal = MetaManager.instance?.colors;
        if (spec.Palette is { Count: > 0 } && pal != null)
        {
            int idx = spec.Palette[0];
            if (idx >= 0 && idx < pal.Count) return pal[idx].color;
        }
        return Color.white;
    }

    private Color SlotOriginalColor(int flatSlot)
    {
        _slotOriginals ??= BridgeOriginalColorButton.BuildSlotOriginalColors(cosmeticAsset!, slotCount);
        return flatSlot >= 0 && flatSlot < _slotOriginals.Length ? _slotOriginals[flatSlot] : Color.white;
    }

    // MenuButton.Awake() captures buttonText.localPosition and ButtonNormal() restores it each frame — captured against the TEMPLATE's pivot/anchors, so after Place() re-pivots the button the glyph lands wrong.
    // Fix: reshape the TMP to the button's anchor+pivot convention (localPosition (0,0) = left-centre for both), then store the post-reshape localPosition back into MenuButton's private fields via Traverse.
    private static void Place(GameObject go, ref float x, float w)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);  // stretch vertically to container height
        // pivot.y = 0 so UIGetRectTransformPositionOnScreen yields the bottom edge, which UIMouseHover expects for correct [bottom, top] hit bounds.
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(w, 0f);   // height driven by stretch

        // Reshape the TMP to fill the button with the button's own anchor/pivot, so localPosition=(0,0) is correct in the new coordinate system.
        var menuBtn = go.GetComponent<MenuButton>();
        if (menuBtn?.buttonText != null)
        {
            var textRT = menuBtn.buttonText.GetComponent<RectTransform>();
            if (textRT != null)
            {
                textRT.anchorMin = new Vector2(0f, 0f);
                textRT.anchorMax = new Vector2(0f, 1f);   // match button
                textRT.pivot = new Vector2(0f, 0f); // match button
                textRT.anchoredPosition = new Vector2(0f, GlyphYOffset);
                textRT.sizeDelta = new Vector2(w, 0f);
                menuBtn.buttonText.alignment = TextAlignmentOptions.Center;
            }
            // Read the localPosition Unity computed from the RT setup above and store it so MenuButton.ButtonNormal() restores to the correct position.
            var storedPos = menuBtn.buttonText.transform.localPosition;
            Traverse.Create(menuBtn).Field("buttonTextSelectedOriginalPos").SetValue(storedPos);
            Traverse.Create(menuBtn).Field("buttonTextHoverPos").SetValue(storedPos + new Vector3(0f, 1f, 0f));
        }

        x += w + ButtonGap;
    }

    // Styles a slot button with its resolved colour (selected = full tint, unselected = dimmed; grey only as last-resort fallback). Alpha is preserved, not scaled, so buttons stay opaque.
    private static void Style(MenuButton? btn, bool selected, Color? tint)
    {
        if (btn == null) return;

        if (tint.HasValue)
        {
            Color c = tint.Value;
            btn.colorNormal = selected ? c : new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a);
            btn.colorHover = selected ? c + Color.white * 0.15f : new Color(c.r * 0.65f, c.g * 0.65f, c.b * 0.65f, c.a);
            btn.colorClick = Color.white;
        }
        else
        {
            btn.colorNormal = selected ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.32f, 0.32f, 0.32f);
            btn.colorHover = selected ? Color.white : new Color(0.60f, 0.60f, 0.60f);
            btn.colorClick = Color.white;
        }

    }

    // ── Click callbacks ────────────────────────────────────────────────────────

    internal void OnSlotClicked(int slotIndex)
    {
        PerCosmeticColors.ActiveSlot = slotIndex;
        Refresh();
        UpdateOriginalButtonColor();   // refresh "M"/"C" colour first so the ring can land on them
        UpdateCustomButtonColor();
        UpdateColorIndicator();
    }

    private void UpdateCustomButtonColor()
    {
        if (cosmeticAsset == null || menuPageColor?.colorButtonHolder == null) return;
        var custBtn = menuPageColor.colorButtonHolder.GetComponentInChildren<BridgeCustomColorButton>(true);
        if (custBtn != null
            && PerCosmeticColors.TryGetSlotCustom(cosmeticAsset.assetId, PerCosmeticColors.ActiveSlot, out var c))
            custBtn.SetDisplayColor(c);
    }

    // Re-tints the "M" Original button to the active slot's original colour (ALL = first slot) so it previews exactly what clicking it would restore.
    private void UpdateOriginalButtonColor()
    {
        if (cosmeticAsset == null || menuPageColor?.colorButtonHolder == null) return;
        var origBtn = menuPageColor.colorButtonHolder.GetComponentInChildren<BridgeOriginalColorButton>(true);
        int activeSlot = PerCosmeticColors.ActiveSlot;
        origBtn?.SetDisplayColor(SlotOriginalColor(activeSlot < 0 ? 0 : activeSlot));
    }

    internal void OnArrowClicked(bool right)
    {
        _scrollOffset = right
            ? Mathf.Min(_scrollOffset + 1, Mathf.Max(0, slotCount - MaxVisibleSlots))
            : Mathf.Max(_scrollOffset - 1, 0);
        Refresh();
    }

    // ── Color indicator sync ───────────────────────────────────────────────────

    // Springs the vanilla ring onto whatever represents the active slot's colour: the matching palette swatch, or the "M" button in original mode (resolves like GetSlotTintColor, including whole-asset inheritance).
    private void UpdateColorIndicator()
    {
        if (menuPageColor == null || cosmeticAsset == null || MetaManager.instance?.colors == null) return;
        string assetId = cosmeticAsset.assetId;

        int activeSlot = PerCosmeticColors.ActiveSlot;

        // Animation resolves first (per-slot, or whole when the slot has no per-slot override).
        if (PerCosmeticColors.IsSlotAnimated(assetId, activeSlot)) { SelectAnimateButton(); return; }

        // Custom next (per-slot custom, or whole custom when the slot has no override).
        if (PerCosmeticColors.IsSlotCustom(assetId, activeSlot)) { SelectCustomButton(); return; }

        int colorIdx;
        bool isOriginal;
        if (activeSlot >= 0 && PerCosmeticColors.TryGetSlotColor(assetId, activeSlot, out colorIdx))
            isOriginal = colorIdx == PerCosmeticColors.OriginalColorSentinel;
        else  // ALL, or a numbered slot with no per-slot entry → inherit the whole-asset colour.
            isOriginal = !PerCosmeticColors.TryGetColor(assetId, out colorIdx)
                         || colorIdx == PerCosmeticColors.OriginalColorSentinel;

        // Original → put the ring on the "M" button instead of a palette swatch.
        if (isOriginal) { SelectOriginalButton(); return; }
        if (colorIdx < 0 || colorIdx >= MetaManager.instance.colors.Count) return;

        var holder = menuPageColor.colorButtonHolder;
        if (holder == null) return;

        // menuColorSelected.SetColor directly — skips the Confirm sound inside SetColor.
        foreach (var mbc in holder.GetComponentsInChildren<MenuButtonColor>(includeInactive: false))
        {
            if (mbc.colorID != colorIdx) continue;
            var mbc_rt = mbc.GetComponent<RectTransform>();
            if (mbc_rt == null) return;

            Color c = MetaManager.instance.colors[colorIdx].color;
            Vector3 pos = mbc_rt.position
                         + new Vector3(mbc_rt.rect.width / 2f, mbc_rt.rect.height / 2f, 0f);

            menuPageColor.menuColorSelected.gameObject.SetActive(true);
            menuPageColor.menuColorSelected.SetColor(c, pos);
            return;
        }
    }

    private void SelectOriginalButton()
    {
        if (menuPageColor?.colorButtonHolder == null || menuPageColor.menuColorSelected == null) return;
        var origBtn = menuPageColor.colorButtonHolder.GetComponentInChildren<BridgeOriginalColorButton>(true);
        var rt = origBtn != null ? origBtn.GetComponent<RectTransform>() : null;
        if (rt == null) return;

        Vector3 pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        menuPageColor.menuColorSelected.SetColor(origBtn!.originalColor, pos);
    }

    private void SelectAnimateButton()
    {
        if (menuPageColor?.colorButtonHolder == null || menuPageColor.menuColorSelected == null) return;
        var animBtn = menuPageColor.colorButtonHolder.GetComponentInChildren<BridgeAnimateButton>(true);
        var rt = animBtn != null ? animBtn.GetComponent<RectTransform>() : null;
        if (rt == null) return;

        Color col = animBtn!.selectedColor;
        int activeSlot = PerCosmeticColors.ActiveSlot;
        if (activeSlot >= 0 && PerCosmeticColors.TryGetSlotAnimation(cosmeticAsset!.assetId, activeSlot, out var sa))
            col = AnimRepresentative(sa);
        else if (PerCosmeticColors.TryGetAnimation(cosmeticAsset!.assetId, out var wa))
            col = AnimRepresentative(wa);

        Vector3 pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        menuPageColor.menuColorSelected.SetColor(col, pos);
    }

    private void SelectCustomButton()
    {
        if (menuPageColor?.colorButtonHolder == null || menuPageColor.menuColorSelected == null) return;
        var custBtn = menuPageColor.colorButtonHolder.GetComponentInChildren<BridgeCustomColorButton>(true);
        var rt = custBtn != null ? custBtn.GetComponent<RectTransform>() : null;
        if (rt == null) return;

        Color col = PerCosmeticColors.TryGetSlotCustom(cosmeticAsset!.assetId, PerCosmeticColors.ActiveSlot, out var c)
            ? c : custBtn!.selectedColor;
        Vector3 pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        menuPageColor.menuColorSelected.SetColor(col, pos);
    }
}
