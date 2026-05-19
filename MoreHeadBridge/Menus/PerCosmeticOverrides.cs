// ============================================================================
// Per-cosmetic rarity, type, and modded-highlight overrides for bridge cosmetics.
// Saved to BepInEx/config/MoreHeadBridge_CosmeticOverrides.json.
//
// Applied at registration time (HhhCosmeticLoader) so every launch picks up
// the overrides without needing to patch MetaManager.
//
// Updated in-game via CosmeticOverridePopup (Shift+click on a bridge
// cosmetic; requires EnableCosmeticOverrideUI = true).
// ============================================================================

using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MoreHeadBridge;

/// User-visible cosmetic sub-category options for per-cosmetic overrides.
internal enum OverrideCosmeticType
{
    // HEAD
    Hat,
    HeadBottom,
    Ears,
    Eyewear,
    FaceTop,
    FaceBottom,
    // BODY
    BodyTop,
    BodyBottom,
    BodyTopOverlay,
    BodyBottomOverlay,
    // ARMS
    ArmRight,
    ArmLeft,
    ArmRightOverlay,
    ArmLeftOverlay,
    // LEGS
    LegRight,
    LegLeft,
    FootRight,
    FootLeft,
    LegRightOverlay,
    LegLeftOverlay,
    // WORLD
    World,
}

/// Top-level cosmetic category groups used in the override popup.
internal enum MainCosmeticCategory { Head, Body, Arms, Legs, World }

internal static class PerCosmeticOverrides
{
    private static readonly string SavePath = Path.Combine(
        Paths.ConfigPath, "MoreHeadBridge_CosmeticOverrides.json");

    private static Dictionary<string, CosmeticOverrideData> _overrides = new();
    private static Task _lastWrite = Task.CompletedTask;

    // ── Public API ─────────────────────────────────────────────────────────

    internal static void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) { _overrides = new(); return; }
            string json = File.ReadAllText(SavePath);
            var data = JsonConvert.DeserializeObject<SaveData>(json);
            _overrides = data?.Overrides ?? new();
            if (_overrides.Count > 0)
                Plugin.Logger.LogDebug($"PerCosmeticOverrides: loaded {_overrides.Count} override(s).");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"PerCosmeticOverrides: load failed — {ex.Message}");
            _overrides = new();
        }
    }

    internal static bool TryGet(string assetId, out CosmeticOverrideData data)
        => _overrides.TryGetValue(assetId, out data!);

    /// Applies rarity/type/isModded overrides to a live CosmeticAsset and persists them.
    internal static void SetAndApply(CosmeticAsset asset,
                                     bool?                isModded,
                                     SemiFunc.Rarity      pendingRarity,
                                     OverrideCosmeticType pendingType)
    {
        if (!_overrides.TryGetValue(asset.assetId, out var data))
            data = new CosmeticOverrideData();

        data.IsModded = isModded;
        data.Rarity   = pendingRarity;
        data.Type     = pendingType;
        _overrides[asset.assetId] = data;

        ApplyToAsset(asset, data);
        Save();

        Plugin.Logger.LogDebug(
            $"CosmeticOverride: '{asset.assetName}' → isModded={isModded?.ToString() ?? "Default"}, " +
            $"rarity={pendingRarity}, type={pendingType}");
    }

    /// Clears ALL per-cosmetic overrides and deletes the save file.
    /// Called when ResetCosmeticCustomizer config flag is set.
    internal static void ResetAll()
    {
        _overrides.Clear();
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"PerCosmeticOverrides: could not delete save file — {ex.Message}");
        }
    }

    /// Removes the override and restores the asset to loader defaults.
    internal static void Reset(CosmeticAsset asset)
    {
        if (!_overrides.Remove(asset.assetId)) return;
        HhhCosmeticLoader.ReapplyDefaults(asset);
        Save();
        Plugin.Logger.LogDebug($"CosmeticOverride: '{asset.assetName}' reset to defaults.");
    }

    /// Applies the stored override (if any) to a CosmeticAsset at registration time.
    /// Called by HhhCosmeticLoader.TryRegister after setting default rarity/type.
    internal static void ApplyIfPresent(CosmeticAsset asset)
    {
        if (_overrides.TryGetValue(asset.assetId, out var data))
            ApplyToAsset(asset, data);
    }

    /// Returns true if this asset has any stored override.
    internal static bool HasOverride(CosmeticAsset asset)
        => _overrides.ContainsKey(asset.assetId);

    /// Returns whether a bridge cosmetic should be treated as "modded" for border/sort purposes.
    /// Per-cosmetic IsModded override takes priority; falls back to the global setting.
    /// Always returns false for non-bridge assets.
    internal static bool IsModdedForAsset(CosmeticAsset? asset)
    {
        if (asset == null || !BridgeIds.IsBridgeAsset(asset)) return false;
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.IsModded.HasValue)
            return data.IsModded.Value;
        return Plugin.HighlightModdedCosmetics.Value;
    }

    /// Returns the effective OverrideCosmeticType for the asset's current state.
    internal static OverrideCosmeticType GetCurrentType(CosmeticAsset asset)
    {
        if (HhhCosmeticLoader.IsWorldAsset(asset)) return OverrideCosmeticType.World;
        return asset.type switch
        {
            SemiFunc.CosmeticType.HeadBottom        => OverrideCosmeticType.HeadBottom,
            SemiFunc.CosmeticType.Ears              => OverrideCosmeticType.Ears,
            SemiFunc.CosmeticType.Eyewear           => OverrideCosmeticType.Eyewear,
            SemiFunc.CosmeticType.FaceTop           => OverrideCosmeticType.FaceTop,
            SemiFunc.CosmeticType.FaceBottom        => OverrideCosmeticType.FaceBottom,
            SemiFunc.CosmeticType.BodyTop           => OverrideCosmeticType.BodyTop,
            SemiFunc.CosmeticType.BodyBottom        => OverrideCosmeticType.BodyBottom,
            SemiFunc.CosmeticType.BodyTopOverlay    => OverrideCosmeticType.BodyTopOverlay,
            SemiFunc.CosmeticType.BodyBottomOverlay => OverrideCosmeticType.BodyBottomOverlay,
            SemiFunc.CosmeticType.ArmRight          => OverrideCosmeticType.ArmRight,
            SemiFunc.CosmeticType.ArmLeft           => OverrideCosmeticType.ArmLeft,
            SemiFunc.CosmeticType.ArmRightOverlay   => OverrideCosmeticType.ArmRightOverlay,
            SemiFunc.CosmeticType.ArmLeftOverlay    => OverrideCosmeticType.ArmLeftOverlay,
            SemiFunc.CosmeticType.LegRight          => OverrideCosmeticType.LegRight,
            SemiFunc.CosmeticType.LegLeft           => OverrideCosmeticType.LegLeft,
            SemiFunc.CosmeticType.FootRight         => OverrideCosmeticType.FootRight,
            SemiFunc.CosmeticType.FootLeft          => OverrideCosmeticType.FootLeft,
            SemiFunc.CosmeticType.LegRightOverlay   => OverrideCosmeticType.LegRightOverlay,
            SemiFunc.CosmeticType.LegLeftOverlay    => OverrideCosmeticType.LegLeftOverlay,
            _                                       => OverrideCosmeticType.Hat,
        };
    }

    /// Returns the MainCosmeticCategory that contains the asset's current type.
    internal static MainCosmeticCategory GetCurrentMain(CosmeticAsset asset)
    {
        return GetCurrentType(asset) switch
        {
            OverrideCosmeticType.Hat or
            OverrideCosmeticType.HeadBottom or
            OverrideCosmeticType.Ears or
            OverrideCosmeticType.Eyewear or
            OverrideCosmeticType.FaceTop or
            OverrideCosmeticType.FaceBottom     => MainCosmeticCategory.Head,

            OverrideCosmeticType.BodyTop or
            OverrideCosmeticType.BodyBottom or
            OverrideCosmeticType.BodyTopOverlay or
            OverrideCosmeticType.BodyBottomOverlay => MainCosmeticCategory.Body,

            OverrideCosmeticType.ArmRight or
            OverrideCosmeticType.ArmLeft or
            OverrideCosmeticType.ArmRightOverlay or
            OverrideCosmeticType.ArmLeftOverlay => MainCosmeticCategory.Arms,

            OverrideCosmeticType.LegRight or
            OverrideCosmeticType.LegLeft or
            OverrideCosmeticType.FootRight or
            OverrideCosmeticType.FootLeft or
            OverrideCosmeticType.LegRightOverlay or
            OverrideCosmeticType.LegLeftOverlay => MainCosmeticCategory.Legs,

            _                                   => MainCosmeticCategory.World,
        };
    }

    /// Maps an OverrideCosmeticType to the internal vanilla type + world flag.
    internal static (SemiFunc.CosmeticType cosmeticType, bool isWorld) ResolveType(OverrideCosmeticType t)
        => t switch
        {
            OverrideCosmeticType.World             => (SemiFunc.CosmeticType.Hat,              true),
            OverrideCosmeticType.Hat               => (SemiFunc.CosmeticType.Hat,              false),
            OverrideCosmeticType.HeadBottom        => (SemiFunc.CosmeticType.HeadBottom,       false),
            OverrideCosmeticType.Ears              => (SemiFunc.CosmeticType.Ears,             false),
            OverrideCosmeticType.Eyewear           => (SemiFunc.CosmeticType.Eyewear,          false),
            OverrideCosmeticType.FaceTop           => (SemiFunc.CosmeticType.FaceTop,          false),
            OverrideCosmeticType.FaceBottom        => (SemiFunc.CosmeticType.FaceBottom,       false),
            OverrideCosmeticType.BodyTop           => (SemiFunc.CosmeticType.BodyTop,          false),
            OverrideCosmeticType.BodyBottom        => (SemiFunc.CosmeticType.BodyBottom,       false),
            OverrideCosmeticType.BodyTopOverlay    => (SemiFunc.CosmeticType.BodyTopOverlay,   false),
            OverrideCosmeticType.BodyBottomOverlay => (SemiFunc.CosmeticType.BodyBottomOverlay,false),
            OverrideCosmeticType.ArmRight          => (SemiFunc.CosmeticType.ArmRight,         false),
            OverrideCosmeticType.ArmLeft           => (SemiFunc.CosmeticType.ArmLeft,          false),
            OverrideCosmeticType.ArmRightOverlay   => (SemiFunc.CosmeticType.ArmRightOverlay,  false),
            OverrideCosmeticType.ArmLeftOverlay    => (SemiFunc.CosmeticType.ArmLeftOverlay,   false),
            OverrideCosmeticType.LegRight          => (SemiFunc.CosmeticType.LegRight,         false),
            OverrideCosmeticType.LegLeft           => (SemiFunc.CosmeticType.LegLeft,          false),
            OverrideCosmeticType.FootRight         => (SemiFunc.CosmeticType.FootRight,        false),
            OverrideCosmeticType.FootLeft          => (SemiFunc.CosmeticType.FootLeft,         false),
            OverrideCosmeticType.LegRightOverlay   => (SemiFunc.CosmeticType.LegRightOverlay,  false),
            OverrideCosmeticType.LegLeftOverlay    => (SemiFunc.CosmeticType.LegLeftOverlay,   false),
            _                                      => (SemiFunc.CosmeticType.Hat,              false),
        };

    // ── Internals ───────────────────────────────────────────────────────────

    internal static void ApplyToAsset(CosmeticAsset asset, CosmeticOverrideData data)
    {
        // IsModded is purely a UI/sort metadata flag — no change to the asset itself.

        if (data.Rarity != null)
            asset.rarity = data.Rarity.Value;

        if (data.Type != null)
        {
            var (cosmeticType, isWorld) = ResolveType(data.Type.Value);
            asset.type = cosmeticType;

            // Keep WorldAssetIds in sync — world rendering depends on it.
            if (isWorld)
                HhhCosmeticLoader.WorldAssetIds.Add(asset.assetId);
            else
                HhhCosmeticLoader.WorldAssetIds.Remove(asset.assetId);
        }
    }

    private static void Save()
    {
        string json = JsonConvert.SerializeObject(
            new SaveData { Overrides = _overrides }, Formatting.Indented);
        _lastWrite = _lastWrite.ContinueWith(_ =>
        {
            try
            {
                string tmpPath = SavePath + ".tmp";
                File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
                if (File.Exists(SavePath))
                    File.Replace(tmpPath, SavePath, null);
                else
                    File.Move(tmpPath, SavePath);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"PerCosmeticOverrides: save failed — {ex.Message}");
            }
        }, TaskScheduler.Default);
    }

    private sealed class SaveData
    {
        [JsonProperty("overrides")]
        public Dictionary<string, CosmeticOverrideData> Overrides { get; set; } = new();
    }
}

internal sealed class CosmeticOverrideData
{
    /// null  = use global HighlightModdedCosmetics setting.
    /// true  = always treat as modded (orange border, sort first).
    /// false = never treat as modded (vanilla appearance and sort position).
    [JsonProperty("isModded")]
    public bool? IsModded { get; set; }

    [JsonProperty("rarity")]
    [JsonConverter(typeof(StringEnumConverter))]
    public SemiFunc.Rarity? Rarity { get; set; }

    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public OverrideCosmeticType? Type { get; set; }
}
