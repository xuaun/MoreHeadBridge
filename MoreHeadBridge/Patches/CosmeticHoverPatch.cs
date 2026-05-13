// ============================================================================
// [MenuCapture] Reactive hover capture.
//
// Whenever the user hovers a bridge cosmetic button in the customization menu,
// the vanilla code spawns the cosmetic on the preview avatar (as a "preview"
// equip). We piggy-back on that — wait a couple of frames for the avatar to
// render, then snapshot the menu's existing RenderTexture and save it as the
// icon PNG. Icons fill in gradually as the player browses.
//
// To disable: set [Icons] AutoCaptureIcons=false in the config.
// ============================================================================

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticButton), "Update")]
internal static class CosmeticHoverPatch
{
    private static readonly HashSet<string> _scheduled = new();

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticButton __instance)
    {
        if (!Plugin.AutoCaptureIcons.Value) return;

        var asset = __instance.cosmeticAsset;
        if (asset == null) return;
        if (!BridgeIds.IsBridgeAsset(asset)) return;

        if (!__instance.wasHovering) return;

        if (IconCapture.HasCache(asset)) return;
        if (!_scheduled.Add(asset.assetId)) return;

        __instance.StartCoroutine(CaptureAfterDelay(asset));
    }

    private static IEnumerator CaptureAfterDelay(CosmeticAsset asset)
    {
        yield return null;
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();

        bool ok = IconCapture.TryCapture(asset);
        if (!ok)
        {
            _scheduled.Remove(asset.assetId);
        }
    }
}
