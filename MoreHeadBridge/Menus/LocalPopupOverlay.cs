using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Semi-transparent blocker parented to the PARENT popup, dimming it (not the whole screen) while the sub-popup is open. Destroyed in OnDestroy → covers every close path.
internal sealed class LocalPopupOverlay : MonoBehaviour
{
    // Alpha of the semi-transparent dimmer placed over the parent popup.
    private const float DimmerAlpha = 0.7f;

    private GameObject? _overlay;

    /// Adds a semi-transparent blocking overlay inside <paramref name="parentPopupTransform"/>
    /// and attaches this component to <paramref name="subPopupGo"/> for lifecycle management.
    internal static void Add(GameObject subPopupGo, Transform parentPopupTransform)
    {
        var comp = subPopupGo.AddComponent<LocalPopupOverlay>();
        comp.CreateOverlay(parentPopupTransform);
    }

    private void CreateOverlay(Transform parentPopupTransform)
    {
        var go = new GameObject("LocalDimmer", typeof(RectTransform));
        go.transform.SetParent(parentPopupTransform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, DimmerAlpha);
        img.raycastTarget = true;

        // Overlay must be above all popup content.
        rt.SetAsLastSibling();
        _overlay = go;

        // Raise the preview avatar (and its label) above the dimmer. If the parent doesn't host the avatar (offset entry popup), fall back to a scene-wide search.
        var preview = parentPopupTransform.GetComponentInChildren<REPOAvatarPreview>(true)
                      ?? Object.FindObjectOfType<REPOAvatarPreview>();
        if (preview != null)
            preview.transform.SetAsLastSibling();
    }

    private void OnDestroy()
    {
        if (_overlay != null)
            Destroy(_overlay);
    }
}
