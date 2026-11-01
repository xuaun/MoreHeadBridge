using HarmonyLib;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

// Resolves the icon before the button's "icon == null → load it" checks and before every redraw, so an icon cleared by IconCapture gets rebuilt.
[HarmonyPatch(typeof(MenuElementCosmeticButton))]
internal static class BridgeIconPrefillPatch
{
    [HarmonyPatch("Start"), HarmonyPrefix]
    private static void StartPrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);

    [HarmonyPatch("OnEnable"), HarmonyPrefix]
    private static void OnEnablePrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);

    [HarmonyPatch("UpdateIcon"), HarmonyPrefix]
    private static void UpdateIconPrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);
}

// Skips the throwaway prefab spawn for cosmetics without a SemiIconMaker — it can't produce an icon and would repeat on every button enable.
[HarmonyPatch(typeof(CosmeticAsset), nameof(CosmeticAsset.GenerateIconAsync))]
internal static class GenerateIconNoMakerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticAsset __instance, ref Task<Sprite> __result)
    {
        var prefab = __instance.prefab?.Prefab;
        if (prefab == null || prefab.GetComponentInChildren<SemiIconMaker>(true) != null) return true;

        __result = Task.FromResult<Sprite>(null!);
        return false;
    }
}

// The game spawns every missing icon at the same spot, and each one stays there while its GPU readback is pending — the next icon camera would film them too. Moves a cosmetic out of the way once it has been rendered.
[HarmonyPatch(typeof(SemiIconMaker), nameof(SemiIconMaker.CreateIconReadback))]
internal static class IconSpawnOverlapPatch
{
    private static readonly Vector3 RenderedSpawnPosition = new(-1000f, -6000f, -1000f);

    [HarmonyPostfix]
    private static void Postfix(SemiIconMaker __instance)
    {
        var cosmetic = __instance.GetComponentInParent<Cosmetic>(true);
        if (cosmetic != null && cosmetic.iconCreation)
            cosmetic.transform.position = RenderedSpawnPosition;
    }
}
