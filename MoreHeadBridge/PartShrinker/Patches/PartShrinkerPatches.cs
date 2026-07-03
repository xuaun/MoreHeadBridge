using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// ── PartShrinkerSpawnPatch ────────────────────────────────────────────────────
// Fires after InstantiateCosmetic — triggers part-hiding for newly spawned bridge cosmetics and ALWAYS disables PartShrinker.Update() to prevent NRE walk-ups.
[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static class PartShrinkerSpawnPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null) return;
        if (__instance == null) return;
        if (!BridgeIds.IsBridgeAsset(_cosmeticAsset)) return;

        // playerAvatarVisuals may be null (death-head PlayerCosmetics) — OnSpawn handles that.
        PartShrinkerBridge.OnSpawn(__result, __instance.playerAvatarVisuals, __instance);
    }
}

// ── PartShrinkerRemovePatch ───────────────────────────────────────────────────
// Fires before Cosmetic.Remove — restores part visibility before the cosmetic is destroyed so the HiddenParts list stays consistent.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Remove))]
internal static class PartShrinkerRemovePatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        if (__instance == null) return;
        if (!BridgeIds.IsBridgeAsset(__instance.cosmeticAsset)) return;
        if (__instance.playerCosmetics == null) return;

        PartShrinkerBridge.OnRemove(
            __instance.gameObject,
            __instance.playerCosmetics.playerAvatarVisuals,
            __instance.playerCosmetics);
    }
}

// ── MeshSwitchRemoveResyncPatch ───────────────────────────────────────────────
// When a mesh-SWITCH cosmetic is removed, re-assert part-hiding so the tracker drops the now-gone swap renderers. The vanilla base mesh reappears as a GameObject but its renderer stays disabled by HiddenParts, so a still-hidden part stays hidden. Self-gates to meshSwitch; no-op without a shrinker.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Remove))]
internal static class MeshSwitchRemoveResyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(Cosmetic __instance)
    {
        if (__instance == null || __instance.cosmeticTypeAsset == null
            || !__instance.cosmeticTypeAsset.meshSwitch) return;
        if (__instance.playerCosmetics == null) return;
        PartShrinkerBridge.EnforceSwapHiding(__instance.playerCosmetics.playerAvatarVisuals);
    }
}
