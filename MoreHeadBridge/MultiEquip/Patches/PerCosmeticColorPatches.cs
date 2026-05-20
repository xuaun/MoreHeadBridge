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
        if (__instance.cosmeticAsset != null)
            PerCosmeticColors.TemporarilyShowForColorPage(__instance.cosmeticAsset);
    }
}

// Section color button: clear PendingAsset so CosmeticColorSet knows this is a
// type-wide paint (not per-cosmetic), and its postfix will wipe overrides for that type.
[HarmonyPatch(typeof(MenuElementCosmeticSection), "ChangeColorButton")]
internal static class SectionColorButtonPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticSection __instance)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        PerCosmeticColors.PendingAsset = null;
    }
}

// Restore the temporarily-overwritten colorsEquipped[type] 
[HarmonyPatch(typeof(MenuPageColor), "OnDestroy")]
internal static class ColorPageClosePatch
{
    [HarmonyPrefix]
    private static void Prefix()
        => PerCosmeticColors.RestoreTypeColor();

    [HarmonyPostfix]
    private static void Postfix()
        => PerCosmeticColors.PendingAsset = null;
}

// Intercept CosmeticColorSet (called every time the user clicks a color):
//   Per-cosmetic button (PendingAsset != null):
//     → Save per-cosmetic color, skip vanilla so the TYPE color stays unchanged.
//     → SetupColorsLogic postfix re-applies the override on the next visual update.
//   Section / any other path (PendingAsset == null):
//     → Vanilla runs normally (updates type color for all cosmetics of that type).
//     → Postfix clears per-cosmetic overrides for that type so the type color truly wins.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticColorSet))]
internal static class CosmeticColorSetPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int _index, int _colorID)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return true;

        if (PerCosmeticColors.PendingAsset != null)
        {
            var asset = PerCosmeticColors.PendingAsset;

            if (MetaManager.instance != null
                && _index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
            {
                int realTypeColor = PerCosmeticColors.GetRealTypeColor(
                    _index, MetaManager.instance.colorsEquipped);
                if (realTypeColor >= 0)
                {
                    foreach (int idx in MetaManager.instance.cosmeticEquipped)
                    {
                        if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;
                        var other = MetaManager.instance.cosmeticAssets[idx];
                        if (other == null || other.type != asset.type) continue;
                        if (other.assetId == asset.assetId) continue; // the one being painted
                        if (PerCosmeticColors.HasOverride(other.assetId)) continue; // already pinned
                        PerCosmeticColors.SetNoSave(other.assetId, realTypeColor);
                    }
                }
            }

            // Save the target asset's new color (also saves the siblings pinned above).
            PerCosmeticColors.Set(asset.assetId, _colorID);

            // Keep colorsEquipped[type] in sync so the color picker UI shows the right
            // selected button. SetupColorsLogic will apply this to all cosmetics, then
            // ApplyOverrides re-applies each individual override on top.
            if (MetaManager.instance != null
                && _index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
                MetaManager.instance.colorsEquipped[_index] = _colorID;
            return false;
        }

        return true; // Section button: let vanilla update the type color.
    }

    [HarmonyPostfix]
    private static void Postfix(int _index, int _colorID)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (PerCosmeticColors.PendingAsset != null) return; // per-cosmetic paint: no clear
        if (MetaManager.instance == null) return;

        // Always clear per-cosmetic overrides for the type being painted.
        if (_index >= 0 && _index < MetaManager.instance.colorsEquipped.Length)
            PerCosmeticColors.ClearForType((SemiFunc.CosmeticType)_index, MetaManager.instance);
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
