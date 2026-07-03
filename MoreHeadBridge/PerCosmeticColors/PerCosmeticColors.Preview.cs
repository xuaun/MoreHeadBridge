using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Partial class — UI-state and hover-preview half of PerCosmeticColors.
internal static partial class PerCosmeticColors
{
    // The cosmetic whose per-cosmetic color button was clicked; null when the section (type-wide) color button was used instead.
    internal static CosmeticAsset? PendingAsset { get; set; }

    // Temporary per-cosmetic colors for the hover-preview frame only. Never persisted.
    private static Dictionary<string, int> _previewOverrides = new();

    // Temporary per-cosmetic CUSTOM RGB for the hover-preview frame only (preset preview).
    private static Dictionary<string, Color> _previewCustom = new();

    // Temporary per-SLOT colours for the hover-preview frame only (preset preview): palette index and custom RGB keyed by flat material slot, so multi-slot cosmetics preview each slot's colour.
    private static Dictionary<string, Dictionary<int, int>> _previewSlotOverrides = new();
    private static Dictionary<string, Dictionary<int, Color>> _previewSlotCustom = new();

    // Temporary animation specs for the preset hover preview — ColorAnimatorRefresher consults these so animated cosmetics animate during hover.
    private static Dictionary<string, ColorAnimation> _previewAnimations = new();
    private static Dictionary<string, Dictionary<int, ColorAnimation>> _previewSlotAnimations = new();

    // AssetIds with an EXPLICIT entry in the hovered preset. Cosmetics NOT here fall back to their LIVE state during hover — an animated cosmetic the preset never overrode keeps animating.
    private static readonly HashSet<string> _previewEntryExists = new();

    // Saved type-color temporarily replaced by a per-cosmetic value so the color page highlights the right button.
    private static int _savedTypeColor = -1;
    private static int _savedTypeIndex = -1;

    /// Opening the colour page for a cosmetic: temporarily write its override colour into colorsEquipped[type] so the picker highlights it.
    /// Bridge with NO override → colorsEquipped[type] = -1: no vanilla button matches, and BridgeOriginalColorButton places the indicator on Original (initiallySelected).
    internal static void TemporarilyShowForColorPage(CosmeticAsset asset)
    {
        _savedTypeIndex = -1;
        if (MetaManager.instance == null) return;

        int typeIdx = (int)asset.type;
        if (typeIdx < 0 || typeIdx >= MetaManager.instance.colorsEquipped.Length) return;

        _savedTypeColor = MetaManager.instance.colorsEquipped[typeIdx];
        _savedTypeIndex = typeIdx;

        if (!_colors.TryGetValue(asset.assetId, out int colorIdx))
        {
            // No override stored. Bridge: suppress the vanilla indicator (sentinel -1 matches nothing) so BridgeOriginalColorButton can place it on the Original button.
            if (BridgeIds.IsBridgeAsset(asset))
                MetaManager.instance.colorsEquipped[typeIdx] = -1;
            // Vanilla cosmetics: leave colorsEquipped unchanged (the current type color is the right indicator position).
            return;
        }

        MetaManager.instance.colorsEquipped[typeIdx] = colorIdx;

        // Give the other equipped cosmetics of the same type a preview entry so ApplyOverrides can restore their visible color while this page is open.
        foreach (int idx in MetaManager.instance.cosmeticEquipped)
        {
            if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;
            var other = MetaManager.instance.cosmeticAssets[idx];
            if (other == null || other.type != asset.type) continue;
            if (other.assetId == asset.assetId) continue; // the cosmetic being painted
            if (_colors.ContainsKey(other.assetId)) continue; // ApplyOverrides handles it via _colors
            // Bridge cosmetics without a per-cosmetic colour show their original material colour — use the sentinel so ApplyOverrides calls RestoreOriginalColor, not the (unrelated) type colour.
            _previewOverrides[other.assetId] = BridgeIds.IsBridgeAsset(other)
                ? OriginalColorSentinel
                : _savedTypeColor;
        }
    }

    internal static void RestoreTypeColor()
    {
        if (_savedTypeIndex < 0 || MetaManager.instance == null) return;
        if (_savedTypeIndex < MetaManager.instance.colorsEquipped.Length)
            MetaManager.instance.colorsEquipped[_savedTypeIndex] = _savedTypeColor;
        _savedTypeIndex = -1;
        _savedTypeColor = -1;
        ClearPreviewOverrides();
    }

    internal static void SetPreview(string assetId, int colorIndex)
        => _previewOverrides[assetId] = colorIndex;

    internal static bool HasPreviewOverride(string assetId)
        => _previewOverrides.ContainsKey(assetId);

    internal static void ClearPreviewOverrides()
    {
        _previewOverrides.Clear();
        _previewCustom.Clear();
        _previewSlotOverrides.Clear();
        _previewSlotCustom.Clear();
        _previewAnimations.Clear();
        _previewSlotAnimations.Clear();
        _previewEntryExists.Clear();
        _presetPreviewActive = false;
    }

    // Clears ONLY the preset-preview maps + flag — KEEPS _previewOverrides (which also holds the type-colour pins of an individual-cosmetic hover). For when a cosmetic hover begins while a stale preset preview is active: dropping just the preset state lets the LIVE anim spec drive again (else an animated cosmetic freezes until the mouse leaves).
    internal static void ClearPresetPreviewOnly()
    {
        _previewCustom.Clear();
        _previewSlotOverrides.Clear();
        _previewSlotCustom.Clear();
        _previewAnimations.Clear();
        _previewSlotAnimations.Clear();
        _previewEntryExists.Clear();
        _presetPreviewActive = false;
    }

    // True when the hovered preset has an explicit entry for this cosmetic. Without one, the cosmetic uses its live state — it was un-overridden at save time, so that IS the preset's intent.
    internal static bool HasPreviewEntry(string assetId) => _previewEntryExists.Contains(assetId);

    // Per-slot preview lookups, mirroring the live per-slot stores during a preset hover.
    internal static bool TryGetPreviewSlotCustom(string assetId, int slot, out Color color)
    {
        color = default;
        return _previewSlotCustom.TryGetValue(assetId, out var slots) && slots.TryGetValue(slot, out color);
    }

    internal static bool TryGetPreviewSlotOverride(string assetId, int slot, out int colorIndex)
    {
        colorIndex = 0;
        return _previewSlotOverrides.TryGetValue(assetId, out var slots) && slots.TryGetValue(slot, out colorIndex);
    }

    // Whether a preset hover preview is currently driving the maps — lets ColorAnimatorRefresher pick the preview animation specs over the live store while a preset is hovered.
    internal static bool PresetPreviewActive => _presetPreviewActive;

    // Anim spec during a preset hover: explicit entry → the preset's spec (may be empty = stop, show stored colour); no entry → the LIVE spec, so an animated cosmetic keeps animating.
    internal static AnimSet GetPreviewAnimSet(string assetId)
    {
        if (!_previewEntryExists.Contains(assetId))
            return GetAnimSet(assetId);   // fall back to live — preset doesn't override this cosmetic

        _previewAnimations.TryGetValue(assetId, out var whole);
        var perSlot = _previewSlotAnimations.TryGetValue(assetId, out var s) && s.Count > 0 ? s : null;
        return new AnimSet(whole, perSlot);
    }

    // ── Mini-Semibot preset colour context ───────────────────────────────────────────
    // True while a Mini-Semibot is dressed from a preset (RandomPreset): lets ApplyOverrides / RefreshLiveAnimators apply the preset's colours to the mini; outside it, they skip preset minis so the live store never bleeds in.
    internal static bool MiniPresetContextActive { get; private set; }

    // Drives the preview maps from a preset slot around <paramref name="dress"/>, then restores the prior preview state exactly — an in-progress player preset HOVER is never disturbed. Map instances are swapped, not deep-copied.
    internal static void RunWithPresetContext(int slot, System.Action dress)
    {
        if (dress == null) return;

        var o  = _previewOverrides;     var c  = _previewCustom;
        var so = _previewSlotOverrides; var sc = _previewSlotCustom;
        var an = _previewAnimations;    var sa = _previewSlotAnimations;
        var ee = new HashSet<string>(_previewEntryExists);   // readonly field → copy + restore by content
        bool prevActive = _presetPreviewActive;
        bool prevMini   = MiniPresetContextActive;

        _previewOverrides = new();     _previewCustom = new();
        _previewSlotOverrides = new(); _previewSlotCustom = new();
        _previewAnimations = new();    _previewSlotAnimations = new();
        _previewEntryExists.Clear();

        try
        {
            var entries = GetPresetColors(slot);
            if (entries != null)
                foreach (var kv in entries) SetPreviewFromEntry(kv.Key, kv.Value);
            // Active even with no entries → every cosmetic keeps its palette colour (no live custom bleed).
            SetPresetPreviewActive(true);
            MiniPresetContextActive = true;
            dress();
        }
        finally
        {
            _previewOverrides = o;     _previewCustom = c;
            _previewSlotOverrides = so; _previewSlotCustom = sc;
            _previewAnimations = an;    _previewSlotAnimations = sa;
            _previewEntryExists.Clear(); _previewEntryExists.UnionWith(ee);
            SetPresetPreviewActive(prevActive);
            MiniPresetContextActive = prevMini;
        }
    }

    // Populates the hover-preview maps from a stored preset entry (whole-asset colour + animations).
    internal static void SetPreviewFromEntry(string assetId, PresetColorEntry e)
    {
        _previewEntryExists.Add(assetId);   // mark as explicitly covered by this preset

        if (e.Anim != null)
            _previewAnimations[assetId] = e.Anim;
        if (e.SlotAnim is { Count: > 0 })
            _previewSlotAnimations[assetId] = new Dictionary<int, ColorAnimation>(e.SlotAnim);
        if (e.Custom is { Length: >= 3 })
            _previewCustom[assetId] = new Color(e.Custom[0], e.Custom[1], e.Custom[2]);
        else if (e.Index.HasValue)
            _previewOverrides[assetId] = e.Index.Value;

        if (e.SlotIndex is { Count: > 0 })
            _previewSlotOverrides[assetId] = new Dictionary<int, int>(e.SlotIndex);
        if (e.SlotCustom is { Count: > 0 })
        {
            var sc = new Dictionary<int, Color>();
            foreach (var s in e.SlotCustom)
                if (s.Value is { Length: >= 3 }) sc[s.Key] = new Color(s.Value[0], s.Value[1], s.Value[2]);
            _previewSlotCustom[assetId] = sc;
        }
    }
}
