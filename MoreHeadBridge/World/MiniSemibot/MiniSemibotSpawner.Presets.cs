// RandomPreset outfit selection for the Mini-Semibot: rolling/remembering a saved preset slot and the
// preset-mini predicates. Spawn/lifecycle lives in MiniSemibotSpawner.cs; placement in MiniSemibotSpawner.Placement.cs.

using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static partial class MiniSemibotSpawner
{
    // The rolled preset slot persists in MiniSemibotVisualPrefs (disk) — stable across re-spawns AND restarts. Reset only on a genuine un-equip→equip or a popup re-roll.
    private static bool _semibotWasEquipped;

    // Forgets the current roll so the next RandomPreset spawn picks a fresh outfit (popup re-roll).
    internal static void ClearLocalRoll() => MiniSemibotVisualPrefs.RolledPreset = -1;

    // On a genuine LOCAL un-equip, drop the remembered roll so re-equipping rolls a NEW preset; menu↔game re-spawns keep the same one.
    internal static void UpdateEquipState()
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;
        bool equipped = false;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var a = meta.cosmeticAssets[idx];
            if (a != null && a.assetId == MiniSemibotCosmetic.AssetId) { equipped = true; break; }
        }
        if (_semibotWasEquipped && !equipped) MiniSemibotVisualPrefs.RolledPreset = -1;
        _semibotWasEquipped = equipped;
    }

    // LOCAL RandomPreset: snapshot a saved preset onto the tag — reuse the remembered roll, else pick a fresh non-empty slot. Any other case clears the snapshot (live outfit).
    private static void RollOutfitForTag(PlayerCosmetics wearer, MiniSemibotTag tag)
    {
        tag.PresetCosmetics = null;
        tag.PresetColors = null;
        tag.PresetSlot = -1;

        if (!IsLocalWearer(wearer.playerAvatarVisuals)
            || MiniSemibotSettings.OutfitMode != MiniSemibotOutfitMode.RandomPreset) return;

        var meta = MetaManager.instance;
        if (meta?.cosmeticPresets == null) return;

        // Reuse the remembered slot if still valid + non-empty → same outfit in menu/in-game and across restarts (persisted).
        int kept = MiniSemibotVisualPrefs.RolledPreset;
        if (kept >= 0 && IsPresetNonEmpty(meta, kept))
        {
            ApplyPresetToTag(meta, kept, tag);
            return;
        }

        // Otherwise pick a fresh random non-empty slot and remember it.
        var candidates = new List<int>();
        for (int i = 0; i < meta.cosmeticPresets.Count; i++)
            if (IsPresetNonEmpty(meta, i)) candidates.Add(i);

        if (candidates.Count == 0)
        {
            // RollOutfitForTag re-runs often and minis respawn across transitions — warn once per session, not per mini.
            if (!_warnedNoPresets)
            {
                _warnedNoPresets = true;
                BceConsole.LogWarning("[MiniSemibot] RandomPreset: no saved presets found in meta.cosmeticPresets — " +
                                      "falling back to your live outfit. (Save an outfit in the Presets tab first.)");
            }
            return;
        }
        _warnedNoPresets = false; // presets exist again → a later deletion may warn once more

        int pick = candidates[Random.Range(0, candidates.Count)];
        MiniSemibotVisualPrefs.RolledPreset = pick;
        ApplyPresetToTag(meta, pick, tag);
    }

    private static bool IsPresetNonEmpty(MetaManager meta, int slot)
    {
        if (slot < 0 || slot >= meta.cosmeticPresets.Count) return false;
        bool hasCos = meta.cosmeticPresets[slot] != null && meta.cosmeticPresets[slot].Count > 0;
        bool hasCol = meta.colorPresets != null && slot < meta.colorPresets.Count
                      && meta.colorPresets[slot] != null && meta.colorPresets[slot].Count > 0;
        return hasCos || hasCol;
    }

    private static void ApplyPresetToTag(MetaManager meta, int slot, MiniSemibotTag tag)
    {
        tag.PresetCosmetics = meta.cosmeticPresets[slot].ToArray();
        if (meta.colorPresets != null && slot < meta.colorPresets.Count && meta.colorPresets[slot] != null)
            tag.PresetColors = meta.colorPresets[slot].ToArray();
        tag.PresetSlot = slot;   // so RefreshOutfit can pull the preset's per-cosmetic custom/animated colours
    }

    // True for a local RandomPreset mini: its customs come from the PRESET, so the global PerCosmeticColors apply path skips it (RefreshOutfit drives it under a preset context). SameAsPlayer minis mirror you and are NOT preset minis.
    internal static bool IsPresetMini(PlayerCosmetics? pc) => PresetSlotOf(pc) >= 0;

    // Same test for any component under a mini (e.g. BridgeTintMaterial), for paint loops iterating renderers. RandomPreset minis only.
    internal static bool IsPresetMiniComponent(Component? c)
    {
        if (c == null) return false;
        var tag = c.GetComponentInParent<MiniSemibotTag>();
        return tag != null && tag.PresetSlot >= 0;
    }

    // The rolled preset slot for a Mini-Semibot's PlayerCosmetics, or -1 if this isn't a RandomPreset mini.
    internal static int PresetSlotOf(PlayerCosmetics? pc)
    {
        if (pc == null) return -1;
        var tag = pc.GetComponentInParent<MiniSemibotTag>();
        return tag != null ? tag.PresetSlot : -1;
    }
}
