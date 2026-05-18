using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

// Stores a per-cosmetic color override (assetId → colorIndex) on top of vanilla's
// per-type color system. Applied as a postfix override after SetupColorsLogic runs.
internal static class PerCosmeticColors
{
    private static readonly string SavePath =
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
            if (!File.Exists(SavePath)) return;
            _colors = JsonConvert.DeserializeObject<Dictionary<string, int>>(
                          File.ReadAllText(SavePath)) ?? new();
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
