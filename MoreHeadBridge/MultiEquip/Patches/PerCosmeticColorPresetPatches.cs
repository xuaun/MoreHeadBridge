using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// When a preset is saved (or deleted), also save/remove the per-cosmetic color
// overrides that belong to it so they can be restored on load.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPresetSet))]
internal static class CosmeticPresetSetColorsPatch
{
    [HarmonyPostfix]
    private static void Postfix(int _index, List<int> _cosmeticEquipped)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;

        if (_cosmeticEquipped.Count == 0)
            PerCosmeticColors.DeletePresetColors(_index);
        else
            PerCosmeticColors.SavePreset(_index, _cosmeticEquipped);
    }
}

// Track which preset button is actively hovered so PresetPreviewColorsPatch can
// resolve the preset index directly instead of scanning cosmeticEquippedPreview.
[HarmonyPatch(typeof(MenuElementCosmeticPreset), "Update")]
internal static class PresetHoverIndexTrackPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticPreset __instance)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (__instance.menuButton != null && __instance.menuButton.hovering)
            PerCosmeticColors.NotifyPresetHoverStart(__instance.presetIndex);
    }
}

// When a preset hover-preview starts (CosmeticPreviewSet(_state: true)), identify
// which preset is being previewed and populate _previewOverrides from the stored
// preset colors so ApplyOverrides shows the correct individual colors.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPreviewSet))]
internal static class PresetPreviewColorsPatch
{
    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance, bool _state)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (!_state) return; // CosmeticPreviewSetClearPatch handles _state=false

        int presetIndex = FindMatchingPreset(__instance);
        if (presetIndex < 0) return;

        var presetColors = PerCosmeticColors.GetPresetColors(presetIndex);
        if (presetColors == null || presetColors.Count == 0)
        {
            Plugin.Logger.LogDebug($"Preset hover: preset {presetIndex} matched but has no per-cosmetic colors saved.");
            return;
        }

        foreach (var kv in presetColors)
            PerCosmeticColors.SetPreview(kv.Key, kv.Value);

        // Signal ApplyOverrides to prefer _previewOverrides over session _colors.
        PerCosmeticColors.SetPresetPreviewActive(true);
    }

    private static int FindMatchingPreset(MetaManager meta)
    {
        var preview = meta.cosmeticEquippedPreview;
        if (preview.Count == 0) return -1;

        var previewSet = new HashSet<int>(preview);

        int hint = PerCosmeticColors.GetPendingHoverPresetIndex();
        if (hint >= 0 && hint < meta.cosmeticPresets.Count)
        {
            var hintPreset = meta.cosmeticPresets[hint];
            if (hintPreset.Count == previewSet.Count)
            {
                bool hintMatch = true;
                foreach (int idx in hintPreset)
                    if (!previewSet.Contains(idx)) { hintMatch = false; break; }
                if (hintMatch) return hint;
            }
        }

        // Fallback: scan all presets (covers edge cases where the hint is absent or stale).
        for (int i = 0; i < meta.cosmeticPresets.Count; i++)
        {
            if (i == hint) continue; // already checked above
            var preset = meta.cosmeticPresets[i];
            if (preset.Count == 0 || preset.Count != previewSet.Count) continue;

            bool match = true;
            foreach (int idx in preset)
                if (!previewSet.Contains(idx)) { match = false; break; }
            if (match) return i;
        }

        Plugin.Logger.LogDebug($"Preset hover: no preset matched preview [{string.Join(", ", preview)}] (hint={hint}).");
        return -1;
    }
}

// When the user clicks a preset to load it, vanilla calls CosmeticColorSet for each
// type, which triggers our CosmeticColorSetPatch → ClearForType, wiping the
// per-cosmetic overrides we need. After TogglePreset finishes, restore the preset's
// stored per-cosmetic colors and re-apply them visually.
//
// A prefix captures whether this toggle is a LOAD (preset was non-empty) or a SAVE
// (slot was empty) so the postfix can skip the no-op save path.
[HarmonyPatch(typeof(MenuElementCosmeticPreset), nameof(MenuElementCosmeticPreset.TogglePreset))]
internal static class PresetLoadColorsPatch
{
    private static bool _wasLoad;

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticPreset __instance)
    {
        var meta = MetaManager.instance;
        if (meta == null) { _wasLoad = false; return; }

        // A non-empty slot means this click is a LOAD (or no-op if already equipped).
        // An empty slot means this click will SAVE the current outfit — handled by
        // CosmeticPresetSetColorsPatch, nothing to do here.
        _wasLoad = meta.cosmeticPresets[__instance.presetIndex].Count > 0
                || meta.colorPresets[__instance.presetIndex].Count > 0;
    }

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticPreset __instance)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return;
        if (!_wasLoad) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        var presetColors = PerCosmeticColors.GetPresetColors(__instance.presetIndex);
        if (presetColors == null || presetColors.Count == 0) return;

        // Identify the CosmeticTypes covered by this preset.
        var coveredTypes = new HashSet<SemiFunc.CosmeticType>();
        foreach (int idx in meta.cosmeticPresets[__instance.presetIndex])
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset != null) coveredTypes.Add(asset.type);
        }

        // ClearForType removes overrides for currently-equipped cosmetics of each type.
        // This wipes any stale session overrides from the previous outfit before we
        // restore the preset's own colors.
        foreach (var type in coveredTypes)
            PerCosmeticColors.ClearForType(type, meta);

        // Restore the per-cosmetic colors that were saved with this preset.
        foreach (var kv in presetColors)
            PerCosmeticColors.SetNoSave(kv.Key, kv.Value);
        PerCosmeticColors.SaveNow();

        // Re-trigger the local player update so ApplyOverrides runs again with the
        // now-correct _colors. Vanilla already called this at the end of TogglePreset,
        // but _colors was empty for the preset's types at that point.
        meta.CosmeticPlayerUpdateLocal(_synced: false);
    }
}
