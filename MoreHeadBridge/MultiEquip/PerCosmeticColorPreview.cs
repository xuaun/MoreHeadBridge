using System.Collections.Generic;

namespace MoreHeadBridge;

// UI-state and hover-preview half of PerCosmeticColors (partial class).
internal static partial class PerCosmeticColors
{
    // The cosmetic whose per-cosmetic color button was clicked. Null when the section
    // (type-wide) color button was used instead.
    internal static CosmeticAsset? PendingAsset { get; set; }

    // Temporary per-cosmetic colors for the hover-preview frame only. Never persisted.
    private static Dictionary<string, int> _previewOverrides = new();

    // Saved type-color that was temporarily replaced by a per-cosmetic value so the
    // color page highlights the right button.
    private static int _savedTypeColor = -1;
    private static int _savedTypeIndex = -1;

    /// When opening the color page for a specific cosmetic, temporarily writes that
    /// cosmetic's per-cosmetic override color into colorsEquipped[type] so the color
    /// picker highlights the currently active color. Does nothing if no override exists.
    internal static void TemporarilyShowForColorPage(CosmeticAsset asset)
    {
        _savedTypeIndex = -1;
        if (MetaManager.instance == null) return;
        if (!_colors.TryGetValue(asset.assetId, out int colorIdx)) return;

        int typeIdx = (int)asset.type;
        if (typeIdx < 0 || typeIdx >= MetaManager.instance.colorsEquipped.Length) return;

        _savedTypeColor = MetaManager.instance.colorsEquipped[typeIdx];
        _savedTypeIndex = typeIdx;
        MetaManager.instance.colorsEquipped[typeIdx] = colorIdx;

        // Give the other equipped cosmetics of the same type a preview entry so
        // ApplyOverrides can restore their visible color while this page is open.
        foreach (int idx in MetaManager.instance.cosmeticEquipped)
        {
            if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;
            var other = MetaManager.instance.cosmeticAssets[idx];
            if (other == null || other.type != asset.type) continue;
            if (other.assetId == asset.assetId) continue; // the cosmetic being painted
            if (_colors.ContainsKey(other.assetId)) continue; // ApplyOverrides handles it via _colors
            _previewOverrides[other.assetId] = _savedTypeColor;
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
        _presetPreviewActive = false;
    }
}
