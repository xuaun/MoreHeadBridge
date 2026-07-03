using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

// Partial class — custom-RGB storage half of PerCosmeticColors. Kept in its own map: the apply path checks _customColors FIRST and skips palette-index logic when present. Saved to config\MoreHeadBridge\PerCosmeticCustomColors.json.
internal static partial class PerCosmeticColors
{
    private static readonly string CustomSavePath = BridgePaths.Of("PerCosmeticCustomColors.json");

    // assetId → custom RGB (whole-asset). Alpha is ignored (tinting preserves each slot's alpha).
    private static Dictionary<string, Color> _customColors = new();

    internal static bool HasCustomColor(string? assetId)
        => assetId != null && _customColors.ContainsKey(assetId);

    internal static bool TryGetCustomColor(string assetId, out Color color)
        => _customColors.TryGetValue(assetId, out color);

    // Sets the custom colour and clears any palette/slot override for the asset so the custom colour fully replaces them and the index path can't fight it. Persists the affected stores.
    internal static void SetCustomColor(string assetId, Color color)
    {
        _customColors[assetId] = color;
        bool colorsChanged     = _colors.Remove(assetId);
        bool slotsChanged      = RemoveSlotsNoSave(assetId);
        bool animChanged       = RemoveAnimationNoSave(assetId);
        bool slotAnimChanged   = RemoveSlotAnimationsNoSave(assetId);
        bool customSlotsChanged = RemoveCustomSlotsNoSave(assetId);
        SaveCustom();
        if (colorsChanged)      Save();
        if (slotsChanged)       SaveSlots();
        if (animChanged)        SaveAnimations();
        if (slotAnimChanged)    SaveSlotAnimations();
        if (customSlotsChanged) SaveCustomSlots();
    }

    internal static bool RemoveCustomColorNoSave(string assetId)
        => _customColors.Remove(assetId);

    // Sets a custom colour without saving (batched section operations save once at the end). Also clears the palette index, slot overrides and animations for this asset so custom RGB wins.
    internal static void SetCustomColorNoSave(string assetId, Color color)
    {
        _customColors[assetId] = color;
        _colors.Remove(assetId);
        RemoveSlotsNoSave(assetId);
        RemoveAnimationNoSave(assetId);
        RemoveSlotAnimationsNoSave(assetId);
        RemoveCustomSlotsNoSave(assetId);
    }

    internal static IReadOnlyDictionary<string, Color> GetAllCustom() => _customColors;
    internal static IReadOnlyDictionary<string, Dictionary<int, Color>> GetAllCustomSlots() => _customSlotColors;

    // True when the effective colour for the given slot (-1 = whole asset) resolves to a custom RGB — mirrors the apply priority (per-slot custom > per-slot palette > whole custom).
    internal static bool IsSlotCustom(string? assetId, int slot)
    {
        if (assetId == null) return false;
        if (slot >= 0)
        {
            if (TryGetCustomSlotColor(assetId, slot, out _)) return true;
            if (TryGetSlotColor(assetId, slot, out _)) return false;   // per-slot palette overrides
        }
        return HasCustomColor(assetId);
    }

    // The custom colour that applies to the given slot (-1 = whole), if any.
    internal static bool TryGetSlotCustom(string assetId, int slot, out Color color)
    {
        if (slot >= 0 && TryGetCustomSlotColor(assetId, slot, out color)) return true;
        if (slot >= 0 && TryGetSlotColor(assetId, slot, out _)) { color = default; return false; }
        return TryGetCustomColor(assetId, out color);
    }

    // ── Per-slot custom colours (assetId → flatSlot → RGB) ───────────────────────
    private static readonly string CustomSlotSavePath = BridgePaths.Of("PerCosmeticCustomSlotColors.json");

    private static Dictionary<string, Dictionary<int, Color>> _customSlotColors = new();

    internal static bool TryGetCustomSlotColor(string assetId, int slot, out Color color)
    {
        color = default;
        return _customSlotColors.TryGetValue(assetId, out var slots) && slots.TryGetValue(slot, out color);
    }

    internal static bool HasAnyCustomSlotColor(string? assetId)
        => assetId != null && _customSlotColors.TryGetValue(assetId, out var s) && s.Count > 0;

    // Sets a per-slot custom colour, clearing that slot's palette override (custom replaces it).
    internal static void SetCustomSlotColor(string assetId, int slot, Color color)
    {
        if (!_customSlotColors.TryGetValue(assetId, out var slots))
            _customSlotColors[assetId] = slots = new Dictionary<int, Color>();
        slots[slot] = color;
        SaveCustomSlots();

        // Clear the same slot's palette override so the index path can't fight the custom colour.
        if (_slotColors.TryGetValue(assetId, out var idxSlots) && idxSlots.Remove(slot))
            SaveSlots();
        // ... and that slot's animation, so a static custom colour stops it animating.
        if (RemoveSlotAnimationNoSave(assetId, slot)) SaveSlotAnimations();
    }

    internal static bool RemoveCustomSlotsNoSave(string assetId)
        => _customSlotColors.Remove(assetId);

    internal static bool RemoveCustomSlotNoSave(string assetId, int slot)
        => _customSlotColors.TryGetValue(assetId, out var slots) && slots.Remove(slot);

    internal static void LoadCustom()
    {
        LoadWholeCustom();
        LoadCustomSlots();
    }

    private static void LoadCustomSlots()
    {
        try
        {
            if (!File.Exists(CustomSlotSavePath)) return;
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, float[]>>>(
                File.ReadAllText(CustomSlotSavePath));
            if (loaded == null) return;
            _customSlotColors = new();
            foreach (var kv in loaded)
            {
                var slots = new Dictionary<int, Color>();
                foreach (var s in kv.Value)
                    if (s.Value is { Length: >= 3 })
                        slots[s.Key] = new Color(s.Value[0], s.Value[1], s.Value[2]);
                _customSlotColors[kv.Key] = slots;
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: custom slot colours load failed: {ex.Message}");
        }
    }

    internal static void SaveCustomSlots() => Save();

    // ── DTO conversion (Color ↔ [r,g,b]) shared by Save()/Load() ────────────────

    internal static Dictionary<string, float[]> CustomToDto()
    {
        var dto = new Dictionary<string, float[]>(_customColors.Count);
        foreach (var kv in _customColors)
            dto[kv.Key] = new[] { kv.Value.r, kv.Value.g, kv.Value.b };
        return dto;
    }

    internal static Dictionary<string, Dictionary<int, float[]>> CustomSlotsToDto()
    {
        var dto = new Dictionary<string, Dictionary<int, float[]>>(_customSlotColors.Count);
        foreach (var kv in _customSlotColors)
        {
            var slots = new Dictionary<int, float[]>();
            foreach (var s in kv.Value)
                slots[s.Key] = new[] { s.Value.r, s.Value.g, s.Value.b };
            dto[kv.Key] = slots;
        }
        return dto;
    }

    internal static void SetCustomFromDto(Dictionary<string, float[]>? dto)
    {
        _customColors = new();
        if (dto == null) return;
        foreach (var kv in dto)
            if (kv.Value is { Length: >= 3 })
                _customColors[kv.Key] = new Color(kv.Value[0], kv.Value[1], kv.Value[2]);
    }

    internal static void SetCustomSlotsFromDto(Dictionary<string, Dictionary<int, float[]>>? dto)
    {
        _customSlotColors = new();
        if (dto == null) return;
        foreach (var kv in dto)
        {
            var slots = new Dictionary<int, Color>();
            foreach (var s in kv.Value)
                if (s.Value is { Length: >= 3 })
                    slots[s.Key] = new Color(s.Value[0], s.Value[1], s.Value[2]);
            _customSlotColors[kv.Key] = slots;
        }
    }

    internal static bool HasAnyCustomData()
        => _customColors.Count > 0 || _customSlotColors.Count > 0;

    // ── Persistence ────────────────────────────────────────────────────────────
    // v1 sibling files, read only by the one-time Load() migration; new saves go through Save().

    private static void LoadWholeCustom()
    {
        try
        {
            if (!File.Exists(CustomSavePath)) return;
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(
                File.ReadAllText(CustomSavePath));
            if (loaded == null) return;
            _customColors = new();
            foreach (var kv in loaded)
                if (kv.Value is { Length: >= 3 })
                    _customColors[kv.Key] = new Color(kv.Value[0], kv.Value[1], kv.Value[2]);
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: custom colours load failed: {ex.Message}");
        }
    }

    internal static void SaveCustom() => Save();
}
