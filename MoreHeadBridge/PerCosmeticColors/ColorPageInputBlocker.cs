using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// The colour page's buttons use REPO's own mouse polling, so the popup dimmer does NOT stop them — disable every MenuButton under MenuPageColor on open, re-enable exactly those on close (OnDestroy; popup isn't cached).
internal sealed class ColorPageInputBlocker : MonoBehaviour
{
    private readonly List<MenuButton> _disabled = new();

    internal void Block()
    {
        var page = Object.FindObjectOfType<MenuPageColor>();
        if (page == null) return;

        foreach (var mb in page.GetComponentsInChildren<MenuButton>(includeInactive: true))
        {
            if (mb != null && mb.enabled)
            {
                mb.enabled = false;        // stops MenuButton.Update → no hover/click
                _disabled.Add(mb);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var mb in _disabled)
            if (mb != null) mb.enabled = true;
    }
}
