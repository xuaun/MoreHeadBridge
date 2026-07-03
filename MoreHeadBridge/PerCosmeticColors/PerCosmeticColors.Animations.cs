using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

// Partial class — animated-colour storage half of PerCosmeticColors (CRUD + Save/Load on the shared _lastWrite queue). Whole-asset specs → ColorAnimations.json (assetId → ColorAnimation); per-slot specs → SlotColorAnimations.json (assetId → flatSlot → ColorAnimation).
internal static partial class PerCosmeticColors
{
    private static readonly string AnimSavePath = BridgePaths.Of("ColorAnimations.json");
    private static readonly string SlotAnimSavePath = BridgePaths.Of("SlotColorAnimations.json");

    // assetId → whole-asset animation spec. An entry means the whole cosmetic is animated.
    private static Dictionary<string, ColorAnimation> _animations = new();

    // assetId → (flatSlot → animation spec). A slot here animates independently of the whole spec.
    private static Dictionary<string, Dictionary<int, ColorAnimation>> _slotAnimations = new();

    // ── Whole-asset animation CRUD ──────────────────────────────────────────────

    // Sets the whole-asset animation and resets every other store for the asset (palette, custom, per-slot) so the animation governs all slots — mirrors SetCustomColor's full replace.
    internal static void SetAnimation(string assetId, ColorAnimation spec)
    {
        _animations[assetId] = spec;
        SaveAnimations();
        if (RemoveColorNoSave(assetId))        Save();
        if (RemoveSlotsNoSave(assetId))        SaveSlots();
        if (RemoveCustomColorNoSave(assetId))  SaveCustom();
        if (RemoveCustomSlotsNoSave(assetId))  SaveCustomSlots();
        if (RemoveSlotAnimationsNoSave(assetId)) SaveSlotAnimations();
    }

    internal static bool TryGetAnimation(string assetId, out ColorAnimation spec)
    {
        if (assetId != null && _animations.TryGetValue(assetId, out spec!))
            return true;
        spec = null!;
        return false;
    }

    internal static bool HasAnimation(string? assetId)
        => assetId != null && _animations.ContainsKey(assetId);

    internal static IReadOnlyDictionary<string, ColorAnimation> GetAllAnimations()
        => _animations;

    internal static IReadOnlyDictionary<string, Dictionary<int, ColorAnimation>> GetAllSlotAnimations()
        => _slotAnimations;

    // Removes the whole-asset animation for an asset without saving. For batched ops that Save() once.
    internal static bool RemoveAnimationNoSave(string assetId)
        => _animations.Remove(assetId);

    internal static void ClearAnimationForAsset(string assetId)
    {
        if (_animations.Remove(assetId)) SaveAnimations();
    }

    // Removes BOTH whole-asset and all per-slot animations for an asset (saving affected stores); true if anything was removed. Used when a cosmetic becomes a bridge mesh-switch (can't animate).
    internal static bool ClearAllAnimationForAsset(string assetId)
    {
        bool whole = RemoveAnimationNoSave(assetId);
        bool slots = RemoveSlotAnimationsNoSave(assetId);
        if (whole) SaveAnimations();
        if (slots) SaveSlotAnimations();
        return whole || slots;
    }

    internal static void ClearAllAnimationsNoSave()
    {
        _animations.Clear();
        _slotAnimations.Clear();
    }

    // ── Per-slot animation CRUD ─────────────────────────────────────────────────

    // Sets a per-slot animation and clears that slot's static palette / custom so they can't fight it. Does NOT touch the whole-asset spec or other slots.
    internal static void SetSlotAnimation(string assetId, int slot, ColorAnimation spec)
    {
        if (!_slotAnimations.TryGetValue(assetId, out var slots))
            _slotAnimations[assetId] = slots = new Dictionary<int, ColorAnimation>();
        slots[slot] = spec;
        SaveSlotAnimations();

        if (RemoveSlotColorNoSave(assetId, slot)) SaveSlots();
        if (RemoveCustomSlotNoSave(assetId, slot)) SaveCustomSlots();
    }

    internal static bool TryGetSlotAnimation(string assetId, int slot, out ColorAnimation spec)
    {
        spec = null!;
        return _slotAnimations.TryGetValue(assetId, out var slots) && slots.TryGetValue(slot, out spec!);
    }

    internal static bool HasAnySlotAnimation(string? assetId)
        => assetId != null && _slotAnimations.TryGetValue(assetId, out var s) && s.Count > 0;

    internal static IReadOnlyDictionary<int, ColorAnimation>? GetSlotAnimations(string assetId)
        => _slotAnimations.TryGetValue(assetId, out var d) ? d : null;

    // Removes all per-slot animations for an asset without saving.
    internal static bool RemoveSlotAnimationsNoSave(string assetId)
        => _slotAnimations.Remove(assetId);

    // Removes a single slot's animation without saving.
    internal static bool RemoveSlotAnimationNoSave(string assetId, int slot)
        => _slotAnimations.TryGetValue(assetId, out var slots) && slots.Remove(slot);

    internal static void ClearSlotAnimationForAsset(string assetId, int slot)
    {
        if (RemoveSlotAnimationNoSave(assetId, slot)) SaveSlotAnimations();
    }

    // True when the given slot resolves to an animation (-1 = whole asset).
    internal static bool IsSlotAnimated(string? assetId, int slot)
    {
        if (assetId == null) return false;
        if (slot >= 0 && TryGetSlotAnimation(assetId, slot, out _)) return true;
        return slot < 0 && HasAnimation(assetId);
    }

    // The combined animation specs (whole + per-slot) for an asset, used to (re)bind animators.
    internal static AnimSet GetAnimSet(string assetId)
    {
        _animations.TryGetValue(assetId, out var whole);
        var perSlot = _slotAnimations.TryGetValue(assetId, out var s) && s.Count > 0 ? s : null;

        // Slots with a static colour only matter when a whole-asset animation would otherwise cover them; collect them so the animator leaves those slots on their fixed colour.
        HashSet<int>? statics = null;
        if (whole != null)
        {
            CollectKeys(ref statics, _slotColors, assetId);
            CollectKeys(ref statics, _customSlotColors, assetId);
        }
        return new AnimSet(whole, perSlot, statics);
    }

    private static void CollectKeys<TVal>(
        ref HashSet<int>? into, Dictionary<string, Dictionary<int, TVal>> store, string assetId)
    {
        if (!store.TryGetValue(assetId, out var slots) || slots.Count == 0) return;
        into ??= new HashSet<int>();
        foreach (var k in slots.Keys) into.Add(k);
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    // v1 sibling files, read only by the one-time Load() migration; new saves go through Save().
    internal static void LoadAnimations()
    {
        try
        {
            if (File.Exists(AnimSavePath))
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, ColorAnimation>>(
                    File.ReadAllText(AnimSavePath));
                if (loaded != null) _animations = loaded;
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: animations load failed: {ex.Message}");
        }

        try
        {
            if (File.Exists(SlotAnimSavePath))
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, ColorAnimation>>>(
                    File.ReadAllText(SlotAnimSavePath));
                if (loaded != null) _slotAnimations = loaded;
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: slot animations load failed: {ex.Message}");
        }
    }

    internal static void SaveAnimations() => Save();

    internal static void SaveSlotAnimations() => Save();
}
