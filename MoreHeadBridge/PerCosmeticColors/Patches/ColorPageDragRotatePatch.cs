using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine;

namespace MoreHeadBridge;

// Click-drag avatar rotation while the colour picker is open — PlayerAvatarMenuHover provides this on the cosmetics page but the colour page overlays its hover area.
//
// Only active when PendingAsset is a bridge cosmetic; vanilla colour pages are unchanged.
//
// Drag proportional to horizontal displacement (vanilla spring feel); a 2 px threshold separates drags from plain colour-button clicks.
[HarmonyPatch(typeof(MenuPageColor), "Update")]
internal static class ColorPageDragRotatePatch
{
    private const float DragThresholdPixels = 2f;  // min displacement to count as a drag, not a click
    private const float RotateDegreesPerPixel = 10f;

    private static bool _grabActive;
    private static float _grabOriginX;

    [HarmonyPostfix]
    private static void Postfix()
    {
        var asset = PerCosmeticColors.PendingAsset;
        if (asset == null || !BridgeIds.IsBridgeAsset(asset)) return;
        if (PlayerAvatarMenu.instance == null) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            _grabActive = false;
            return;
        }

        if (SemiFunc.InputHold(InputKey.Grab))
        {
            float mouseX = Input.mousePosition.x;
            if (!_grabActive) { _grabActive = true; _grabOriginX = mouseX; }

            float deltaX = mouseX - _grabOriginX;

            // Ignore sub-pixel jitter / plain clicks; only rotate on intentional drag.
            if (Mathf.Abs(deltaX) > DragThresholdPixels)
                PlayerAvatarMenu.instance.Rotate(new Vector3(0f, -deltaX * RotateDegreesPerPixel, 0f));
        }
        else
        {
            _grabActive = false;
        }
    }
}
