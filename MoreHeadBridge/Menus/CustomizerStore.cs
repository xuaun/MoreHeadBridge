// Per-cosmetic overrides (rarity/type/highlight/fixes/offsets…) for bridge cosmetics; saved to CosmeticOverrides.json, applied at registration.

using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MoreHeadBridge;

internal static class CustomizerStore
{
    private static readonly string SavePath = BridgePaths.Of("CosmeticOverrides.json");

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
                BridgeLog.Trace($"CustomizerStore: loaded {_overrides.Count} override(s)");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"CustomizerStore: load failed — {ex.Message}");
            _overrides = new();
        }
    }

    internal static bool TryGet(string assetId, out CosmeticOverrideData data)
        => _overrides.TryGetValue(assetId, out data!);

    /// Applies the override fields carried by <paramref name="incoming"/> to a live CosmeticAsset and
    /// persists them. Snapshot fields (OriginalRarity/Type/Tintable) are managed here, not by the caller.
    internal static void SetAndApply(CosmeticAsset asset, CosmeticOverrideData incoming)
    {
        bool isFirstOverride = !_overrides.TryGetValue(asset.assetId, out var data);
        if (isFirstOverride)
        {
            data = new CosmeticOverrideData();
            // Snapshot original rarity/type/tintable once (non-bridge modded) so Reset restores them.
            if (BridgeIds.IsModdedCosmetic(asset))
            {
                data.OriginalRarity   = asset.rarity;
                data.OriginalType     = asset.type;
                data.OriginalTintable = asset.tintable;
            }
        }

        data.IsModded = incoming.IsModded;
        data.Rarity = incoming.Rarity;
        data.Type = incoming.Type;
        data.FixCollider = incoming.FixCollider;
        data.FixAnimation = incoming.FixAnimation;
        data.VanillaEquipAnimationMode = incoming.VanillaEquipAnimationMode;
        data.Tintable = incoming.Tintable;
        data.EnableSway = incoming.EnableSway;
        data.CustomTypes = incoming.CustomTypes is { Count: > 0 }
            ? new List<CosmeticCustomCondition.Type>(incoming.CustomTypes)
            : null;
        data.Offsets = incoming.Offsets is { Count: > 0 }
            ? new List<CosmeticOffsetEntry>(incoming.Offsets)
            : null;
        data.Crown = incoming.Crown;
        data.ShowOnDeathHead = incoming.ShowOnDeathHead;
        data.FloorPose = incoming.FloorPose;
        data.FixCrown = incoming.FixCrown;
        data.HideConditions = incoming.HideConditions is { HasAny: true } ? incoming.HideConditions.Clone() : null;
        data.EnableCustomColors = incoming.EnableCustomColors;
        data.EnableColorAnimations = incoming.EnableColorAnimations;
        data.UseIsolatedIcon = incoming.UseIsolatedIcon;
        data.UseFitOffsets = incoming.UseFitOffsets;
        _overrides[asset.assetId] = data;

        ApplyToAsset(asset, data);
        MoreHeadCosmeticMountPatch.RefreshLiveOffsets(asset);
        MoreHeadCosmeticMountPatch.RefreshLiveSway(asset);
        Save();
        CustomizerSync.BroadcastAll();

        BridgeLog.Trace(
            $"CosmeticOverride: '{asset.assetName}' → isModded={data.IsModded?.ToString() ?? "Default"}, " +
            $"rarity={data.Rarity}, type={data.Type}, " +
            $"fixCollider={data.FixCollider?.ToString() ?? "Default"}, fixAnimation={data.FixAnimation?.ToString() ?? "Default"}, " +
            $"fixCrown={data.FixCrown?.ToString() ?? "Default"}, " +
            $"equipAnim={data.VanillaEquipAnimationMode?.ToString() ?? "Default"}, tintable={data.Tintable?.ToString() ?? "Default"}, " +
            $"sway={data.EnableSway?.ToString() ?? "Default(off)"}, " +
            $"customTypes={data.CustomTypes?.Count ?? 0}, crown={data.Crown != null}");
    }

    /// Returns the effective (fixCollider, fixAnimation) for a given assetId.
    /// Per-cosmetic override takes priority over the global config setting.
    /// Pass null assetId (e.g. for MoreHead prefabs) to use global defaults only.
    internal static (bool fixCollider, bool fixAnimation) GetEffectiveFixes(string? assetId)
    {
        bool fixCollider = Plugin.RemoveBridgePhysics.Value;
        bool fixAnimation = Plugin.LoopBridgeAnimation.Value;

        if (assetId != null && _overrides.TryGetValue(assetId, out var data))
        {
            if (data.FixCollider.HasValue) fixCollider = data.FixCollider.Value;
            if (data.FixAnimation.HasValue) fixAnimation = data.FixAnimation.Value;
        }

        return (fixCollider, fixAnimation);
    }

    internal static bool GetEffectiveFixCrown(string? assetId)
        => assetId != null
           && _overrides.TryGetValue(assetId, out var data)
           && data.FixCrown == true;

    internal static VanillaEquipAnimationMode GetEffectiveEquipAnimationMode(string? assetId)
    {
        var mode = Plugin.BridgeEquipAnimationMode.Value;

        if (assetId != null && _overrides.TryGetValue(assetId, out var data))
        {
            if (data.VanillaEquipAnimationMode.HasValue)
                mode = data.VanillaEquipAnimationMode.Value;
        }

        return mode;
    }

    /// Returns the stored sway override for a given assetId, if any.
    /// Null means no per-cosmetic override (bridge cosmetics default to no sway).
    internal static SwayMode? GetEffectiveSway(string? assetId)
    {
        if (assetId != null && _overrides.TryGetValue(assetId, out var data))
            return data.EnableSway;

        return null;
    }

    /// True when the cosmetic is explicitly hidden on the in-game death head
    /// (ShowOnDeathHead == false). Absent/null/true → shown (default).
    internal static bool IsHiddenOnDeathHead(string? assetId)
        => assetId != null
           && _overrides.TryGetValue(assetId, out var data)
           && data.ShowOnDeathHead == false;

    /// The configured death-head "floor pose" for the cosmetic, or null when none is set.
    internal static DeathHeadFloorPose? GetEffectiveFloorPose(string? assetId)
        => assetId != null && _overrides.TryGetValue(assetId, out var data) ? data.FloorPose : null;

    /// Whether the custom-RGB "C" button is allowed for this cosmetic: the per-cosmetic override
    /// when set, otherwise the global setting for its kind (bridge / modded / vanilla).
    internal static bool GetEffectiveCustomColors(CosmeticAsset? asset)
    {
        if (asset == null) return false;
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.EnableCustomColors.HasValue)
            return data.EnableCustomColors.Value;
        if (BridgeIds.IsBridgeAsset(asset))   return Plugin.EnableBridgeCustomColors.Value;
        if (BridgeIds.IsModdedCosmetic(asset)) return Plugin.EnableModdedCustomColors.Value;
        return Plugin.EnableVanillaCustomColors.Value;
    }

    /// A bridge cosmetic whose EFFECTIVE type is a mesh-switch (e.g. HeadTopMesh). These are cloned/baked
    /// onto the death head and can't carry animated colour, so animations are denied for them in one place
    /// (GetEffectiveColorAnimations) — which also hides the "A" button and the popup toggle.
    internal static bool IsBridgeMeshSwitch(string? assetId)
    {
        if (assetId == null) return false;
        var meta = MetaManager.instance;
        if (meta?.cosmeticAssets == null || meta.cosmeticTypeAssets == null) return false;
        var asset = meta.cosmeticAssets.Find(a => a != null && a.assetId == assetId);
        if (asset == null || !BridgeIds.IsBridgeAsset(asset)) return false;
        int ti = (int)asset.type;
        return ti >= 0 && ti < meta.cosmeticTypeAssets.Count
            && meta.cosmeticTypeAssets[ti] != null && meta.cosmeticTypeAssets[ti].meshSwitch;
    }

    /// Whether the animate-colour "A" button is allowed for this cosmetic: the per-cosmetic override
    /// when set, otherwise the global EnableBridgeColorAnimations setting. Animations are bridge-only,
    /// and never allowed on a bridge MESH-switch cosmetic (forces off even if an override said on).
    internal static bool GetEffectiveColorAnimations(string? assetId)
    {
        if (IsBridgeMeshSwitch(assetId)) return false;
        if (assetId != null && _overrides.TryGetValue(assetId, out var data) && data.EnableColorAnimations.HasValue)
            return data.EnableColorAnimations.Value;
        return Plugin.EnableBridgeColorAnimations.Value;
    }

    /// Convenience overload taking the asset directly.
    internal static bool GetEffectiveColorAnimations(CosmeticAsset? asset)
        => asset != null && GetEffectiveColorAnimations(asset.assetId);

    /// Whether this cosmetic's icon should be rendered in isolation: the per-cosmetic override when
    /// set, otherwise the global UseIsolatedIconRender setting.
    internal static bool GetEffectiveIsolatedIcon(string? assetId)
        => assetId != null && _overrides.TryGetValue(assetId, out var data) && data.UseIsolatedIcon.HasValue
            ? data.UseIsolatedIcon.Value
            : Plugin.UseIsolatedIconRender.Value;

    /// Whether the automatic vanilla position/scale fixes apply to this cosmetic.
    /// Per-cosmetic override wins over the global UseVanillaPositionFixes default.
    internal static bool GetEffectiveUseFitOffsets(string? assetId)
        => assetId != null && _overrides.TryGetValue(assetId, out var data) && data.UseFitOffsets.HasValue
            ? data.UseFitOffsets.Value
            : Plugin.UseVanillaPositionFixes.Value;

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
            BceConsole.LogWarning($"CustomizerStore: could not delete save file — {ex.Message}");
        }
    }

    /// Removes the override and restores the asset to its pre-override defaults.
    /// Mirrors SetAndApply: live offsets/sway are refreshed and the change is broadcast
    /// so remote clients update immediately without requiring re-equip.
    internal static void Reset(CosmeticAsset asset)
    {
        if (!_overrides.TryGetValue(asset.assetId, out var removedData)) return;
        _overrides.Remove(asset.assetId);

        if (BridgeIds.IsBridgeAsset(asset))
        {
            // Bridge: restore the .hhh-loader defaults (original type, bridge rarity).
            HhhCosmeticLoader.ReapplyDefaults(asset);
        }
        else
        {
            // Non-bridge modded: restore from the first-override snapshot (else leave unchanged).
            if (removedData.OriginalRarity.HasValue)   asset.rarity   = removedData.OriginalRarity.Value;
            if (removedData.OriginalType.HasValue)     asset.type     = removedData.OriginalType.Value;
            if (removedData.OriginalTintable.HasValue) asset.tintable = removedData.OriginalTintable.Value;
        }

        MoreHeadCosmeticMountPatch.RefreshLiveOffsets(asset);
        MoreHeadCosmeticMountPatch.RefreshLiveSway(asset);
        Save();
        CustomizerSync.BroadcastAll();
        BridgeLog.Trace($"CosmeticOverride: '{asset.assetName}' reset to defaults");
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

    /// Returns true if any bridge cosmetic has a stored per-cosmetic override (rarity, type, etc.).
    internal static bool HasAnyOverrides() => _overrides.Count > 0;

    /// Returns true if any bridge cosmetic could show a modded marker or affect sort order:
    /// either the global highlight setting is enabled, or there are per-cosmetic overrides,
    /// or at least one non-bridge modded cosmetic is registered in the session.
    internal static bool HasAnyModded()
        => Plugin.HighlightBridgeCosmetics.Value || _overrides.Count > 0 || BridgeIds.HasAnyNonBridgeModded();

    /// Returns a snapshot of all override data for multiplayer broadcast.
    internal static Dictionary<string, CosmeticOverrideData> GetAllData()
        => new(_overrides);

    /// Merges a batch of overrides into the local store.
    /// Existing entries are overwritten by incoming data; others are kept.
    /// Applies to any currently-loaded assets, saves, and broadcasts.
    internal static void ImportBatch(Dictionary<string, CosmeticOverrideData> batch)
    {
        foreach (var kvp in batch)
            _overrides[kvp.Key] = kvp.Value;

        // Apply to already-registered assets (runtime import); during Awake meta is null → LoadAll applies it.
        var meta = MetaManager.instance;
        if (meta != null)
        {
            foreach (var asset in meta.cosmeticAssets)
            {
                if (asset == null || !BridgeIds.IsBridgeAsset(asset)) continue;
                if (batch.ContainsKey(asset.assetId))
                    ApplyToAsset(asset, _overrides[asset.assetId]);
            }
        }

        Save();
        CustomizerSync.BroadcastAll();

        // Runtime import: re-mount local cosmetics and refresh the menu so the imported data shows for US too — remotes already refresh via the broadcast (RefreshRemoteCosmetics).
        if (meta != null)
        {
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        }

        BridgeLog.Trace($"CustomizerStore: imported {batch.Count} override(s)");
    }

    /// Returns the MainCosmeticCategory for a given OverrideCosmeticType without needing an asset.
    internal static MainCosmeticCategory GetMainForType(OverrideCosmeticType t)
    {
        return t switch
        {
            OverrideCosmeticType.Hat or
            OverrideCosmeticType.HeadBottom or
            OverrideCosmeticType.Ears or
            OverrideCosmeticType.Eyewear or
            OverrideCosmeticType.FaceTop or
            OverrideCosmeticType.FaceBottom or
            OverrideCosmeticType.HeadTopMesh or
            OverrideCosmeticType.HeadBottomMesh or
            OverrideCosmeticType.EyeLidRightMesh or
            OverrideCosmeticType.EyeLidLeftMesh => MainCosmeticCategory.Head,

            OverrideCosmeticType.BodyTop or
            OverrideCosmeticType.BodyBottom or
            OverrideCosmeticType.BodyTopOverlay or
            OverrideCosmeticType.BodyBottomOverlay or
            OverrideCosmeticType.BodyTopMesh or
            OverrideCosmeticType.BodyBottomMesh => MainCosmeticCategory.Body,

            OverrideCosmeticType.ArmRight or
            OverrideCosmeticType.ArmLeft or
            OverrideCosmeticType.ArmRightOverlay or
            OverrideCosmeticType.ArmLeftOverlay or
            OverrideCosmeticType.ArmRightMesh or
            OverrideCosmeticType.ArmLeftMesh => MainCosmeticCategory.Arms,

            OverrideCosmeticType.LegRight or
            OverrideCosmeticType.LegLeft or
            OverrideCosmeticType.FootRight or
            OverrideCosmeticType.FootLeft or
            OverrideCosmeticType.LegRightOverlay or
            OverrideCosmeticType.LegLeftOverlay or
            OverrideCosmeticType.LegRightMesh or
            OverrideCosmeticType.LegLeftMesh => MainCosmeticCategory.Legs,

            _ => MainCosmeticCategory.World,
        };
    }

    /// Returns whether an asset should display the orange "modded" border (bridge cosmetics only).
    /// Per-cosmetic IsModded override takes priority; without one, follows HighlightBridgeCosmetics.
    internal static bool IsModdedForAsset(CosmeticAsset? asset)
    {
        if (asset == null) return false;
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.IsModded.HasValue)
            return data.IsModded.Value && BridgeIds.IsBridgeAsset(asset);
        return BridgeIds.IsBridgeAsset(asset) && Plugin.HighlightBridgeCosmetics.Value;
    }

    /// Returns whether an asset should display the purple "non-bridge modded" border.
    /// Per-cosmetic IsModded override takes priority; without one, follows HighlightModdedCosmetics.
    internal static bool IsNonBridgeModdedForAsset(CosmeticAsset? asset)
    {
        if (asset == null || !BridgeIds.IsModdedCosmetic(asset)) return false;
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.IsModded.HasValue)
            return data.IsModded.Value;
        return Plugin.HighlightModdedCosmetics.Value;
    }

    /// Returns the effective OverrideCosmeticType for the asset's current state.
    internal static OverrideCosmeticType GetEffectiveType(CosmeticAsset asset)
    {
        if (HhhCosmeticLoader.IsWorldAsset(asset)) return OverrideCosmeticType.World;
        return VanillaToOverride.TryGetValue(asset.type, out var t) ? t : OverrideCosmeticType.Hat;
    }

    /// The cosmetic's ORIGINAL OverrideCosmeticType (no Type override): the saved OriginalType while an override is active, else asset.type. Intrinsic per assetId — lets the sync customizer tell a real Type change from a redundant "== original".
    internal static OverrideCosmeticType GetOriginalType(CosmeticAsset asset)
    {
        if (asset == null) return OverrideCosmeticType.Hat;
        if (HhhCosmeticLoader.IsWorldAsset(asset)) return OverrideCosmeticType.World;
        SemiFunc.CosmeticType vanilla =
            (_overrides.TryGetValue(asset.assetId, out var data) && data.OriginalType.HasValue)
                ? data.OriginalType.Value
                : asset.type;
        return VanillaToOverride.TryGetValue(vanilla, out var t) ? t : OverrideCosmeticType.Hat;
    }

    /// Returns the MainCosmeticCategory that contains the asset's current type.
    internal static MainCosmeticCategory GetCurrentMain(CosmeticAsset asset)
        => GetMainForType(GetEffectiveType(asset));

    /// Maps an OverrideCosmeticType to the internal vanilla type + world flag.
    internal static (SemiFunc.CosmeticType cosmeticType, bool isWorld) MapOverrideToVanilla(OverrideCosmeticType t)
    {
        var vanilla = OverrideToVanilla.TryGetValue(t, out var v) ? v : SemiFunc.CosmeticType.Hat;
        return (vanilla, t == OverrideCosmeticType.World);
    }

    // ── Remote fallbacks ─────────────────────────────────────────────────────
    // Remote wearer with no payload = "no override", but the shared asset may carry the VIEWER's own
    // Type/Tintable override — resolve pre-override values so local edits don't leak onto others.

    /// Pre-override (vanilla type, world flag) for a REMOTE instance of this asset.
    internal static (SemiFunc.CosmeticType cosmeticType, bool isWorld) GetRemoteFallbackType(CosmeticAsset asset)
    {
        if (BridgeIds.IsBridgeAsset(asset)
            && HhhCosmeticLoader.TryGetOriginalType(asset.assetId, out var original))
            return MapOverrideToVanilla(original);
        // Non-bridge modded: the first-override snapshot (present only when the viewer overrode type).
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.OriginalType.HasValue)
            return (data.OriginalType.Value, false);
        return (asset.type, HhhCosmeticLoader.IsWorldAsset(asset));
    }

    /// Pre-override tintable flag for a REMOTE instance of this asset.
    internal static bool GetRemoteFallbackTintable(CosmeticAsset asset)
    {
        if (HhhCosmeticLoader.TryGetDefaultTintable(asset, out bool defaultTintable))
            return defaultTintable; // bridge: loader-detected, gated by EnableBridgeTinting
        if (_overrides.TryGetValue(asset.assetId, out var data) && data.OriginalTintable.HasValue)
            return data.OriginalTintable.Value;
        return asset.tintable;
    }

    // ── Type mapping (single source of truth) ────────────────────────────────
    // OverrideCosmeticType → vanilla type. World shares Hat (keeps type-indexed structures in-bounds); world flag distinguishes it.
    private static readonly Dictionary<OverrideCosmeticType, SemiFunc.CosmeticType> OverrideToVanilla = new()
    {
        [OverrideCosmeticType.World] = SemiFunc.CosmeticType.Hat,
        [OverrideCosmeticType.Hat] = SemiFunc.CosmeticType.Hat,
        [OverrideCosmeticType.HeadBottom] = SemiFunc.CosmeticType.HeadBottom,
        [OverrideCosmeticType.Ears] = SemiFunc.CosmeticType.Ears,
        [OverrideCosmeticType.Eyewear] = SemiFunc.CosmeticType.Eyewear,
        [OverrideCosmeticType.FaceTop] = SemiFunc.CosmeticType.FaceTop,
        [OverrideCosmeticType.FaceBottom] = SemiFunc.CosmeticType.FaceBottom,
        [OverrideCosmeticType.HeadTopMesh] = SemiFunc.CosmeticType.HeadTopMesh,
        [OverrideCosmeticType.HeadBottomMesh] = SemiFunc.CosmeticType.HeadBottomMesh,
        [OverrideCosmeticType.EyeLidRightMesh] = SemiFunc.CosmeticType.EyeLidRightMesh,
        [OverrideCosmeticType.EyeLidLeftMesh] = SemiFunc.CosmeticType.EyeLidLeftMesh,
        [OverrideCosmeticType.BodyTop] = SemiFunc.CosmeticType.BodyTop,
        [OverrideCosmeticType.BodyBottom] = SemiFunc.CosmeticType.BodyBottom,
        [OverrideCosmeticType.BodyTopOverlay] = SemiFunc.CosmeticType.BodyTopOverlay,
        [OverrideCosmeticType.BodyBottomOverlay] = SemiFunc.CosmeticType.BodyBottomOverlay,
        [OverrideCosmeticType.BodyTopMesh] = SemiFunc.CosmeticType.BodyTopMesh,
        [OverrideCosmeticType.BodyBottomMesh] = SemiFunc.CosmeticType.BodyBottomMesh,
        [OverrideCosmeticType.ArmRight] = SemiFunc.CosmeticType.ArmRight,
        [OverrideCosmeticType.ArmLeft] = SemiFunc.CosmeticType.ArmLeft,
        [OverrideCosmeticType.ArmRightOverlay] = SemiFunc.CosmeticType.ArmRightOverlay,
        [OverrideCosmeticType.ArmLeftOverlay] = SemiFunc.CosmeticType.ArmLeftOverlay,
        [OverrideCosmeticType.ArmRightMesh] = SemiFunc.CosmeticType.ArmRightMesh,
        [OverrideCosmeticType.ArmLeftMesh] = SemiFunc.CosmeticType.ArmLeftMesh,
        [OverrideCosmeticType.LegRight] = SemiFunc.CosmeticType.LegRight,
        [OverrideCosmeticType.LegLeft] = SemiFunc.CosmeticType.LegLeft,
        [OverrideCosmeticType.FootRight] = SemiFunc.CosmeticType.FootRight,
        [OverrideCosmeticType.FootLeft] = SemiFunc.CosmeticType.FootLeft,
        [OverrideCosmeticType.LegRightOverlay] = SemiFunc.CosmeticType.LegRightOverlay,
        [OverrideCosmeticType.LegLeftOverlay] = SemiFunc.CosmeticType.LegLeftOverlay,
        [OverrideCosmeticType.LegRightMesh] = SemiFunc.CosmeticType.LegRightMesh,
        [OverrideCosmeticType.LegLeftMesh] = SemiFunc.CosmeticType.LegLeftMesh,
    };

    // Reverse of OverrideToVanilla so they never drift; World skipped (worlds detected via IsWorldAsset).
    private static readonly Dictionary<SemiFunc.CosmeticType, OverrideCosmeticType> VanillaToOverride =
        BuildVanillaToOverride();

    private static Dictionary<SemiFunc.CosmeticType, OverrideCosmeticType> BuildVanillaToOverride()
    {
        var map = new Dictionary<SemiFunc.CosmeticType, OverrideCosmeticType>();
        foreach (var kvp in OverrideToVanilla)
        {
            if (kvp.Key == OverrideCosmeticType.World) continue;
            map[kvp.Value] = kvp.Key;
        }
        return map;
    }

    // ── Internals ───────────────────────────────────────────────────────────

    internal static void ApplyToAsset(CosmeticAsset asset, CosmeticOverrideData data)
    {
        // IsModded is purely a UI/sort metadata flag — no change to the asset itself.

        if (data.Rarity != null)
            asset.rarity = data.Rarity.Value;
        else if (BridgeIds.IsBridgeAsset(asset))
            // Bridge falls back to global BridgeDefaultRarity; non-bridge modded keep their own rarity.
            asset.rarity = Plugin.BridgeDefaultRarity.Value;

        if (data.Type != null)
        {
            var (cosmeticType, isWorld) = MapOverrideToVanilla(data.Type.Value);
            asset.type = cosmeticType;

            // Keep WorldAssetIds in sync — world rendering depends on it.
            if (isWorld)
                HhhCosmeticLoader.WorldAssetIds.Add(asset.assetId);
            else
                HhhCosmeticLoader.WorldAssetIds.Remove(asset.assetId);
        }

        if (data.Tintable != null)
            asset.tintable = data.Tintable.Value;
        else if (BridgeIds.IsBridgeAsset(asset)
              && HhhCosmeticLoader.TryGetDefaultTintable(asset, out bool defaultTintable))
            // Bridge cosmetics: restore the loader-detected default when no explicit override.
            asset.tintable = defaultTintable;
        else if (data.OriginalTintable.HasValue)
            // Non-bridge modded: restore the original tintable so "Default" reverses a Yes/No override.
            asset.tintable = data.OriginalTintable.Value;

        // CustomTypes broadcast per-instance via BridgeCustomTypesBroadcaster, NOT asset.customTypeList (shared asset would leak local config to remotes).
    }

    private static void Save()
    {
        string json = JsonConvert.SerializeObject(
            new SaveData { Overrides = _overrides }, Formatting.Indented);
        _lastWrite = AtomicJson.QueueWrite(_lastWrite, SavePath, json, "CustomizerStore: save failed");
    }

    /// Blocks briefly for the in-flight background write to finish. Called on quit so the
    /// last override change is never lost if the process exits immediately after a save.
    internal static void FlushPendingWrites()
    {
        try { _lastWrite.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* best-effort flush on shutdown */ }
    }

    private sealed class SaveData
    {
        [JsonProperty("overrides")]
        public Dictionary<string, CosmeticOverrideData> Overrides { get; set; } = new();
    }
}
