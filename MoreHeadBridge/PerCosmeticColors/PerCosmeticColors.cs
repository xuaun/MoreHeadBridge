using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

// Save/load/apply for per-cosmetic colour overrides (palette index, per-slot, custom RGB, animations — all in BepInEx\config\MoreHeadBridge\PerCosmeticColors.json). Preview half → PerCosmeticColors.Preview.cs, presets → PerCosmeticColors.Presets.cs.
internal static partial class PerCosmeticColors
{
    private static readonly string SavePath = BridgePaths.Of("PerCosmeticColors.json");

    // Oldest location (pre-config refactor). BridgePaths handles the config-root → MoreHeadBridge/ move generically; this only rescues files still in persistentDataPath.
    private static readonly string LegacyPath =
        Path.Combine(Application.persistentDataPath, "MoreHeadBridge_PerCosmeticColors.json");

    internal static readonly int PropAlbedo   = Shader.PropertyToID("_AlbedoColor");
    internal static readonly int PropEmission = Shader.PropertyToID("_EmissionColor");
    internal static readonly int PropFresnel  = Shader.PropertyToID("_FresnelColor");

    // Sentinel stored in _colors meaning "restore the cosmetic's original material colour" rather than an index into MetaManager.instance.colors.
    internal const int OriginalColorSentinel = -1;

    // Master switch for the whole per-cosmetic colour system (palette overrides, customs, animations, sync).
    // Independent of EnableBridgeTinting, which only gates BRIDGE tinting (tintable flags + BTM injection).
    internal static bool FeatureEnabled => Plugin.EnablePerCosmeticColors.Value;

    private static Dictionary<string, int> _colors = new();

    internal static void Set(string assetId, int colorIndex)
    {
        _colors[assetId] = colorIndex;
        Save();
        if (RemoveCustomColorNoSave(assetId)) SaveCustom();   // a palette pick clears any custom RGB
    }

    internal static void SetNoSave(string assetId, int colorIndex)
        => _colors[assetId] = colorIndex;

    internal static bool HasOverride(string assetId)
        => _colors.ContainsKey(assetId);

    /// Removes the whole-asset colour override without saving to disk.
    /// For batched section operations that call Save() once at the end.
    internal static bool RemoveColorNoSave(string assetId)
        => _colors.Remove(assetId);

    // Removes the per-cosmetic color for a single asset; called on unequip so re-equipping later starts fresh with the current type color.
    internal static void ClearForAsset(string assetId)
    {
        bool colorsChanged = _colors.Remove(assetId);
        bool slotsChanged  = RemoveSlotsNoSave(assetId);
        bool animChanged   = RemoveAnimationNoSave(assetId);
        bool customChanged = RemoveCustomColorNoSave(assetId);
        bool customSlotsChanged = RemoveCustomSlotsNoSave(assetId);
        bool slotAnimChanged = RemoveSlotAnimationsNoSave(assetId);
        if (colorsChanged) Save();
        if (slotsChanged)  SaveSlots();
        if (animChanged)   SaveAnimations();
        if (customChanged) SaveCustom();
        if (customSlotsChanged) SaveCustomSlots();
        if (slotAnimChanged) SaveSlotAnimations();
    }

    internal static void ClearAll()
    {
        bool any = _colors.Count > 0 || _slotColors.Count > 0 || _animations.Count > 0
                   || _customColors.Count > 0 || _customSlotColors.Count > 0 || _slotAnimations.Count > 0;
        _colors.Clear();
        ClearAllSlotsNoSave();
        ClearAllAnimationsNoSave();   // clears both whole-asset and per-slot animations
        _customColors.Clear();
        _customSlotColors.Clear();
        if (any) { Save(); SaveSlots(); SaveAnimations(); SaveSlotAnimations(); SaveCustom(); SaveCustomSlots(); }
    }

    internal static IReadOnlyDictionary<string, int> GetAll() => _colors;

    /// Stores the OriginalColorSentinel for the given assetId, clears any per-slot overrides
    /// (so they cannot win over the sentinel in ApplyOverrides), and persists to disk.
    internal static void SetOriginalColor(string assetId)
    {
        _colors[assetId] = OriginalColorSentinel;
        bool slotsChanged = RemoveSlotsNoSave(assetId);
        Save();
        if (slotsChanged) SaveSlots();
        if (RemoveCustomColorNoSave(assetId)) SaveCustom();         // "original" clears any custom RGB
        if (RemoveCustomSlotsNoSave(assetId)) SaveCustomSlots();    // ... including per-slot customs
        if (RemoveAnimationNoSave(assetId)) SaveAnimations();       // ... and any animation
        if (RemoveSlotAnimationsNoSave(assetId)) SaveSlotAnimations();
    }

    /// Returns true when the stored colour for assetId is the OriginalColorSentinel.
    internal static bool IsOriginalMode(string? assetId)
        => assetId != null
           && _colors.TryGetValue(assetId, out int idx)
           && idx == OriginalColorSentinel;

    /// Returns the stored whole-asset colour index for this asset without interpreting it.
    /// Useful for updating the colour-picker indicator when switching slots.
    internal static bool TryGetColor(string assetId, out int colorIndex)
        => _colors.TryGetValue(assetId, out colorIndex);

    internal static int GetRealTypeColor(int typeIdx, int[] colorsEquipped)
        => (_savedTypeIndex == typeIdx) ? _savedTypeColor : colorsEquipped[typeIdx];

    // Resolves the colour index for a cosmetic: its committed override when set (and not the original sentinel), else the per-type fallback — for cosmetics mounted outside the normal pipeline (death-head preview prefab mounts).
    internal static int GetEffectiveColorIndex(string assetId, int fallbackTypeColor)
    {
        if (FeatureEnabled && _colors.TryGetValue(assetId, out int c) && c != OriginalColorSentinel)
            return c;
        return fallbackTypeColor;
    }

    // Committed overrides onto vanilla PlayerMaterials — for the death-head avatar (null playerAvatarVisuals makes ApplyOverrides bail). Bridge skipped; "original" sentinel keeps the type colour.
    internal static void ApplyVanillaOverridesTo(IEnumerable<PlayerMaterial> playerMaterials)
    {
        if (!FeatureEnabled || playerMaterials == null) return;
        // Under a preset context the colours live in the PREVIEW maps, not _colors/_customColors — include them in the "anything to apply?" guard or the preset's customs never reach the death head.
        bool hasAny = _colors.Count > 0 || _customColors.Count > 0
                   || (_presetPreviewActive && (_previewOverrides.Count > 0 || _previewCustom.Count > 0));
        if (!hasAny) return;

        foreach (var pm in playerMaterials)
        {
            if (pm == null) continue;
            if (pm.cosmetic == null)
            {
                // Base mesh PM ("Default" mesh): apply the synthetic "__base_N__" custom RGB if present.
                if (!Plugin.EnableVanillaCustomColors.Value) continue;
                string baseId = VanillaTintHelper.BaseMeshAssetId((int)pm.cosmeticType);
                if (_presetPreviewActive)
                {
                    // Preset context → read the preset's stored base custom (preview map). No entry = keep the palette colour ApplyBodyColors already set (preset has no base custom).
                    if (_previewCustom.TryGetValue(baseId, out var presetBaseCustom))
                        VanillaTintHelper.ApplyCustomRGB(pm, presetBaseCustom);
                }
                else if (_customColors.TryGetValue(baseId, out var baseCustom))
                    VanillaTintHelper.ApplyCustomRGB(pm, baseCustom);
                continue;
            }
            if (pm.cosmetic.cosmeticAsset is not { } asset || BridgeIds.IsBridgeAsset(asset)) continue;
            string assetId = asset.assetId;
            // Custom RGB outranks the palette index. Preview-aware like ApplyOverrides, so a preset-context death head resolves the preset's colours, not the live store.
            if (_presetPreviewActive)
            {
                if (_previewCustom.TryGetValue(assetId, out var previewCustom))
                    VanillaTintHelper.ApplyCustomRGB(pm, previewCustom);
                else if (_previewOverrides.TryGetValue(assetId, out int previewColor) && previewColor != OriginalColorSentinel)
                    pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, previewColor);
            }
            else if (_customColors.TryGetValue(assetId, out var customColor))
                VanillaTintHelper.ApplyCustomRGB(pm, customColor);
            else if (_colors.TryGetValue(assetId, out int idx) && idx != OriginalColorSentinel)
                pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, idx);
        }
    }

    // Committed colour for the assetId onto one BTM, with the per-slot/whole-asset precedence of ApplyOverrides. False = no override (caller restores original). Used by the death-head mount (BTMs outside playerAvatarVisuals).
    internal static bool ApplyLocalToBridgeTint(BridgeTintMaterial btm, string assetId)
    {
        if (!FeatureEnabled) return false;

        int matCount = btm.materials?.Length ?? 0;
        bool applied = false;
        for (int i = 0; i < matCount; i++)
        {
            int flatSlot = btm.SlotIdOf(i);
            Color? slotCustom  = TryGetCustomSlotColor(assetId, flatSlot, out var sc) ? sc : (Color?)null;
            int?   slotIndex   = TryGetSlotColor(assetId, flatSlot, out var si) ? si : (int?)null;
            Color? wholeCustom = _customColors.TryGetValue(assetId, out var wc) ? wc : (Color?)null;
            int?   wholeIndex  = _colors.TryGetValue(assetId, out var wi) ? wi : (int?)null;
            if (ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustom, wholeIndex))
                applied = true;
        }
        return applied;
    }

    // Builds a temporary colour payload aligning each cosmetic type's slot to the per-cosmetic colour of its first compatible cosmetic.
    // Vanilla peers receive ONE colour per type and render the first equipped cosmetic of it; our per-cosmetic colours travel on an RPC they ignore — so align each type slot to the first compatible cosmetic's colour. Prefer the vanilla cosmetic over a REPOLib one when both are equipped (true vanilla peers can only render the former). The clone is passed as SetupColors' _colors argument so the RPC uses aligned values without mutating menu/save state.
    // Returns null when no temporary payload is needed. Bridge cosmetics are skipped — a client without MoreHeadBridge can't render them.
    internal static int[]? BuildCompatibilitySyncColors(MetaManager? meta)
    {
        if (!FeatureEnabled) return null;
        if (meta?.cosmeticEquipped == null || meta.colorsEquipped == null) return null;

        var vanillaByType = new Dictionary<SemiFunc.CosmeticType, CosmeticAsset>();
        var moddedByType = new Dictionary<SemiFunc.CosmeticType, CosmeticAsset>();

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || BridgeIds.IsBridgeAsset(asset)) continue;

            if (BridgeIds.IsModdedCosmetic(asset))
                moddedByType.TryAdd(asset.type, asset);
            else
                vanillaByType.TryAdd(asset.type, asset);
        }

        int[]? payload = null;
        bool changed = false;
        var types = new HashSet<SemiFunc.CosmeticType>(vanillaByType.Keys);
        foreach (var type in moddedByType.Keys)
            types.Add(type);

        foreach (var type in types)
        {
            var asset = vanillaByType.TryGetValue(type, out var vanilla)
                ? vanilla
                : moddedByType[type];
            int ti = (int)asset.type;
            if (ti < 0 || ti >= meta.colorsEquipped.Length) continue;
            if (_colors.TryGetValue(asset.assetId, out int c) && c != OriginalColorSentinel
                && meta.colorsEquipped[ti] != c)
            {
                payload ??= (int[])meta.colorsEquipped.Clone();
                payload[ti] = c;
                changed = true;
            }
        }
        return changed ? payload : null;
    }

    // Applies per-cosmetic overrides on top of the type colour set by SetupColors*Logic. Local player + menu avatar only; handles both vanilla PMs and bridge BTMs.
    internal static void ApplyOverrides(PlayerCosmetics pc)
    {
        if (!FeatureEnabled) return;
        bool hasColors  = _colors.Count > 0;
        bool hasSlots   = _slotColors.Count > 0;
        bool hasCustom  = _customColors.Count > 0 || _customSlotColors.Count > 0;
        // hasPreview covers ALL preview maps + the active flag — a preset with only custom RGB (no palette index) must not slip through the early-out.
        bool hasPreview = _presetPreviewActive
            || _previewOverrides.Count > 0
            || _previewCustom.Count > 0
            || _previewSlotOverrides.Count > 0
            || _previewSlotCustom.Count > 0;
        if (!hasColors && !hasSlots && !hasPreview && !hasCustom) return;
        if (pc?.playerMaterials == null) return;

        var visuals = pc.playerAvatarVisuals;
        if (visuals == null) return;
        if (!visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true) return;

        // A REMOTE player's Mini-Semibot passes the local/menu gate above, but its colours are the OWNER's (applied by RemoteColorSync) — never paint the viewer's store onto it.
        if (AvatarIdentity.IsRemoteMini(pc)) return;

        // A RandomPreset mini takes its customs from the PRESET. If reached outside the preset context (autonomous SetupColors), re-establish it instead of leaking the live store.
        if (!MiniPresetContextActive)
        {
            int presetSlot = MiniSemibotSpawner.PresetSlotOf(pc);
            if (presetSlot >= 0)
            {
                RunWithPresetContext(presetSlot, () => ApplyOverrides(pc));
                return;
            }
        }

        // ── Vanilla cosmetics via PlayerMaterial ──────────────────────────────
        foreach (var pm in pc.playerMaterials)
        {
            if (pm == null) continue;

            // Base mesh PM (pm.cosmetic == null, the "Default" mesh). Honours preset-preview state: preview custom > live custom > current palette index. See ApplyBaseMeshColor.
            if (pm.cosmetic == null)
            {
                ApplyBaseMeshColor(pm, pc.colorsEquipped);
                continue;
            }

            if (pm.cosmetic.cosmeticAsset == null) continue;
            string assetId = pm.cosmetic.cosmeticAsset.assetId;
            // Custom RGB applies only when allowed (global config + per-cosmetic override); when disabled the cosmetic falls through to its palette index / original colour.
            bool customAllowed = CustomizerStore.GetEffectiveCustomColors(pm.cosmetic.cosmeticAsset);

            if (_presetPreviewActive)
            {
                // During a preset hover, _previewOverrides outranks the session _colors; cosmetics not in it keep the vanilla type colour.
                if (_previewCustom.TryGetValue(assetId, out var previewCustom))
                    VanillaTintHelper.ApplyCustomRGB(pm, previewCustom);
                else if (_previewOverrides.TryGetValue(assetId, out int previewColor)
                    && previewColor != OriginalColorSentinel)
                    pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, previewColor);
            }
            else if (customAllowed && _customColors.TryGetValue(assetId, out var customColor))
            {
                // Custom RGB overrides palette index for this cosmetic.
                VanillaTintHelper.ApplyCustomRGB(pm, customColor);
            }
            else if (_colors.TryGetValue(assetId, out int colorIdx))
            {
                if (colorIdx != OriginalColorSentinel)
                    pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, colorIdx);
            }
            else if (_previewOverrides.TryGetValue(assetId, out colorIdx))
            {
                // Fallback: colour-page temporary overrides (TemporarilyShowForColorPage). Guard the sentinel (-1): PlayerMaterial.ColorSet(-1) reads out of bounds — for Hurtable shaders "original" is the type colour already applied.
                if (colorIdx != OriginalColorSentinel)
                    pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, colorIdx);
            }
            else if (!BridgeIds.IsBridgeAsset(pm.cosmetic.cosmeticAsset))
            {
                // No override applies (e.g. custom colours toggled OFF with a custom still painted on) —
                // repaint the palette explicitly; vanilla's colorsEquipped diff never clears a painted-over PM.
                VanillaTintHelper.RepaintPalette(pm, pc);
            }
        }

        // ── Bridge cosmetics via BridgeTintMaterial ───────────────────────────
        var btms = visuals.GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true);
        foreach (var btm in btms)
        {
            if (btm?.cosmetic?.cosmeticAsset == null) continue;
            string assetId = btm.cosmetic.cosmeticAsset.assetId;

            // Ensure instance materials exist: newly-previewed cosmetics may not have run Setup() yet → matCount 0 → colour never applied.
            btm.EnsureSetup();

            if (_presetPreviewActive)
            {
                // No explicit entry = the cosmetic was original/default at save time — the preset's intent is the ORIGINAL colour. Reading the live store here would leak a current-outfit colour onto it during hover.
                if (!HasPreviewEntry(assetId))
                {
                    // Keep the live colour ONLY while an animation is active (the animator overwrites this static base each frame anyway); otherwise restore the original.
                    if (HasAnimation(assetId) || HasAnySlotAnimation(assetId))
                    {
                        int matCount = btm.materials?.Length ?? 0;
                        for (int i = 0; i < matCount; i++)
                        {
                            int flatSlot = btm.SlotIdOf(i);
                            Color? slotCustom  = TryGetCustomSlotColor(assetId, flatSlot, out var sc) ? sc : (Color?)null;
                            int?   slotIndex   = TryGetSlotColor(assetId, flatSlot, out var si) ? si : (int?)null;
                            Color? wholeCustom = _customColors.TryGetValue(assetId, out var wc) ? wc : (Color?)null;
                            int?   wholeIndex  = _colors.TryGetValue(assetId, out var wi) ? wi : (int?)null;
                            ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustom, wholeIndex);
                        }
                    }
                    else
                    {
                        btm.RestoreOriginalColor();
                    }
                }
                else
                {
                    // Explicitly in the preset — apply its stored colour: per-slot custom > per-slot index > whole custom > whole index. Animations are driven separately.
                    int matCount = btm.materials?.Length ?? 0;
                    for (int i = 0; i < matCount; i++)
                    {
                        int flatSlot = btm.SlotIdOf(i);
                        Color? slotCustom  = TryGetPreviewSlotCustom(assetId, flatSlot, out var sc) ? sc : (Color?)null;
                        int?   slotIndex   = TryGetPreviewSlotOverride(assetId, flatSlot, out var si) ? si : (int?)null;
                        Color? wholeCustom = _previewCustom.TryGetValue(assetId, out var wc) ? wc : (Color?)null;
                        int?   wholeIndex  = _previewOverrides.TryGetValue(assetId, out var wi) ? wi : (int?)null;
                        ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustom, wholeIndex);
                    }
                }
            }
            else
            {
                // Per-slot pass — each slot resolves independently: per-slot custom > per-slot index > whole custom > whole index > preview. Custom RGB skipped when not allowed, falling through to palette/original.
                bool customAllowed = CustomizerStore.GetEffectiveCustomColors(btm.cosmetic.cosmeticAsset);
                int matCount = btm.materials?.Length ?? 0;
                for (int i = 0; i < matCount; i++)
                {
                    int flatSlot = btm.SlotIdOf(i);
                    Color? slotCustom  = customAllowed && TryGetCustomSlotColor(assetId, flatSlot, out var sc) ? sc : (Color?)null;
                    int?   slotIndex   = TryGetSlotColor(assetId, flatSlot, out var si) ? si : (int?)null;
                    Color? wholeCustom = customAllowed && _customColors.TryGetValue(assetId, out var wc) ? wc : (Color?)null;
                    // Whole-index tier falls back to colour-page temporary overrides (_previewOverrides) when no committed _colors entry exists.
                    int?   wholeIndex  = _colors.TryGetValue(assetId, out var wi) ? wi
                                       : (_previewOverrides.TryGetValue(assetId, out var pv) ? pv : (int?)null);
                    ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustom, wholeIndex);
                }
            }
        }
    }

    // Applies a palette index (or restores the original colour for the sentinel) to one slot.
    private static void ApplySlotIndex(BridgeTintMaterial btm, int localSlot, int colorIndex)
    {
        if (colorIndex == OriginalColorSentinel) btm.RestoreOriginalColorInSlot(localSlot);
        else btm.ApplyColorToSlot(localSlot, colorIndex);
    }

    // Single source of truth for per-slot colour precedence on a BTM (local AND remote must agree):
    // per-slot custom RGB > per-slot palette index > whole-asset custom RGB > whole-asset palette index.
    // Each tier is passed pre-resolved (null = absent); palette indexes honour the original-colour sentinel.
    // Returns true if any tier applied a colour to the slot.
    internal static bool ApplySlotPrecedence(BridgeTintMaterial btm, int localSlot,
        Color? slotCustom, int? slotIndex, Color? wholeCustom, int? wholeIndex)
    {
        if (slotCustom.HasValue)  { btm.ApplyColorRGBToSlot(localSlot, slotCustom.Value);  return true; }
        if (slotIndex.HasValue)   { ApplySlotIndex(btm, localSlot, slotIndex.Value);        return true; }
        if (wholeCustom.HasValue) { btm.ApplyColorRGBToSlot(localSlot, wholeCustom.Value);  return true; }
        if (wholeIndex.HasValue)  { ApplySlotIndex(btm, localSlot, wholeIndex.Value);        return true; }
        return false;
    }

    // Colours a base-mesh PM (pm.cosmetic == null). Preset-preview-aware. Priority: preview custom (when active) > live custom > colorsEquipped[type] palette. Shared with VanillaTintHelper.ReapplyBaseMeshColors so both agree.
    internal static void ApplyBaseMeshColor(PlayerMaterial pm, int[]? colorsEquipped)
    {
        if (!Plugin.EnableVanillaCustomColors.Value) return;
        if (pm == null || pm.cosmetic != null || !pm.tintable) return;
        int ti = (int)pm.cosmeticType;
        string baseId = VanillaTintHelper.BaseMeshAssetId(ti);

        if (_presetPreviewActive)
        {
            // During a preset hover the preview store is authoritative: only customs the preset actually stored apply; otherwise fall through to the palette.
            if (_previewCustom.TryGetValue(baseId, out var previewCustom))
            {
                VanillaTintHelper.ApplyCustomRGB(pm, previewCustom);
                return;
            }
        }
        else if (_customColors.TryGetValue(baseId, out var liveCustom))
        {
            VanillaTintHelper.ApplyCustomRGB(pm, liveCustom);
            return;
        }

        // Palette fallback from the live colorsEquipped (also covers Paint All / Body / subcategory).
        if (colorsEquipped == null || ti < 0 || ti >= colorsEquipped.Length) return;
        int ci = colorsEquipped[ti];
        if (ci >= 0 && MetaManager.instance?.colors != null && ci < MetaManager.instance.colors.Count)
            pm.ColorSet(PropAlbedo, PropEmission, PropFresnel, ci);
    }

    // Removes every "__base_N__" base-mesh custom. Used on preset load (ClearAllForType never reaches them); RestorePreset re-adds the loaded preset's own. Caller persists if true.
    internal static bool ClearAllBaseMeshCustomColorsNoSave()
    {
        List<string>? keys = null;
        foreach (var k in _customColors.Keys)
            if (VanillaTintHelper.IsBaseMeshId(k)) (keys ??= new List<string>()).Add(k);
        if (keys == null) return false;
        foreach (var k in keys) _customColors.Remove(k);
        return true;
    }

    /// Returns true when <paramref name="pc"/> belongs to a locally-owned death-head avatar.
    /// Used to bypass the isLocal guard for death heads, whose PlayerAvatarVisuals may not
    /// have playerAvatar set while still being under local player control.
    internal static bool IsLocalDeathHeadPc(PlayerCosmetics? pc)
        => pc?.deathHead != null && pc.deathHead.setup
           && pc.deathHead.playerAvatar?.photonView?.IsMine == true;

    // v2 unified store: all six colour maps in one file/write. Colors persist as [r,g,b].
    private sealed class ColorStoreData
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, int>? Colors { get; set; }
        public Dictionary<string, Dictionary<int, int>>? Slots { get; set; }
        public Dictionary<string, ColorAnimation>? Animations { get; set; }
        public Dictionary<string, Dictionary<int, ColorAnimation>>? SlotAnimations { get; set; }
        public Dictionary<string, float[]>? Custom { get; set; }
        public Dictionary<string, Dictionary<int, float[]>>? CustomSlots { get; set; }
    }

    internal static void Load()
    {
        bool legacyFormat = false;
        try
        {
            // Pre-v3 location migration (persistentDataPath → BepInEx/config).
            if (!File.Exists(SavePath) && File.Exists(LegacyPath))
            {
                try
                {
                    File.Move(LegacyPath, SavePath);
                    BceConsole.LogInfo("PerCosmeticColors: migrated save file to BepInEx/config", ConsoleColor.Blue);
                }
                catch (Exception ex)
                {
                    BceConsole.LogWarning($"PerCosmeticColors: migration failed: {ex.Message}");
                }
            }

            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                // v1 files are bare assetId→index maps; assetIds never contain "Version".
                if (json.Contains("\"Version\""))
                {
                    var data = JsonConvert.DeserializeObject<ColorStoreData>(json);
                    if (data != null)
                    {
                        _colors         = data.Colors ?? new();
                        _slotColors     = data.Slots ?? new();
                        _animations     = data.Animations ?? new();
                        _slotAnimations = data.SlotAnimations ?? new();
                        SetCustomFromDto(data.Custom);
                        SetCustomSlotsFromDto(data.CustomSlots);
                    }
                }
                else
                {
                    _colors = JsonConvert.DeserializeObject<Dictionary<string, int>>(json) ?? new();
                    legacyFormat = true;
                }
            }
            else
            {
                legacyFormat = true;   // no main file — still pick up any v1 sibling files
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: load failed: {ex.Message}");
            _colors = new();
        }

        if (legacyFormat)
        {
            // One-time v1 → v2 migration: read the five sibling files, then persist everything
            // unified. The v1 files stay on disk (harmless; a downgrade still finds them).
            LoadSlots();
            LoadAnimations();
            LoadCustom();
            if (_colors.Count > 0 || _slotColors.Count > 0 || _animations.Count > 0
                || _slotAnimations.Count > 0 || HasAnyCustomData())
                Save();
        }

        LoadPresets();   // presets keep their own file (independent save cadence)
    }

    // Write-task chain: ContinueWith serialises background writes so WriteAllText never runs on two threads at once.
    private static Task _lastWrite = Task.CompletedTask;

    // Bumped on every colour-store save — MiniDeathHead compares it each frame to rebuild a stale death-head model.
    internal static int StoreVersion;

    /// Persists ALL six colour maps (one file, one write). The per-kind Save*() methods alias this.
    internal static void Save()
    {
        StoreVersion++;
        // Serialize on the game thread — the maps are only mutated on it, so the snapshot is safe before the lambda captures it.
        var data = new ColorStoreData
        {
            Colors         = _colors,
            Slots          = _slotColors,
            Animations     = _animations,
            SlotAnimations = _slotAnimations,
            Custom         = CustomToDto(),
            CustomSlots    = CustomSlotsToDto(),
        };
        string json = JsonConvert.SerializeObject(data);
        _lastWrite = AtomicJson.QueueWrite(_lastWrite, SavePath, json, "PerCosmeticColors: save failed");
    }

    /// Blocks briefly for in-flight background disk writes to finish. Called on quit so the
    /// last colour/slot/animation/preset change is never lost if the process exits immediately.
    internal static void FlushPendingWrites()
    {
        try { Task.WaitAll(new[] { _lastWrite, _lastPresetWrite }, TimeSpan.FromSeconds(2)); }
        catch { /* best-effort flush on shutdown */ }
    }
}
