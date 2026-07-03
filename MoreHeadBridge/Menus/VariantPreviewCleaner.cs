using UnityEngine;

namespace MoreHeadBridge;

// Clears any lingering avatar preview when the picker popup closes (e.g. the cursor was over a cell when
// it closed, so the cell's hover-exit never fired).
internal sealed class VariantPreviewCleaner : MonoBehaviour
{
    private void OnDisable() => CosmeticVariantPopup.ClearPreview();
    private void OnDestroy() => CosmeticVariantPopup.ClearPreview();
}
