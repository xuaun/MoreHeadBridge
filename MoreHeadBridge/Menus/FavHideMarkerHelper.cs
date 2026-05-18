// ============================================================================
// Helper that adds / updates the small star or hidden-eye badge on a cosmetic
// button to indicate favorite or hidden status.
//
// The badge is a plain child RectTransform + UnityEngine.UI.Image that is
// always kept as the LAST sibling of the button, so Unity's Canvas draws it
// on top of every other button element without needing a nested Canvas or
// overrideSorting tricks.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class FavHideMarkerHelper
{
    private const string MarkerName = "MHB_FavHideMarker";

    // Badge geometry — anchored to the bottom-right corner of the button.
    private const float OffsetX = -7f;
    private const float OffsetY =  7f;
    private const float Size    =  9f;

    // Creates or updates the marker badge on the given button.
    // Call whenever fav/hide state changes, or during RefreshScrollContent.
    internal static void UpdateMarker(MenuElementCosmeticButton btn)
    {
        if (btn == null || btn.cosmeticAsset == null) return;

        bool isFav  = BridgeFavoritesManager.IsFavorite(btn.cosmeticAsset);
        bool isHide = BridgeFavoritesManager.IsHidden(btn.cosmeticAsset);

        var existing = btn.transform.Find(MarkerName);

        if (!isFav && !isHide)
        {
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            return;
        }

        Image? img;

        if (existing == null)
        {
            // Create a plain UI child: RectTransform first, then Image.
            // Being the last sibling is sufficient for Canvas draw order
            // (later sibling = rendered on top of earlier siblings).
            var markerGO = new GameObject(MarkerName);
            var rt       = markerGO.AddComponent<RectTransform>();
            markerGO.transform.SetParent(btn.transform, false);
            markerGO.transform.SetAsLastSibling();

            // Anchor to the bottom-right corner of the button.
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            ApplyRect(rt);

            img               = markerGO.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
        }
        else
        {
            // Re-assert last-sibling in case vanilla UpdateIcon reshuffled children.
            existing.SetAsLastSibling();
            ApplyRect(existing.GetComponent<RectTransform>());
            img = existing.GetComponent<Image>();
        }

        if (img == null) return;

        if (isFav)
        {
            img.sprite = FavHideIcons.StarSprite;
            img.color  = Color.white;
        }
        else
        {
            img.sprite = FavHideIcons.HideSprite;
            img.color  = Color.white;
        }
    }

    private static void ApplyRect(RectTransform? rt)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(OffsetX, OffsetY);
        rt.sizeDelta        = new Vector2(Size,    Size);
    }
}
