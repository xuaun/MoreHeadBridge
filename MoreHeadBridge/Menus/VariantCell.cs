using System;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Drives a cloned cell's border/background to mimic the native idle/hover/selected look (border = base × 0.3 / 0.65 / 1.0; bg darkens when selected).
// Hover comes from the native MenuButton (REPO polls the mouse itself — IPointer events don't fire here); clicks run MenuButton → ToggleCosmetic → FavHideTogglePatch → OnClick.
internal sealed class VariantCell : MonoBehaviour
{
    internal MenuButton? Button;
    internal RawImage? Border;
    internal RawImage? BgMain;
    internal Color BaseColor = Color.white;
    internal bool Equipped;
    internal Action? OnClick;
    internal Action? OnHoverEnter;   // preview the variant on the avatar
    internal Action? OnHoverExit;    // clear the preview

    private bool _lastHover;
    private bool _init;

    private void Update()
    {
        bool hover = Button != null && Button.hovering;
        if (!_init) { _init = true; _lastHover = hover; Apply(hover); return; }
        if (hover == _lastHover) return;

        _lastHover = hover;
        Apply(hover);
        if (hover) OnHoverEnter?.Invoke();
        else OnHoverExit?.Invoke();
    }

    internal void Refresh() => Apply(_lastHover);

    private void Apply(bool hover)
    {
        if (Border != null)
        {
            float scale = Equipped ? 1f : (hover ? 0.65f : 0.3f);
            Border.color = new Color(BaseColor.r * scale, BaseColor.g * scale, BaseColor.b * scale, BaseColor.a);
        }
        if (BgMain != null)
            BgMain.color = Equipped ? new Color32(0, 0, 0, 175) : new Color32(0, 0, 0, 255);
    }
}
