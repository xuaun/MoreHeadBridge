using HarmonyLib;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla CosmeticAsset.GetIcon() does:
//   1. Try cached PNG → return
//   2. Else instantiate prefab, find SemiIconMaker, render icon → return
//   3. Else log "No IconMaker found in ..." and destroy temp gameobject → return null
//
// For bridge cosmetics there's no SemiIconMaker, so step 3 fires every single call —
// spamming the log and wasting an Instantiate+Destroy per cosmetic per UI refresh. We
// short-circuit BEFORE the instantiate by inspecting the prefab directly.
//
// Cache: we load from our PRIVATE bridge cache (see IconCapture.CacheDir). The vanilla
// cache path is wiped by REPOLib's MetaManagerPatch on every launch for any non-vanilla
// PNGs, which is exactly why we don't store ours there.
[HarmonyPatch(typeof(CosmeticAsset), nameof(CosmeticAsset.GetIcon))]
internal static class GetIconPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticAsset __instance, ref Sprite? __result)
    {
        if (__instance.icon != null) { __result = __instance.icon; return false; }
        if (__instance.prefab?.Prefab == null) return true;

        if (__instance.prefab.Prefab.GetComponentInChildren<SemiIconMaker>(true) != null)
            return true;

        // Look for the PNG in our private cache (NOT vanilla's, which REPOLib wipes).
        string cachePath = IconCapture.CachePathFor(__instance);

        if (File.Exists(cachePath))
        {
            __result = SemiFunc.LoadSpriteFromFile(cachePath);
            __instance.icon = __result;
            return false;
        }

        if (BridgeIds.IsBridgeAsset(__instance))
        {
            if (Plugin.UseTextureAsPlaceholder.Value &&
                HhhCosmeticLoader.BridgeIconTextures.TryGetValue(__instance.assetId, out var tex) && tex != null)
            {
                __result = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);
                __result.name = $"BridgeIcon_{__instance.name}";
            }
            else
            {
                __result = PlaceholderIcon.Get();
            }
            __instance.icon = __result;
        }
        else
        {
            __result = null;
        }
        return false;
    }
}
