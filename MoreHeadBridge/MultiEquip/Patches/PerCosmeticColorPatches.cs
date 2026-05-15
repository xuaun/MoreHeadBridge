using HarmonyLib;

namespace MoreHeadBridge;

// Per-cosmetic tintable button: track which asset is being colored.
// The color page still opens normally; we intercept CosmeticColorSet below.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "ChangeColorButton")]
internal static class PerCosmeticColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticButton __instance)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        PerCosmeticColors.PendingAsset = __instance.cosmeticAsset;
        PerCosmeticColors.PendingClearType = null;
    }
}

// Section color button: mark this as an "apply to all" operation so CosmeticColorSet
// can clear per-cosmetic overrides for that type when the user picks a color.
[HarmonyPatch(typeof(MenuElementCosmeticSection), "ChangeColorButton")]
internal static class SectionColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticSection __instance)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        PerCosmeticColors.PendingAsset = null;
        PerCosmeticColors.PendingClearType = __instance.subCategory;
    }
}

// Clear pending state when the color page closes.
[HarmonyPatch(typeof(MenuPageColor), "OnDestroy")]
internal static class ColorPageClosePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PerCosmeticColors.PendingAsset = null;
        PerCosmeticColors.PendingClearType = null;
    }
}

// Intercept CosmeticColorSet (called every time the user clicks a color):
//   Per-cosmetic button (PendingAsset != null):
//     → Save per-cosmetic color, skip vanilla so the TYPE color stays unchanged.
//     → SetupColorsLogic postfix re-applies the override on the next visual update.
//   Section button (PendingClearType != null):
//     → Vanilla runs normally (updates type color for all).
//     → Postfix clears per-cosmetic overrides for that type so section truly wins.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticColorSet))]
internal static class CosmeticColorSetPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int _index, int _colorID)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return true;

        if (PerCosmeticColors.PendingAsset != null)
        {
            // Per-cosmetic: save override, skip vanilla type-color update.
            PerCosmeticColors.Set(PerCosmeticColors.PendingAsset.assetId, _colorID);
            return false;
        }

        return true; // Section button: let vanilla update the type color.
    }

    [HarmonyPostfix]
    private static void Postfix(int _index, int _colorID)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (PerCosmeticColors.PendingAsset != null) return; // handled in prefix

        if (PerCosmeticColors.PendingClearType.HasValue && MetaManager.instance != null)
            PerCosmeticColors.ClearForType(PerCosmeticColors.PendingClearType.Value, MetaManager.instance);
    }
}

// Re-apply per-cosmetic color overrides after SetupColorsLogic runs (per-type path).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class SetupColorsLogicOverridePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => PerCosmeticColors.ApplyOverrides(__instance);
}

// Re-apply after SetupColorsAllLogic (called when every type shares the same color).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsAllLogic")]
internal static class SetupColorsAllLogicOverridePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => PerCosmeticColors.ApplyOverrides(__instance);
}
