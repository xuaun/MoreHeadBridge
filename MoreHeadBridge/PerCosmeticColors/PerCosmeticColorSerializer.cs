using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MoreHeadBridge;

// Serializes/deserializes { assetId → colorIndex } to/from a single string.
// Format: whole-asset = {assetId}\x1F{colorIndex}\x1F; per-slot = {assetId}\x1E{btmIndex}\x1F{colorIndex}\x1F. \x1F = key/value sep (Sep), \x1E = assetId/slot sep (SlotKeySep) — ASCII control chars never in assetIds.
// Old senders produce only whole-asset entries; the deserialiser handles both.
internal static class PerCosmeticColorSerializer
{
    private const char Sep        = '\x1F'; // separates key–value pairs
    private const char SlotKeySep = '\x1E'; // separates assetId from btmIndex in slot keys

    // ── Whole-asset only (legacy / preset paths) ───────────────────────────────

    internal static string Serialize(IReadOnlyDictionary<string, int> colors)
    {
        if (colors.Count == 0) return "";

        var sb = new StringBuilder(colors.Count * 48);
        foreach (var kv in colors)
        {
            sb.Append(kv.Key);
            sb.Append(Sep);
            sb.Append(kv.Value);
            sb.Append(Sep);
        }
        return sb.ToString();
    }

    internal static Dictionary<string, int> Deserialize(string data)
    {
        var result = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(data)) return result;

        var parts = data.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            // Skip slot entries from new senders (they contain SlotKeySep).
            if (parts[i].IndexOf(SlotKeySep) >= 0) continue;
            if (int.TryParse(parts[i + 1], out int colorIdx))
                result[parts[i]] = colorIdx;
        }
        return result;
    }

    // ── With-slots (RPC sync path) ─────────────────────────────────────────────

    /// Serialises both whole-asset and per-slot colour overrides into a single string.
    internal static string SerializeWithSlots(
        IReadOnlyDictionary<string, int> colors,
        IReadOnlyDictionary<string, Dictionary<int, int>> slotColors)
    {
        if (colors.Count == 0 && slotColors.Count == 0) return "";

        var sb = new StringBuilder();

        foreach (var kv in colors)
        {
            sb.Append(kv.Key); sb.Append(Sep); sb.Append(kv.Value); sb.Append(Sep);
        }

        foreach (var assetKv in slotColors)
        foreach (var slotKv in assetKv.Value)
        {
            sb.Append(assetKv.Key);
            sb.Append(SlotKeySep);
            sb.Append(slotKv.Key);   // btmIndex
            sb.Append(Sep);
            sb.Append(slotKv.Value); // colorIndex
            sb.Append(Sep);
        }

        return sb.ToString();
    }

    /// Deserialises a combined string into separate whole-asset and per-slot dictionaries.
    internal static void DeserializeWithSlots(
        string data,
        out Dictionary<string, int> colors,
        out Dictionary<string, Dictionary<int, int>> slotColors)
    {
        colors     = new Dictionary<string, int>();
        slotColors = new Dictionary<string, Dictionary<int, int>>();
        if (string.IsNullOrEmpty(data)) return;

        var parts = data.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string key = parts[i];
            if (!int.TryParse(parts[i + 1], out int colorIdx)) continue;

            int sepIdx = key.IndexOf(SlotKeySep);
            if (sepIdx < 0)
            {
                // Whole-asset entry
                colors[key] = colorIdx;
            }
            else
            {
                // Slot entry: {assetId}\x1E{btmIndex}
                string assetId  = key.Substring(0, sepIdx);
                string slotPart = key.Substring(sepIdx + 1);
                if (!int.TryParse(slotPart, out int slot)) continue;

                if (!slotColors.TryGetValue(assetId, out var slots))
                    slotColors[assetId] = slots = new Dictionary<int, int>();
                slots[slot] = colorIdx;
            }
        }
    }

    // ── Colour animations (separate RPC channel) ────────────────────────────────
    // Animations travel on their own network event as JSON — self-contained, independent of the colour/slot string format.

    internal static string SerializeAnimations(IReadOnlyDictionary<string, ColorAnimation> animations)
        => animations.Count == 0 ? "" : JsonConvert.SerializeObject(animations);

    internal static Dictionary<string, ColorAnimation> DeserializeAnimations(string data)
    {
        if (string.IsNullOrEmpty(data)) return new Dictionary<string, ColorAnimation>();
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, ColorAnimation>>(data)
                   ?? new Dictionary<string, ColorAnimation>();
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: animation deserialise failed: {ex.Message}");
            return new Dictionary<string, ColorAnimation>();
        }
    }

    // ── Custom RGB colours (separate network channel) ───────────────────────────
    // Sent as compact JSON: { "w": { assetId: [r,g,b] }, "s": { assetId: { "slot": [r,g,b] } } } — short keys reduce wire size.

    internal static string SerializeCustomColors(
        IReadOnlyDictionary<string, UnityEngine.Color> whole,
        IReadOnlyDictionary<string, Dictionary<int, UnityEngine.Color>> perSlot)
    {
        if (whole.Count == 0 && perSlot.Count == 0) return "";

        var wDto = new Dictionary<string, float[]>(whole.Count);
        foreach (var kv in whole)
            wDto[kv.Key] = new[] { kv.Value.r, kv.Value.g, kv.Value.b };

        // Per-slot: int keys → string keys (JSON only allows string keys).
        var sDto = new Dictionary<string, Dictionary<string, float[]>>(perSlot.Count);
        foreach (var assetKv in perSlot)
        {
            var slots = new Dictionary<string, float[]>(assetKv.Value.Count);
            foreach (var slotKv in assetKv.Value)
                slots[slotKv.Key.ToString()] = new[] { slotKv.Value.r, slotKv.Value.g, slotKv.Value.b };
            sDto[assetKv.Key] = slots;
        }

        return JsonConvert.SerializeObject(new CustomColorNetworkDto { W = wDto, S = sDto });
    }

    internal static void DeserializeCustomColors(string data,
        out Dictionary<string, UnityEngine.Color> whole,
        out Dictionary<string, Dictionary<int, UnityEngine.Color>> perSlot)
    {
        whole   = new Dictionary<string, UnityEngine.Color>();
        perSlot = new Dictionary<string, Dictionary<int, UnityEngine.Color>>();
        if (string.IsNullOrEmpty(data)) return;
        try
        {
            var dto = JsonConvert.DeserializeObject<CustomColorNetworkDto>(data);
            if (dto == null) return;

            if (dto.W != null)
                foreach (var kv in dto.W)
                    if (kv.Value is { Length: >= 3 })
                        whole[kv.Key] = new UnityEngine.Color(kv.Value[0], kv.Value[1], kv.Value[2]);

            if (dto.S != null)
                foreach (var assetKv in dto.S)
                {
                    if (assetKv.Value == null) continue;
                    var slots = new Dictionary<int, UnityEngine.Color>();
                    foreach (var slotKv in assetKv.Value)
                        if (int.TryParse(slotKv.Key, out int slot) && slotKv.Value is { Length: >= 3 })
                            slots[slot] = new UnityEngine.Color(slotKv.Value[0], slotKv.Value[1], slotKv.Value[2]);
                    if (slots.Count > 0) perSlot[assetKv.Key] = slots;
                }
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: custom colours deserialise failed: {ex.Message}");
        }
    }

    private sealed class CustomColorNetworkDto
    {
        [JsonProperty("w")] public Dictionary<string, float[]>? W { get; set; }
        [JsonProperty("s")] public Dictionary<string, Dictionary<string, float[]>>? S { get; set; }
    }

    // ── Per-slot animations (separate network channel) ──────────────────────────
    // Format: { assetId: { slotIndex: ColorAnimation } }. JSON.NET serialises int dict keys as strings and restores them on deserialise.

    internal static string SerializeSlotAnimations(
        IReadOnlyDictionary<string, Dictionary<int, ColorAnimation>> slotAnims)
        => slotAnims.Count == 0 ? "" : JsonConvert.SerializeObject(slotAnims);

    internal static Dictionary<string, Dictionary<int, ColorAnimation>> DeserializeSlotAnimations(string data)
    {
        if (string.IsNullOrEmpty(data)) return new Dictionary<string, Dictionary<int, ColorAnimation>>();
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, ColorAnimation>>>(data)
                   ?? new Dictionary<string, Dictionary<int, ColorAnimation>>();
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PerCosmeticColors: slot animation deserialise failed: {ex.Message}");
            return new Dictionary<string, Dictionary<int, ColorAnimation>>();
        }
    }
}
