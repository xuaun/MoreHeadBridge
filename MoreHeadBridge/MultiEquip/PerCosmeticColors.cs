using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

// Stores a per-cosmetic color override (assetId → colorIndex) on top of vanilla's
// per-type color system. Applied as a postfix override after SetupColorsLogic runs.
//
// Data is written to:
//   BepInEx\config\MoreHeadBridge_PerCosmeticColors.json
internal static partial class PerCosmeticColors
{
    private static readonly string SavePath =
        Path.Combine(BepInEx.Paths.ConfigPath, "MoreHeadBridge_PerCosmeticColors.json");

    // Old location (before the fix/folder-paths refactor). Used only for one-time migration.
    private static readonly string LegacyPath =
        Path.Combine(Application.persistentDataPath, "MoreHeadBridge_PerCosmeticColors.json");

    internal static readonly int PropAlbedo   = Shader.PropertyToID("_AlbedoColor");
    internal static readonly int PropEmission = Shader.PropertyToID("_EmissionColor");
    internal static readonly int PropFresnel  = Shader.PropertyToID("_FresnelColor");

    private static Dictionary<string, int> _colors = new();

    internal static void Set(string assetId, int colorIndex)
    {
        _colors[assetId] = colorIndex;
        Save();
    }

    internal static void SetNoSave(string assetId, int colorIndex)
        => _colors[assetId] = colorIndex;

    internal static bool HasOverride(string assetId)
        => _colors.ContainsKey(assetId);

    internal static void SaveNow() => Save();

    internal static void ClearAll()
    {
        if (_colors.Count == 0) return;
        _colors.Clear();
        Save();
    }

    internal static IReadOnlyDictionary<string, int> GetAll() => _colors;

    internal static int GetRealTypeColor(int typeIdx, int[] colorsEquipped)
        => (_savedTypeIndex == typeIdx) ? _savedTypeColor : colorsEquipped[typeIdx];

    // Removes per-cosmetic overrides for all currently-equipped cosmetics of the given
    // type. Called when the section "apply to all" button is used.
    internal static void ClearForType(SemiFunc.CosmeticType type, MetaManager meta)
    {
        bool anyRemoved = false;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset != null && asset.type == type)
                anyRemoved |= _colors.Remove(asset.assetId);
        }
        if (anyRemoved) Save();
    }

    // Applies per-cosmetic color overrides on top of the type color already set by
    // SetupColorsLogic / SetupColorsAllLogic. Only runs for local player and menu avatar.
    internal static void ApplyOverrides(PlayerCosmetics pc)
    {
        bool hasColors  = _colors.Count > 0;
        bool hasPreview = _previewOverrides.Count > 0;
        if (!hasColors && !hasPreview) return;
        if (pc?.playerMaterials == null) return;

        var visuals = pc.playerAvatarVisuals;
        if (visuals == null) return;
        if (!visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true) return;

        foreach (var pm in pc.playerMaterials)
        {
            if (pm?.cosmetic?.cosmeticAsset == null) continue;
            string assetId = pm.cosmetic.cosmeticAsset.assetId;

            if (_presetPreviewActive)
            {
                // During a preset hover preview, _previewOverrides holds the preset's
                // stored per-cosmetic colors and takes priority over the session _colors.
                // Cosmetics not in _previewOverrides keep the type color set by vanilla.
                if (_previewOverrides.TryGetValue(assetId, out int previewColor))
                    pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, previewColor);
            }
            else if (_colors.TryGetValue(assetId, out int colorIdx))
            {
                pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, colorIdx);
            }
            else if (_previewOverrides.TryGetValue(assetId, out colorIdx))
            {
                // Non-preset-preview fallback: color-page temporary overrides
                // (set by TemporarilyShowForColorPage while the color picker is open).
                pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, colorIdx);
            }
        }
    }

    internal static void Load()
    {
        try
        {
            // One-time migration: move the file from the old persistentDataPath location
            // to the new BepInEx/config location so existing color overrides are preserved.
            if (!File.Exists(SavePath) && File.Exists(LegacyPath))
            {
                try
                {
                    File.Move(LegacyPath, SavePath);
                    Plugin.Logger.LogInfo("PerCosmeticColors: migrated save file to BepInEx/config.");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"PerCosmeticColors: migration failed, loading from old location: {ex.Message}");
                    // Fall through — try to read from legacy path below.
                }
            }

            string pathToRead = File.Exists(SavePath) ? SavePath : LegacyPath;
            if (!File.Exists(pathToRead)) return;

            _colors = JsonConvert.DeserializeObject<Dictionary<string, int>>(
                          File.ReadAllText(pathToRead)) ?? new();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"PerCosmeticColors: load failed: {ex.Message}");
            _colors = new();
        }

        LoadPresets();
    }

    private static void Save()
    {
        try { File.WriteAllText(SavePath, JsonConvert.SerializeObject(_colors)); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"PerCosmeticColors: save failed: {ex.Message}"); }
    }
}
