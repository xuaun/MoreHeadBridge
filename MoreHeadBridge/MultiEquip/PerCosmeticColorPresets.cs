using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

// Preset-color persistence half of PerCosmeticColors.
//
// Vanilla presets store one color per CosmeticType (colorsEquipped snapshot).
// Our per-cosmetic overrides are stored separately here, keyed by preset index.
// On preset save  → snapshot current _colors for that preset's cosmetics.
// On preset hover → populate _previewOverrides from the stored preset colors.
// On preset load  → restore stored colors into _colors after vanilla's ClearForType runs.
internal static partial class PerCosmeticColors
{
    private static readonly string PresetSavePath =
        Path.Combine(BepInEx.Paths.ConfigPath, "MoreHeadBridge_PresetColors.json");

    // presetIndex → { assetId → colorIndex }
    private static Dictionary<int, Dictionary<string, int>> _presetColors = new();

    // True while a preset hover preview is active AND we have populated _previewOverrides
    // from stored preset colors. Causes ApplyOverrides to prefer _previewOverrides over
    // the session _colors so the correct preset colors show during hover.
    private static bool _presetPreviewActive;

    internal static void SetPresetPreviewActive(bool value)
        => _presetPreviewActive = value;

    // Set by PresetHoverIndexTrackPatch at the start of a MenuElementCosmeticPreset.Update()
    // that is actively being hovered. Gives PresetPreviewColorsPatch the preset index directly
    // so it doesn't have to reverse-engineer it from cosmeticEquippedPreview.
    private static int _pendingHoverPresetIndex = -1;
    internal static void NotifyPresetHoverStart(int presetIndex) => _pendingHoverPresetIndex = presetIndex;
    internal static int GetPendingHoverPresetIndex() => _pendingHoverPresetIndex;

    /// Snapshots the current per-cosmetic overrides for the cosmetics in this preset slot.
    /// Called from CosmeticPresetSetColorsPatch when the user saves a preset.
    internal static void SavePreset(int presetIndex, IList<int> cosmeticIndices)
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        var presetColorMap = new Dictionary<string, int>();
        foreach (int idx in cosmeticIndices)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null) continue;
            if (_colors.TryGetValue(asset.assetId, out int colorIdx))
                presetColorMap[asset.assetId] = colorIdx;
        }

        if (presetColorMap.Count > 0)
            _presetColors[presetIndex] = presetColorMap;
        else
            _presetColors.Remove(presetIndex);

        SavePresets();
    }

    internal static Dictionary<string, int>? GetPresetColors(int presetIndex)
        => _presetColors.TryGetValue(presetIndex, out var d) ? d : null;

    /// Removes stored per-cosmetic colors for a preset slot (called on delete).
    internal static void DeletePresetColors(int presetIndex)
    {
        if (_presetColors.Remove(presetIndex))
            SavePresets();
    }

    internal static void LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetSavePath)) return;
            _presetColors = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, int>>>(
                File.ReadAllText(PresetSavePath)) ?? new();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"PerCosmeticColors: preset colors load failed: {ex.Message}");
            _presetColors = new();
        }
    }

    private static void SavePresets()
    {
        try { File.WriteAllText(PresetSavePath, JsonConvert.SerializeObject(_presetColors)); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"PerCosmeticColors: preset colors save failed: {ex.Message}"); }
    }
}
