using System.Collections;
using UnityEngine;

namespace MoreHeadBridge;

// Click proxy for the "C" (Custom Colour) button on the colour page: opens CustomColorPopup and owns the selection ring while a custom colour is set. Visuals mirror the "M" button (flash, sink below ring, fade back, hover nudge).
internal sealed class BridgeCustomColorButton : MonoBehaviour
{
    private const float PopFadeDuration = 0.1f;

    // The live "C" button, so CustomColorPopup can re-tint it / move the ring after a save.
    internal static BridgeCustomColorButton? Active;

    internal MenuPageColor? menuPageColor;
    internal CosmeticAsset? cosmeticAsset;
    internal RectTransform? labelRT;
    internal Color selectedColor = Color.white;   // current custom colour (button tint + ring)
    internal bool initiallySelected;

    // Section-mode fields: when true, clicking applies custom RGB to all eligible cosmetics in the section instead of a single per-cosmetic asset.
    internal bool sectionMode;
    internal int sectionColorKey;
    internal MenuPageColor.ColorPageType sectionPageMode;

    private MenuButton? _menuButton;
    private bool _buttonClicked;
    private bool _flashSunk;
    private CanvasGroup? _canvasGroup;
    private float _fadeT = -1f;

    private void Awake() => _menuButton = GetComponent<MenuButton>();
    private void OnEnable() { if (Active == null) Active = this; }
    private void OnDestroy() { if (Active == this) Active = null; }

    private IEnumerator Start()
    {
        if (!initiallySelected) yield break;
        yield return new WaitForSeconds(0.1f);
        var page = GetComponentInParent<MenuPage>();
        if (page != null)
            while (page.currentPageState != MenuPage.PageState.Active)
                yield return new WaitForSeconds(0.1f);
        SelectRingNow();
    }

    // Re-tints the button to a custom colour (called after a save so it previews that colour).
    internal void SetDisplayColor(Color c)
    {
        selectedColor = c;
        _menuButton ??= GetComponent<MenuButton>();
        if (_menuButton == null) return;
        _menuButton.colorNormal = c + Color.black * 0.5f;
        _menuButton.colorHover = c;
        _menuButton.colorClick = c + Color.white * BridgeOriginalColorButton.ClickWhiteAmount;
    }

    // Springs the selection ring onto this button (at open, and after saving a custom colour).
    internal void SelectRingNow()
    {
        if (menuPageColor?.menuColorSelected == null) return;
        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        var rt = GetComponent<RectTransform>();
        var pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.SetColor(selectedColor, pos);
    }

    private void Update()
    {
        if (_menuButton == null) return;
        bool clicked = _menuButton.clicked;

        // Sink below the ring during the selecting flash (ring fill hides the white), then pop back above it and fade in — same as the "M" button.
        if (clicked)
        {
            if (!_flashSunk && !IsCurrentlySelected()) { _flashSunk = true; SinkBelowRing(); }
        }
        else if (_flashSunk)
        {
            _flashSunk = false;
            transform.SetAsLastSibling();
            BeginPopFade();
        }

        if (_fadeT >= 0f)
        {
            _fadeT += Time.deltaTime;
            float a = Mathf.Clamp01(_fadeT / PopFadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = a;
            if (a >= 1f) _fadeT = -1f;
        }

        if (!clicked)
        {
            _buttonClicked = false;
            _menuButton.colorClick = IsCurrentlySelected()
                ? selectedColor
                : selectedColor + Color.white * BridgeOriginalColorButton.ClickWhiteAmount;
            return;
        }

        if (_buttonClicked) return;
        _buttonClicked = true;
        MenuManager.instance?.MenuEffectClick(MenuManager.MenuClickEffectType.Confirm);
        if (sectionMode)
            CustomColorPopup.Show(null, sectionColorKey, sectionPageMode);
        else
            CustomColorPopup.Show(cosmeticAsset);
    }

    private bool IsCurrentlySelected()
    {
        if (sectionMode || cosmeticAsset == null) return false;
        return PerCosmeticColors.IsSlotCustom(cosmeticAsset.assetId, PerCosmeticColors.ActiveSlot);
    }

    private void SinkBelowRing()
    {
        var ring = menuPageColor?.menuColorSelected;
        if (ring == null) return;
        transform.SetSiblingIndex(ring.transform.GetSiblingIndex());
    }

    private void BeginPopFade()
    {
        _canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _fadeT = 0f;
    }

    private void LateUpdate()
    {
        if (_menuButton == null || labelRT == null) return;
        var pos = labelRT.anchoredPosition;
        float targetY = _menuButton.hovering ? 1f : 0f;
        if (Mathf.Approximately(pos.y, targetY)) return;
        labelRT.anchoredPosition = new Vector2(pos.x, targetY);
    }
}
