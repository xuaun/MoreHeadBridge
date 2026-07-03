// Per-pack cosmetic-button border themes (RepoPride → pride-flag gradient, yoshicarry → per-colour Yoshi gradient, monstercosmetics → creepy-purple gradient, XuaunCosmetics/FortniteSemibot → solid coral). Vanilla's border is one RawImage coloured by GetRarityColor; buttons are pooled, so the theme is re-evaluated on every UpdateIcon.

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class BorderTheme
{
    // Bridge cosmetics from this plugin subfolder get the solid XuaunCosmetics border.
    private const string XuaunFolder = "Xuaun-XuaunCosmetics";
    private static readonly Color XuaunColor = new(1f, 0.39f, 0.52f);   // warm coral/pink

    // FortniteSemibot: assetId = "fortnitesemibot:…". Same coral border as XuaunCosmetics.
    private const string FortnitePrefix = "fortnitesemibot:";

    // RepoPride: assetId = "repopride:" + 3-letter type token (hto/hbo/bto/bbo/llo/lro/alo/aro/…) + flag token + rest. Strip the type, then match the flag by longest token.
    private const string RepoPridePrefix = "repopride:";

    // yoshicarry: assetId = "yoshicarry:" + 3-letter type token (bbm/llm/hat/…) + colour token + "yoshi". Strip the type, then match the colour by longest token.
    private const string YoshiPrefix = "yoshicarry:";

    // repomonsterscosmetics: assetId = "repomonsterscosmetics:…". Single "creepy purple" gradient for the whole pack.
    private const string MonsterPrefix = "repomonsterscosmetics:";

    private static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // Flag → ordered colour stops (top → bottom) for a smooth gradient. Widely-used flag palettes.
    private static readonly Dictionary<string, Color[]> Flags = new(StringComparer.Ordinal)
    {
        ["pride"]    = new[] { C(228, 3, 3), C(255, 140, 0), C(255, 237, 0), C(0, 128, 38), C(0, 77, 255), C(117, 7, 135) },
        ["newpride"] = new[] { C(0, 0, 0), C(97, 57, 21), C(91, 206, 250), C(245, 169, 184), C(255, 255, 255),
                               C(228, 3, 3), C(255, 140, 0), C(255, 237, 0), C(0, 128, 38), C(0, 77, 255), C(117, 7, 135) },
        ["trans"]    = new[] { C(91, 206, 250), C(245, 169, 184), C(255, 255, 255), C(245, 169, 184), C(91, 206, 250) },
        ["bi"]       = new[] { C(214, 2, 112), C(214, 2, 112), C(155, 79, 150), C(0, 56, 168), C(0, 56, 168) },
        ["pan"]      = new[] { C(255, 33, 140), C(255, 216, 0), C(33, 177, 255) },
        ["lesbian"]  = new[] { C(213, 45, 0), C(255, 154, 86), C(255, 255, 255), C(211, 98, 164), C(163, 2, 98) },
        ["enby"]     = new[] { C(252, 244, 52), C(255, 255, 255), C(156, 89, 209), C(44, 44, 44) },
        ["ace"]      = new[] { C(0, 0, 0), C(163, 163, 163), C(255, 255, 255), C(128, 0, 128) },
        ["aro"]      = new[] { C(61, 165, 66), C(167, 211, 121), C(255, 255, 255), C(169, 169, 169), C(0, 0, 0) },
        ["agender"]  = new[] { C(0, 0, 0), C(185, 185, 185), C(255, 255, 255), C(184, 244, 178), C(255, 255, 255), C(185, 185, 185), C(0, 0, 0) },
        ["intersex"] = new[] { C(255, 216, 0), C(122, 0, 172), C(255, 216, 0) },
    };

    // Flag tokens sorted longest-first so "newpride" wins over "pride", "agender" over "aro", etc.
    private static readonly string[] FlagsByLength = BuildOrder(Flags.Keys);

    // yoshicarry colour token → ordered stops (top → bottom).
    private static readonly Dictionary<string, Color[]> YoshiColors = new(StringComparer.Ordinal)
    {
        ["green"]  = new[] { C(110, 185, 44), C(232, 91, 4), C(227, 2, 15), C(110, 185, 44), C(224, 92, 3),    C(244, 217, 10) },
        ["blue"]   = new[] { C(1, 168, 244),  C(232, 91, 4), C(227, 2, 15), C(1, 168, 244),  C(184, 61, 186),  C(244, 217, 10) },
        ["yellow"] = new[] { C(255, 243, 1),  C(232, 91, 4), C(227, 2, 15), C(255, 243, 1),  C(14, 210, 68),   C(244, 217, 10) },
        ["red"]    = new[] { C(236, 27, 36),  C(232, 91, 4), C(227, 2, 15), C(236, 27, 36),  C(114, 120, 207), C(244, 217, 10) },
        ["custom"] = new[] { C(231, 231, 231),C(232, 91, 4), C(227, 2, 15), C(231, 231, 231),C(1, 168, 244),   C(244, 217, 10) },
    };

    // Per-stop weights per Yoshi colour (height of each band ∝ weight, top → bottom). null = equal spacing.
    private static readonly float[] YoshiWeight = { 4, 2, 3, 2, 3, 1 };
    private static readonly Dictionary<string, float[]?> YoshiWeights = new(StringComparer.Ordinal)
    {
        ["green"]  = YoshiWeight,
        ["blue"]   = YoshiWeight,
        ["yellow"] = YoshiWeight,
        ["red"]    = YoshiWeight,
        ["custom"] = YoshiWeight,
    };

    // Colour tokens sorted longest-first so "custom"/"yellow" win over "red"/"blue" when matching the prefix after the type token.
    private static readonly string[] YoshiByLength = BuildOrder(YoshiColors.Keys);

    // monstercosmetics: single creepy-purple gradient (option F).
    private static readonly Color[] MonsterStops = { C(15, 5, 25), C(90, 30, 140), C(180, 40, 160) };
    private static readonly float[]? MonsterWeights = null;   // equal spacing — fill in to tune

    private static string[] BuildOrder(IEnumerable<string> tokens)
    {
        var keys = new List<string>(tokens);
        keys.Sort((a, b) => b.Length - a.Length);
        return keys.ToArray();
    }

    internal readonly struct Theme
    {
        internal readonly Color[]? Gradient;   // non-null → smooth gradient (use Key for the cached texture)
        internal readonly float[]? Weights;    // optional per-stop weights (band height ∝ weight); null → equal spacing
        internal readonly string Key;
        internal readonly Color Solid;         // used when Gradient is null
        internal readonly bool HasSolid;

        internal Theme(string key, Color[] gradient, float[]? weights = null) { Key = key; Gradient = gradient; Weights = weights; Solid = default; HasSolid = false; }
        internal Theme(Color solid) { Key = ""; Gradient = null; Weights = null; Solid = solid; HasSolid = true; }
    }

    internal static bool TryResolve(CosmeticAsset? asset, out Theme theme)
    {
        theme = default;
        if (asset == null) return false;

        // RepoPride gradient replaces the MODDED purple highlight, so it's gated by the exact same condition (HighlightModdedCosmetics + per-cosmetic IsModded).
        string id = asset.assetId ?? "";
        if (id.StartsWith(RepoPridePrefix, StringComparison.OrdinalIgnoreCase)
            && CustomizerStore.IsNonBridgeModdedForAsset(asset))
        {
            string rest = id.Substring(RepoPridePrefix.Length);
            if (rest.Length > 3)
            {
                string afterType = rest.Substring(3);   // strip the 3-letter type token
                foreach (var flag in FlagsByLength)
                    if (afterType.StartsWith(flag, StringComparison.OrdinalIgnoreCase))
                    {
                        theme = new Theme(flag, Flags[flag]);
                        return true;
                    }
            }
        }

        // yoshicarry gradient is per colour token, gated like RepoPride (HighlightModdedCosmetics + per-cosmetic IsModded).
        if (id.StartsWith(YoshiPrefix, StringComparison.OrdinalIgnoreCase)
            && CustomizerStore.IsNonBridgeModdedForAsset(asset))
        {
            string rest = id.Substring(YoshiPrefix.Length);
            if (rest.Length > 3)
            {
                string afterType = rest.Substring(3);   // strip the 3-letter type token
                foreach (var color in YoshiByLength)
                    if (afterType.StartsWith(color, StringComparison.OrdinalIgnoreCase))
                    {
                        theme = new Theme("yoshi-" + color, YoshiColors[color], YoshiWeights[color]);
                        return true;
                    }
            }
        }

        // monstercosmetics pack → single creepy-purple gradient; non-bridge modded, gated like the others.
        if (id.StartsWith(MonsterPrefix, StringComparison.OrdinalIgnoreCase)
            && CustomizerStore.IsNonBridgeModdedForAsset(asset))
        {
            theme = new Theme("monster", MonsterStops, MonsterWeights);
            return true;
        }

        // XuaunCosmetics pack → solid coral, only when the BRIDGE highlight border would show for it (global HighlightBridgeCosmetics + its per-cosmetic IsModded override).
        if (HhhCosmeticLoader.IsFromFolder(asset, XuaunFolder)
            && CustomizerStore.IsModdedForAsset(asset))
        {
            theme = new Theme(XuaunColor);
            return true;
        }

        // FortniteSemibot pack → same solid coral; non-bridge modded, so gated like the RepoPride gradient (HighlightModdedCosmetics + per-cosmetic IsModded).
        if (id.StartsWith(FortnitePrefix, StringComparison.OrdinalIgnoreCase)
            && CustomizerStore.IsNonBridgeModdedForAsset(asset))
        {
            theme = new Theme(XuaunColor);
            return true;
        }
        return false;
    }

    // ── Gradient texture cache (one small vertical texture per flag) ──────────
    private static readonly Dictionary<string, Texture2D> _gradientCache = new(StringComparer.Ordinal);

    internal static Texture2D GradientTexture(string key, Color[] stops, float[]? weights = null)
    {
        if (_gradientCache.TryGetValue(key, out var tex) && tex != null) return tex;

        const int h = 128;
        tex = new Texture2D(1, h, TextureFormat.RGBA32, mipChain: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        // Stop positions (0..1). With per-stop weights, each stop sits at the centre of a band sized ∝ its weight; otherwise stops are spaced evenly (identical to the old even-lerp).
        var pos = new float[stops.Length];
        if (weights != null && weights.Length == stops.Length)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(weights[i], 0f);
            if (total <= 0f) total = 1f;
            float acc = 0f;
            for (int i = 0; i < stops.Length; i++)
            {
                float w = Mathf.Max(weights[i], 0f);
                pos[i] = (acc + w * 0.5f) / total;
                acc += w;
            }
        }
        else
        {
            for (int i = 0; i < stops.Length; i++) pos[i] = stops.Length > 1 ? (float)i / (stops.Length - 1) : 0f;
        }

        for (int y = 0; y < h; y++)
        {
            float p = 1f - (float)y / (h - 1);   // texture y=0 is the bottom; flip so stops[0] is the visual top
            Color c;
            if (p <= pos[0]) c = stops[0];
            else if (p >= pos[stops.Length - 1]) c = stops[stops.Length - 1];
            else
            {
                int i = 0;
                while (i < stops.Length - 1 && p > pos[i + 1]) i++;
                float span = pos[i + 1] - pos[i];
                float f = span > 0f ? (p - pos[i]) / span : 0f;
                c = Color.Lerp(stops[i], stops[i + 1], f);
            }
            tex.SetPixel(0, y, c);
        }
        tex.Apply();
        _gradientCache[key] = tex;
        return tex;
    }
}

// Swaps only the border TEXTURE (no per-frame colour writes → no feedback loop): gradient packs get white from GetRarityColor so vanilla's per-state colour dims/brightens the texture. Restores the original texture when a pooled button later shows a non-gradient cosmetic.
internal sealed class BridgeBorderTheme : MonoBehaviour
{
    private RawImage? _border;
    private Texture? _origTex;
    private bool _captured;

    internal void ConfigureGradient(RawImage border, string key, Color[] gradient, float[]? weights)
    {
        _border = border;
        if (!_captured) { _origTex = border.texture; _captured = true; }   // remember the default border
        var tex = BorderTheme.GradientTexture(key, gradient, weights);
        if (border.texture != tex) border.texture = tex;
    }

    internal void Deactivate()
    {
        if (_border != null && _captured && _border.texture != _origTex)
            _border.texture = _origTex;   // restore the default border
    }
}

// Re-evaluates the border theme every time a cosmetic button refreshes its icon (covers pooling/scroll). Only gradient themes need the texture component; solid themes are fully handled by GetRarityColor.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "UpdateIcon")]
internal static class CosmeticBorderThemePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticButton __instance)
    {
        var border = __instance.bgBorder;
        if (border == null) return;

        var existing = __instance.GetComponent<BridgeBorderTheme>();
        if (BorderTheme.TryResolve(__instance.cosmeticAsset, out var theme) && !theme.HasSolid)
        {
            existing ??= __instance.gameObject.AddComponent<BridgeBorderTheme>();
            existing.ConfigureGradient(border, theme.Key, theme.Gradient!, theme.Weights);
        }
        else
        {
            existing?.Deactivate();
        }
    }
}
