using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

// Partial class — per-slot colour storage half of PerCosmeticColors (main save/load/apply lives in PerCosmeticColors.cs). A bridge cosmetic can have several BridgeTintMaterials (one per renderer); _slotColors stores per-renderer (btmIndex) overrides independent of the whole-asset _colors. Slot -1 (= ALL) → _colors[assetId]; slot >=0 → _slotColors[assetId][btmIndex]. The active slot is tracked in ActiveSlot and reset to -1 when the colour page closes.
internal static partial class PerCosmeticColors
{
    private static readonly string SlotSavePath = BridgePaths.Of("PerCosmeticSlotColors.json");

    // Per-slot overrides: assetId → (btmIndex → colorIndex). Stored separately from _colors (whole-asset) so the two coexist independently.
    private static Dictionary<string, Dictionary<int, int>> _slotColors = new();

    // BTM index targeted in the picker: -1 = ALL (whole-asset _colors), >=0 = _slotColors[assetId][ActiveSlot]. Reset to -1 by ColorPageClosePatch.
    internal static int ActiveSlot { get; set; } = -1;

    // ── Slot colour CRUD ───────────────────────────────────────────────────────

    internal static void SetSlotColor(string assetId, int slot, int colorIndex)
    {
        if (!_slotColors.TryGetValue(assetId, out var slots))
            _slotColors[assetId] = slots = new Dictionary<int, int>();
        slots[slot] = colorIndex;
        SaveSlots();
        if (RemoveCustomSlotNoSave(assetId, slot)) SaveCustomSlots();        // a per-slot pick clears that slot's custom RGB
        if (RemoveSlotAnimationNoSave(assetId, slot)) SaveSlotAnimations();  // ... and that slot's animation
    }

    // Removes a single slot's palette override without saving. Callers Save() once.
    internal static bool RemoveSlotColorNoSave(string assetId, int slot)
        => _slotColors.TryGetValue(assetId, out var slots) && slots.Remove(slot);

    internal static bool TryGetSlotColor(string assetId, int slot, out int colorIndex)
    {
        colorIndex = 0;
        return _slotColors.TryGetValue(assetId, out var slots)
               && slots.TryGetValue(slot, out colorIndex);
    }

    internal static bool HasAnySlotColor(string assetId)
        => _slotColors.TryGetValue(assetId, out var s) && s.Count > 0;

    internal static IReadOnlyDictionary<int, int>? GetSlots(string assetId)
        => _slotColors.TryGetValue(assetId, out var d) ? d : null;

    internal static IReadOnlyDictionary<string, Dictionary<int, int>> GetAllSlots()
        => _slotColors;

    // Removes all per-slot overrides for the asset (does NOT touch _colors); callers save if needed.
    internal static bool RemoveSlotsNoSave(string assetId)
        => _slotColors.Remove(assetId);

    internal static void ClearSlotsForAsset(string assetId)
    {
        if (_slotColors.Remove(assetId)) SaveSlots();
    }

    internal static void ClearAllSlotsNoSave()
        => _slotColors.Clear();

    // ── Persistence ────────────────────────────────────────────────────────────

    // v1 sibling file, read only by the one-time Load() migration; new saves go through Save().
    internal static void LoadSlots()
    {
        try
        {
            if (!File.Exists(SlotSavePath)) return;
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, int>>>(
                File.ReadAllText(SlotSavePath));
            if (loaded != null) _slotColors = loaded;
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: slot colours load failed: {ex.Message}");
        }
    }

    internal static void SaveSlots() => Save();
}
