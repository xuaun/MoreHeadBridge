using System;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

// MenuScrollBox.Update reads SemiFunc.InputScrollY directly (no EventSystem); with fullPageScroll every box reacts to any scroll — disable all boxes NOT inside the popup. Restored automatically on disable/destroy, so every close path is covered with no per-button cleanup.
internal sealed class PopupScrollGuard : MonoBehaviour
{
    private MenuScrollBox[] _targets = Array.Empty<MenuScrollBox>();

    internal void Init(Transform popupRoot)
    {
        // Only disable currently-enabled boxes so we don't re-enable ones that were already off for unrelated reasons.
        _targets = UnityEngine.Object.FindObjectsOfType<MenuScrollBox>(true)
            .Where(sb => sb.enabled && !sb.transform.IsChildOf(popupRoot))
            .ToArray();

        foreach (var sb in _targets) sb.enabled = false;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    private void Restore()
    {
        foreach (var sb in _targets)
            if (sb != null) sb.enabled = true;
        _targets = Array.Empty<MenuScrollBox>(); // prevent double-restore
    }
}
