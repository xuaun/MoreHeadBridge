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
internal static class PerCosmeticColors
{
    private static readonly string SavePath =
        Path.Combine(BepInEx.Paths.ConfigPath, "MoreHeadBridge_PerCosmeticColors.json");

    // Old location (before the fix/folder-paths refactor). Used only for one-time migration.
    private static readonly string LegacyPath =
        Path.Combine(Application.persistentDataPath, "MoreHeadBridge_PerCosmeticColors.json");

    private static Dictionary<string, int> _colors = new();

    internal static CosmeticAsset? PendingAsset { get; set; }

    internal static SemiFunc.CosmeticType? PendingClearType { get; set; }

    internal static void Set(string assetId, int colorIndex)
    {
        _colors[assetId] = colorIndex;
        Save();
    }

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
    // SetupColorsLogic / SetupColorsAllLogic.
    internal static void ApplyOverrides(PlayerCosmetics pc)
    {
        if (_colors.Count == 0) return;
        if (pc?.playerMaterials == null) return;

        int albedo   = Shader.PropertyToID("_AlbedoColor");
        int emission = Shader.PropertyToID("_EmissionColor");
        int fresnel  = Shader.PropertyToID("_FresnelColor");

        foreach (var pm in pc.playerMaterials)
        {
            if (pm?.cosmetic?.cosmeticAsset == null) continue;
            if (!_colors.TryGetValue(pm.cosmetic.cosmeticAsset.assetId, out int colorIdx)) continue;
            pm.ColorSet(albedo, emission, fresnel, colorIdx);
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
    }

    private static void Save()
    {
        try { File.WriteAllText(SavePath, JsonConvert.SerializeObject(_colors)); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"PerCosmeticColors: save failed: {ex.Message}"); }
    }
}
