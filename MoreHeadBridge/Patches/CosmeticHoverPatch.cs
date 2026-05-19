// ============================================================================
// [MenuCapture] Reactive hover capture.
//
// Patches MetaManager.CosmeticEquip — vanilla calls this with _isPreview: true
// exactly once when the user hovers a cosmetic button, spawning the cosmetic on
// the preview avatar. We piggy-back on that single event (no per-frame overhead),
// poll until the equip animation completes (Cosmetic.equipLerp >= 1f), then
// snapshot the menu's RenderTexture and save it as the icon PNG.
//
// If the user moves away before the animation finishes, the Cosmetic component
// disappears; CheckEquipAnim returns Gone, the coroutine aborts and removes the
// schedule entry so the next hover will retry.
//
// To disable: set [Icons] AutoCaptureIcons=false in the config.
// ============================================================================

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MetaManager), "CosmeticEquip")]
internal static class CosmeticHoverPatch
{
    private static readonly HashSet<string> _scheduled = new();

    private static FieldInfo? _equipLerpField;
    private static bool _equipLerpWarned;

    private enum AnimState { StillRunning, Done, Gone }

    // Searches all Cosmetic components for the target asset and checks equipLerp.
    private static AnimState CheckEquipAnim(CosmeticAsset asset)
    {
        if (_equipLerpField == null && !_equipLerpWarned)
        {
            _equipLerpField = AccessTools.Field(typeof(Cosmetic), "equipLerp");
            if (_equipLerpField == null)
            {
                _equipLerpWarned = true;
                Plugin.Logger.LogWarning("CosmeticHoverPatch: Cosmetic.equipLerp not found — will capture without waiting for animation.");
            }
        }

        // If we can't read the field, skip the wait entirely.
        if (_equipLerpField == null) return AnimState.Done;

        foreach (var cosmetic in UnityEngine.Object.FindObjectsOfType<Cosmetic>())
        {
            if (cosmetic != null && cosmetic.cosmeticAsset == asset)
                return (float)(_equipLerpField.GetValue(cosmetic) ?? 1f) >= 1f
                    ? AnimState.Done
                    : AnimState.StillRunning;
        }

        // Asset not found — cosmetic was unpreview'd (user moved away).
        return AnimState.Gone;
    }

    // Called by BatchIconGeneratorMenuClosePatch when the menu closes, so that asset IDs
    // locked in _scheduled don't block hover capture on the next menu open.
    internal static void OnMenuClosed() => _scheduled.Clear();

    // __0 = first parameter (CosmeticAsset), __1 = second parameter (bool _isPreview).
    // Vanilla calls CosmeticEquip(asset, _isPreview: true) exactly once on hover-start,
    // so this fires at precisely the right moment with no per-frame overhead.
    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance, CosmeticAsset __0, bool __1)
    {
        if (!__1) return;                            // not a preview equip — ignore
        if (!Plugin.AutoCaptureIcons.Value) return;

        var asset = __0;
        if (asset == null) return;
        if (!BridgeIds.IsBridgeAsset(asset)) return;
        if (IconCapture.HasCache(asset)) return;
        if (!_scheduled.Add(asset.assetId)) return;  // coroutine already running for this asset

        __instance.StartCoroutine(CaptureAfterAnimation(asset));
    }

    private static IEnumerator CaptureAfterAnimation(CosmeticAsset asset)
    {
        // One frame so vanilla's hover-equip can spawn the Cosmetic GO.
        yield return null;

        // Poll until the animation completes or the user moves away.
        // Timeout (3 s) guards against a cosmetic whose equipLerp never reaches 1f.
        const float Timeout = 3f;
        float elapsed = 0f;

        while (elapsed < Timeout)
        {
            var state = CheckEquipAnim(asset);

            if (state == AnimState.Done)
                break; // cosmetic is fully scaled — proceed to capture

            if (state == AnimState.Gone)
            {
                // User stopped hovering before animation finished. Remove from
                // the schedule so the next hover retries cleanly.
                _scheduled.Remove(asset.assetId);
                yield break;
            }

            // StillRunning — wait another frame.
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Snapshot at the end of this frame (ReadPixels requires WaitForEndOfFrame).
        yield return new WaitForEndOfFrame();

        bool ok = false;
        try
        {
            ok = IconCapture.TryCapture(asset);
        }
        finally
        {
            // On failure: remove so the next hover can retry.
            // On success: keep in _scheduled — HasCache() will gate the next hover anyway.
            if (!ok)
                _scheduled.Remove(asset.assetId);
        }
    }
}
