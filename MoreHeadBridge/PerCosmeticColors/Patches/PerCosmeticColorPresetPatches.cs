using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// When a preset is saved (or deleted), also save/remove its per-cosmetic colour overrides so they can be restored on load.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPresetSet))]
internal static class CosmeticPresetSetColorsPatch
{
    [HarmonyPostfix]
    private static void Postfix(int _index, List<int> _cosmeticEquipped)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;

        if (_cosmeticEquipped.Count == 0)
            PerCosmeticColors.DeletePresetColors(_index);
        else
            PerCosmeticColors.SavePreset(_index, _cosmeticEquipped);
    }
}

// Vanilla judges "preset equipped" by cosmetics + per-type colours only, so two presets differing only in per-cosmetic colours look identical and the second button stays disabled. When our stored colours differ from live, report "not equipped" so the button stays clickable; PresetLoadColorsPatch restores colours on click.
[HarmonyPatch(typeof(MenuElementCosmeticPreset), "IsEquipped")]
internal static class PresetIsEquippedColorPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticPreset __instance, ref bool __result)
    {
        if (!__result || !PerCosmeticColors.FeatureEnabled) return;
        if (!PerCosmeticColors.PresetMatchesCurrent(__instance.presetIndex))
            __result = false;
    }
}

// Tracks the actively-hovered preset button so PresetPreviewColorsPatch can resolve the preset index directly instead of scanning cosmeticEquippedPreview.
[HarmonyPatch(typeof(MenuElementCosmeticPreset), "Update")]
internal static class PresetHoverIndexTrackPatch
{
    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticPreset __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (__instance.menuButton != null && __instance.menuButton.hovering)
            PerCosmeticColors.NotifyPresetHoverStart(__instance.presetIndex);
    }
}

// On preset hover-start, resolve which preset and populate _previewOverrides from its stored colours so ApplyOverrides shows them.
[HarmonyPatch(typeof(MetaManager), nameof(MetaManager.CosmeticPreviewSet))]
internal static class PresetPreviewColorsPatch
{
    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance, bool _state)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        if (!_state) return; // CosmeticPreviewSetClearPatch handles _state=false

        int presetIndex = FindMatchingPreset(__instance);
        if (presetIndex < 0)
        {
            // Not a preset hover: drop any stale preset-preview state now (else the animator reads the stale preset's specs and an animated cosmetic freezes). KEEP _previewOverrides — it also holds this hover's type-colour pins.
            if (PerCosmeticColors.PresetPreviewActive)
            {
                PerCosmeticColors.ClearPresetPreviewOnly();
                ColorAnimatorRefresher.RefreshLocal();
            }
            return;
        }

        // Enter preview FRESH before checking for colours: clearing first stops a previous preset's custom RGB lingering (custom outranks index), and an active-but-empty preview makes cosmetics show the preset's intent (original colour) instead of the live store.
        PerCosmeticColors.ClearPreviewOverrides();
        PerCosmeticColors.SetPresetPreviewActive(true);

        var presetColors = PerCosmeticColors.GetPresetColors(presetIndex);
        if (presetColors != null)
            foreach (var kv in presetColors)
                PerCosmeticColors.SetPreviewFromEntry(kv.Key, kv.Value);

        // Animators rebind to these preview specs in the CosmeticPlayerUpdateLocal vanilla calls right after CosmeticPreviewSet — a previously-hovered preset's animation doesn't keep running.
    }

    private static int FindMatchingPreset(MetaManager meta)
    {
        var preview = meta.cosmeticEquippedPreview;
        if (preview.Count == 0) return -1;

        // ONLY a hover that started on a preset button counts (hint from PresetHoverIndexTrackPatch). Deliberately NO set-equality fallback: assembling an outfit identical to a saved preset would wrongly paint its saved colours on hover.
        int hint = PerCosmeticColors.GetPendingHoverPresetIndex();
        if (hint < 0 || hint >= meta.cosmeticPresets.Count) return -1;

        var previewSet = new HashSet<int>(preview);
        var hintPreset = meta.cosmeticPresets[hint];
        if (hintPreset.Count != previewSet.Count) return -1;
        foreach (int idx in hintPreset)
            if (!previewSet.Contains(idx)) return -1;
        return hint;
    }
}

// TogglePreset resets type colours and wipes our overrides — after it finishes, clear the covered types' stores and restore the preset's saved state (palette / custom / per-slot / animation). A prefix captures whether this is a LOAD (preset non-empty) or SAVE (slot empty) so the postfix can skip the no-op save path.
[HarmonyPatch(typeof(MenuElementCosmeticPreset), nameof(MenuElementCosmeticPreset.TogglePreset))]
internal static class PresetLoadColorsPatch
{
    private static bool _wasLoad;

    // True while TogglePreset loads. The hover-leave re-bind checks this so it doesn't strip animators mid-load — the preset's specs only reach the live store in this Postfix's RestorePreset.
    internal static bool LoadInProgress { get; private set; }

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticPreset __instance)
    {
        var meta = MetaManager.instance;
        if (meta == null) { _wasLoad = false; return; }

        // Non-empty slot = LOAD; empty slot = SAVE (handled by CosmeticPresetSetColorsPatch — nothing to do here).
        _wasLoad = meta.cosmeticPresets[__instance.presetIndex].Count > 0
                || meta.colorPresets[__instance.presetIndex].Count > 0;
        LoadInProgress = _wasLoad && PerCosmeticColors.FeatureEnabled;
    }

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticPreset __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled || !_wasLoad) { LoadInProgress = false; return; }

        var meta = MetaManager.instance;
        if (meta == null) { LoadInProgress = false; return; }

        var presetColors = PerCosmeticColors.GetPresetColors(__instance.presetIndex);

        // Identify the CosmeticTypes covered by this preset.
        var coveredTypes = new HashSet<SemiFunc.CosmeticType>();
        foreach (int idx in meta.cosmeticPresets[__instance.presetIndex])
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset != null) coveredTypes.Add(asset.type);
        }

        // Wipe the previous outfit's per-cosmetic colour stores for each covered type before restoring the preset's own state.
        foreach (var type in coveredTypes)
            PerCosmeticColors.ClearAllForType(type, meta);

        // Base-mesh customs have no CosmeticAsset, so ClearAllForType never reaches them — clear them all; RestorePreset re-adds the loaded preset's own entries.
        if (PerCosmeticColors.ClearAllBaseMeshCustomColorsNoSave())
            PerCosmeticColors.SaveCustom();

        if (presetColors is { Count: > 0 })
            PerCosmeticColors.RestorePreset(__instance.presetIndex);

        LoadInProgress = false;

        // Re-trigger the local update: vanilla already ran it at the end of TogglePreset, but our stores were still empty for the preset's types at that point.
        meta.CosmeticPlayerUpdateLocal(_synced: false);
    }
}
