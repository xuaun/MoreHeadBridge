using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Vanilla GetIcon(): cached PNG → else instantiate prefab + find SemiIconMaker → else log "No IconMaker found" and return null. Bridge cosmetics have no SemiIconMaker, so that failure path fires on EVERY call (log spam + a wasted Instantiate/Destroy per UI refresh) — short-circuit before the instantiate. Loads from our PRIVATE cache (IconCapture.CacheDir) — REPOLib wipes the vanilla cache path of non-vanilla PNGs on every launch.
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

        // Look for the PNG in our private cache (NOT vanilla's, which REPOLib wipes). HasCache uses an in-memory set — no disk I/O on this hot path.
        if (IconCapture.HasCache(__instance))
        {
            __result = SemiFunc.LoadSpriteFromFile(IconCapture.CachePathFor(__instance));
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
