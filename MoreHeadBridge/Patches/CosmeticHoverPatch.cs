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
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticButton), "Update")]
internal static class CosmeticHoverPatch
{
    private static readonly HashSet<string> _scheduled = new();

    private static FieldInfo? _wasHoveringField;
    private static bool _fieldLookupDone;

    private static bool GetWasHovering(MenuElementCosmeticButton btn)
    {
        if (!_fieldLookupDone)
        {
            _fieldLookupDone = true;
            _wasHoveringField = AccessTools.Field(typeof(MenuElementCosmeticButton), "wasHovering");
            if (_wasHoveringField == null)
                Plugin.Logger.LogWarning("CosmeticHoverPatch: MenuElementCosmeticButton.wasHovering not found — icon hover capture disabled. Update MoreHeadBridge.");
        }
        return _wasHoveringField != null && (bool)(_wasHoveringField.GetValue(btn) ?? false);
    }

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticButton __instance, ref bool __state)
        => __state = GetWasHovering(__instance);

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticButton __instance, bool __state)
    {
        if (__state) return;
        if (!GetWasHovering(__instance)) return;

        if (!Plugin.AutoCaptureIcons.Value) return;

        var asset = __instance.cosmeticAsset;
        if (asset == null) return;
        if (!BridgeIds.IsBridgeAsset(asset)) return;
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
            _scheduled.Remove(asset.assetId);
    }
}
