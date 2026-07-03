using System.Collections;
using UnityEngine;

namespace MoreHeadBridge;

// Click proxy for the "A" (Animate) button on the colour page: opens ColorAnimationPopup and owns the selection ring while an animation is active. Click-edge pattern mirrors SlotButtonProxy.
internal sealed class BridgeAnimateButton : MonoBehaviour
{
    // The live "A" button, so ColorAnimationPopup can move the ring onto it after a save.
    internal static BridgeAnimateButton? Active;

    internal CosmeticAsset? cosmeticAsset;
    internal RectTransform? labelRT;
    internal MenuPageColor? menuPageColor;
    internal Color selectedColor = Color.white;
    internal bool initiallySelected;

    private MenuButton? _btn;
    private bool _wasClicked;

    private void Awake() => _btn = GetComponent<MenuButton>();
    private void OnEnable() { if (Active == null) Active = this; }
    private void OnDestroy() { if (Active == this) Active = null; }

    private IEnumerator Start()
    {
        if (!initiallySelected) yield break;

        // Mirror BridgeOriginalColorButton.Start: wait for the page to finish animating in.
        yield return new WaitForSeconds(0.1f);
        var page = GetComponentInParent<MenuPage>();
        if (page != null)
            while (page.currentPageState != MenuPage.PageState.Active)
                yield return new WaitForSeconds(0.1f);

        SelectRingNow();
    }

    // Moves the colour-selected ring onto this button (at open, and after saving an animation).
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
        bool clicked = _btn != null && _btn.clicked;
        if (!clicked) { _wasClicked = false; return; }
        if (_wasClicked) return;
        _wasClicked = true;

        MenuManager.instance?.MenuEffectClick(MenuManager.MenuClickEffectType.Confirm);
        ColorAnimationPopup.Show(cosmeticAsset);
    }

    private void LateUpdate()
    {
        if (_btn == null || labelRT == null) return;
        var pos = labelRT.anchoredPosition;
        float targetY = _btn.hovering ? 1f : 0f;
        if (Mathf.Approximately(pos.y, targetY)) return;
        labelRT.anchoredPosition = new Vector2(pos.x, targetY);
    }
}
