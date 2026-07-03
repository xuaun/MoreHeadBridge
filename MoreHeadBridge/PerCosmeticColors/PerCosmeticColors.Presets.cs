using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

// One cosmetic's full per-cosmetic colour state in a preset slot: whole-asset palette index / custom RGB, per-slot palette / custom, and an animation spec. Any field may be null.
internal sealed class PresetColorEntry
{
    [JsonProperty("i")]  public int? Index;                         // whole-asset palette index / -1 sentinel
    [JsonProperty("c")]  public float[]? Custom;                    // whole-asset custom [r,g,b]
    [JsonProperty("si")] public Dictionary<int, int>? SlotIndex;    // per-slot palette
    [JsonProperty("sc")] public Dictionary<int, float[]>? SlotCustom; // per-slot custom [r,g,b]
    [JsonProperty("an")] public ColorAnimation? Anim;              // whole-asset animation spec
    [JsonProperty("sa")] public Dictionary<int, ColorAnimation>? SlotAnim; // per-slot animation specs
}

// Partial class — preset-colour persistence: save → snapshot every store for the preset's cosmetics; hover → preview the stored colours; load → clear covered types, then restore.
internal static partial class PerCosmeticColors
{
    private static readonly string PresetSavePath = BridgePaths.Of("PresetColors.json");

    // presetIndex → { assetId → full colour state }
    private static Dictionary<int, Dictionary<string, PresetColorEntry>> _presetColors = new();

    // True while a preset hover preview is active AND the preview maps are populated from stored preset colours — makes ApplyOverrides prefer the preview over session colours.
    private static bool _presetPreviewActive;

    internal static void SetPresetPreviewActive(bool value)
        => _presetPreviewActive = value;

    private static int _pendingHoverPresetIndex = -1;
    internal static void NotifyPresetHoverStart(int presetIndex) => _pendingHoverPresetIndex = presetIndex;
    internal static int GetPendingHoverPresetIndex() => _pendingHoverPresetIndex;

    private static float[] Rgb(Color c) => new[] { c.r, c.g, c.b };
    private static Color FromRgb(float[] a) => new(a[0], a[1], a[2]);

    /// Snapshots the full per-cosmetic colour state for the cosmetics in this preset slot.
    internal static void SavePreset(int presetIndex, IList<int> cosmeticIndices)
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        var map = new Dictionary<string, PresetColorEntry>();
        foreach (int idx in cosmeticIndices)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null) continue;
            string id = asset.assetId;

            var entry = new PresetColorEntry();
            bool any = false;

            if (_colors.TryGetValue(id, out int ci))             { entry.Index = ci; any = true; }
            if (_customColors.TryGetValue(id, out var cc))        { entry.Custom = Rgb(cc); any = true; }
            if (_slotColors.TryGetValue(id, out var si) && si.Count > 0)
                                                                 { entry.SlotIndex = new(si); any = true; }
            if (_customSlotColors.TryGetValue(id, out var sc) && sc.Count > 0)
            {
                entry.SlotCustom = new();
                foreach (var s in sc) entry.SlotCustom[s.Key] = Rgb(s.Value);
                any = true;
            }
            if (_animations.TryGetValue(id, out var an))          { entry.Anim = an; any = true; }
            if (GetSlotAnimations(id) is { Count: > 0 } sa)
            {
                entry.SlotAnim = new();
                foreach (var s in sa) entry.SlotAnim[s.Key] = s.Value;
                any = true;
            }

            if (any) map[id] = entry;
        }

        // Also save base mesh custom colours ("Default" mesh — no CosmeticAsset, stored under the synthetic "__base_{typeIndex}__" key set by the section "C" button).
        if (Plugin.EnableVanillaCustomColors.Value)
        {
            foreach (var kv in _customColors)
            {
                if (VanillaTintHelper.IsBaseMeshId(kv.Key) && !map.ContainsKey(kv.Key))
                    map[kv.Key] = new PresetColorEntry { Custom = Rgb(kv.Value) };
            }
        }

        if (map.Count > 0) _presetColors[presetIndex] = map;
        else _presetColors.Remove(presetIndex);

        SavePresets();
    }

    internal static IReadOnlyDictionary<string, PresetColorEntry>? GetPresetColors(int presetIndex)
        => _presetColors.TryGetValue(presetIndex, out var d) ? d : null;

    // Serialises a preset slot's colours into the same 4 transport blobs as the live sync — ships a RandomPreset mini's colours to other clients (they're not in the owner's live broadcast).
    internal static (string colorData, string animData, string customData, string slotAnimData)
        SerializePresetForBroadcast(int presetIndex)
    {
        var entries = GetPresetColors(presetIndex);
        if (entries == null) return ("", "", "", "");

        var colors      = new Dictionary<string, int>();
        var slotColors  = new Dictionary<string, Dictionary<int, int>>();
        var custom      = new Dictionary<string, Color>();
        var customSlots = new Dictionary<string, Dictionary<int, Color>>();
        var anims       = new Dictionary<string, ColorAnimation>();
        var slotAnims   = new Dictionary<string, Dictionary<int, ColorAnimation>>();

        foreach (var kv in entries)
        {
            var id = kv.Key; var e = kv.Value;
            if (e == null) continue;

            if (e.Custom is { Length: >= 3 })
                custom[id] = new Color(e.Custom[0], e.Custom[1], e.Custom[2]);
            else if (e.Index.HasValue)
                colors[id] = e.Index.Value;

            if (e.SlotIndex is { Count: > 0 })
                slotColors[id] = new Dictionary<int, int>(e.SlotIndex);

            if (e.SlotCustom is { Count: > 0 })
            {
                var sc = new Dictionary<int, Color>();
                foreach (var s in e.SlotCustom)
                    if (s.Value is { Length: >= 3 }) sc[s.Key] = new Color(s.Value[0], s.Value[1], s.Value[2]);
                if (sc.Count > 0) customSlots[id] = sc;
            }

            if (e.Anim != null) anims[id] = e.Anim;
            if (e.SlotAnim is { Count: > 0 })
                slotAnims[id] = new Dictionary<int, ColorAnimation>(e.SlotAnim);
        }

        string colorData = (colors.Count > 0 || slotColors.Count > 0)
            ? PerCosmeticColorSerializer.SerializeWithSlots(colors, slotColors) : "";
        string animData = Plugin.EnableBridgeColorAnimations.Value && anims.Count > 0
            ? PerCosmeticColorSerializer.SerializeAnimations(anims) : "";
        string customData = Plugin.EnableBridgeCustomColors.Value && (custom.Count > 0 || customSlots.Count > 0)
            ? PerCosmeticColorSerializer.SerializeCustomColors(custom, customSlots) : "";
        string slotAnimData = Plugin.EnableBridgeColorAnimations.Value && slotAnims.Count > 0
            ? PerCosmeticColorSerializer.SerializeSlotAnimations(slotAnims) : "";

        return (colorData, animData, customData, slotAnimData);
    }

    internal static void DeletePresetColors(int presetIndex)
    {
        if (_presetColors.Remove(presetIndex))
            SavePresets();
    }

    /// Clears every colour store for the currently-equipped cosmetics of the given type.
    /// Used on preset load to wipe the previous outfit's overrides before restoring the preset.
    internal static void ClearAllForType(SemiFunc.CosmeticType type, MetaManager meta)
    {
        bool c = false, s = false, cu = false, cs = false, an = false, sa = false;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || asset.type != type) continue;
            string id = asset.assetId;
            c  |= _colors.Remove(id);
            s  |= RemoveSlotsNoSave(id);
            cu |= RemoveCustomColorNoSave(id);
            cs |= RemoveCustomSlotsNoSave(id);
            an |= RemoveAnimationNoSave(id);
            sa |= RemoveSlotAnimationsNoSave(id);
        }
        // Also clear the base mesh custom colour for this type — the "Default" mesh has no CosmeticAsset so it is never in cosmeticEquipped and must be cleared separately.
        string baseId = VanillaTintHelper.BaseMeshAssetId((int)type);
        cu |= RemoveCustomColorNoSave(baseId);

        if (c) Save();
        if (s) SaveSlots();
        if (cu) SaveCustom();
        if (cs) SaveCustomSlots();
        if (sa) SaveSlotAnimations();
        if (an) SaveAnimations();
    }

    /// Restores a preset's stored per-cosmetic colour state into the live stores (then persists).
    internal static void RestorePreset(int presetIndex)
    {
        if (!_presetColors.TryGetValue(presetIndex, out var entries)) return;

        foreach (var kv in entries)
        {
            string id = kv.Key;
            var e = kv.Value;
            if (e.Index.HasValue) _colors[id] = e.Index.Value;
            if (e.Custom is { Length: >= 3 }) _customColors[id] = FromRgb(e.Custom);
            if (e.SlotIndex is { Count: > 0 }) _slotColors[id] = new(e.SlotIndex);
            if (e.SlotCustom is { Count: > 0 })
            {
                var sc = new Dictionary<int, Color>();
                foreach (var s in e.SlotCustom)
                    if (s.Value is { Length: >= 3 }) sc[s.Key] = FromRgb(s.Value);
                _customSlotColors[id] = sc;
            }
            if (e.Anim != null) _animations[id] = e.Anim;
            if (e.SlotAnim is { Count: > 0 })
            {
                var sa = new Dictionary<int, ColorAnimation>();
                foreach (var s in e.SlotAnim) sa[s.Key] = s.Value;
                _slotAnimations[id] = sa;
            }
        }

        Save(); SaveSlots(); SaveCustom(); SaveCustomSlots(); SaveAnimations(); SaveSlotAnimations();
    }

    /// True when the live per-cosmetic colour state equals what this preset would restore — lets the patch report "not equipped" when colours differ (vanilla's check ignores per-cosmetic colours). Allocation-free: called every frame.
    internal static bool PresetMatchesCurrent(int presetIndex)
    {
        var meta = MetaManager.instance;
        if (meta == null) return true;
        if (presetIndex < 0 || presetIndex >= meta.cosmeticPresets.Count) return true;

        var stored = GetPresetColors(presetIndex); // may be null when the preset has no colours saved
        foreach (int idx in meta.cosmeticPresets[presetIndex])
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null) continue;

            PresetColorEntry? e = null;
            stored?.TryGetValue(asset.assetId, out e);
            if (!CurrentMatchesEntry(asset.assetId, e)) return false;
        }
        // Also compare base mesh custom colours stored under synthetic "__base_{N}__" keys.
        if (Plugin.EnableVanillaCustomColors.Value && stored != null)
        {
            foreach (var kv in stored)
            {
                if (!VanillaTintHelper.IsBaseMeshId(kv.Key)) continue;
                if (!CurrentMatchesEntry(kv.Key, kv.Value)) return false;
            }
            // If stored has no base mesh entries, the live state must also have none (a base mesh colour set since the preset was saved should mark it non-matching).
            foreach (var kv in _customColors)
            {
                if (!VanillaTintHelper.IsBaseMeshId(kv.Key)) continue;
                if (!stored.ContainsKey(kv.Key)) return false;
            }
        }
        return true;
    }

    // True when the live colour stores for one asset equal the preset entry (null = "no override").
    private static bool CurrentMatchesEntry(string id, PresetColorEntry? e)
    {
        bool hasIdx = _colors.TryGetValue(id, out int idx);
        if (hasIdx != e?.Index.HasValue) return false;
        if (hasIdx && idx != e!.Index!.Value) return false;

        bool hasCustom = _customColors.TryGetValue(id, out var cc);
        bool eCustom = e?.Custom is { Length: >= 3 };
        if (hasCustom != eCustom) return false;
        if (hasCustom && !ColorsApproxEqual(cc, FromRgb(e!.Custom!))) return false;

        return SlotIndexMatches(id, e?.SlotIndex)
            && SlotCustomMatches(id, e?.SlotCustom)
            && AnimMatches(id, e?.Anim)
            && SlotAnimMatches(id, e?.SlotAnim);
    }

    private static bool SlotIndexMatches(string id, Dictionary<int, int>? expected)
    {
        _slotColors.TryGetValue(id, out var cur);
        if ((cur?.Count ?? 0) != (expected?.Count ?? 0)) return false;
        if (cur == null) return true;
        foreach (var kv in cur)
            if (expected == null || !expected.TryGetValue(kv.Key, out int v) || v != kv.Value) return false;
        return true;
    }

    private static bool SlotCustomMatches(string id, Dictionary<int, float[]>? expected)
    {
        _customSlotColors.TryGetValue(id, out var cur);
        if ((cur?.Count ?? 0) != (expected?.Count ?? 0)) return false;
        if (cur == null) return true;
        foreach (var kv in cur)
        {
            if (expected == null || !expected.TryGetValue(kv.Key, out var arr) || arr is not { Length: >= 3 })
                return false;
            if (!ColorsApproxEqual(kv.Value, FromRgb(arr))) return false;
        }
        return true;
    }

    private static bool AnimMatches(string id, ColorAnimation? expected)
    {
        bool has = _animations.TryGetValue(id, out var cur);
        if (has != (expected != null)) return false;
        return !has || AnimEquals(cur!, expected!);
    }

    private static bool SlotAnimMatches(string id, Dictionary<int, ColorAnimation>? expected)
    {
        _slotAnimations.TryGetValue(id, out var cur);
        if ((cur?.Count ?? 0) != (expected?.Count ?? 0)) return false;
        if (cur == null) return true;
        foreach (var kv in cur)
            if (expected == null || !expected.TryGetValue(kv.Key, out var e) || !AnimEquals(kv.Value, e))
                return false;
        return true;
    }

    private static bool AnimEquals(ColorAnimation a, ColorAnimation b)
    {
        if (a.Mode != b.Mode || a.Dir != b.Dir) return false;
        if (Mathf.Abs(a.SecondsPerStep - b.SecondsPerStep) > 0.001f) return false;
        int n = a.Palette?.Count ?? 0;
        if (n != (b.Palette?.Count ?? 0)) return false;
        for (int i = 0; i < n; i++)
            if (a.Palette![i] != b.Palette![i]) return false;
        return true;
    }

    private static bool ColorsApproxEqual(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

    internal static void LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetSavePath)) return;
            string json = File.ReadAllText(PresetSavePath);
            try
            {
                _presetColors = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, PresetColorEntry>>>(json) ?? new();
            }
            catch
            {
                // Backward compat: old format was presetIndex → { assetId → colorIndex }.
                var old = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, int>>>(json);
                _presetColors = new();
                if (old != null)
                    foreach (var p in old)
                    {
                        var map = new Dictionary<string, PresetColorEntry>();
                        foreach (var a in p.Value)
                            map[a.Key] = new PresetColorEntry { Index = a.Value };
                        _presetColors[p.Key] = map;
                    }
            }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: preset colors load failed: {ex.Message}");
            _presetColors = new();
        }
    }

    private static Task _lastPresetWrite = Task.CompletedTask;

    private static void SavePresets()
    {
        string json = JsonConvert.SerializeObject(_presetColors);
        _lastPresetWrite = AtomicJson.QueueWrite(_lastPresetWrite, PresetSavePath, json, "PerCosmeticColors: preset colors save failed");
    }
}
