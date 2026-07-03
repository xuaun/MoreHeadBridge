using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// The dead/spectate 2D head (SpectateHeadUI) colours its sprites from PALETTE indices only (colorsEquipped[type] → colors[idx].color), ignoring our custom-RGB system. Mirror the lobby-head fix: after vanilla Update sets the palette colours, overwrite the sprites whose type has a custom RGB.
// Local player only (SpectateHeadUI reads SemiFunc.PlayerGetLocal()). The head graphic has NO body element — it draws head-top (main face), eyelids, and dark-shaded legs, so only those types apply.
[HarmonyPatch(typeof(SpectateHeadUI), "Update")]
internal static class SpectateHeadCustomColorPatch
{
    // Same per-sprite type map SpectateHeadUI.Update uses internally.
    private static readonly int[] ImageTypes = { 5, 14, 14, 15, 15, 5 };   // HeadTopMesh, EyeLidRight, EyeLidLeft
    private static readonly int[] DarkTypes  = { 11, 12 };                  // LegRightMesh, LegLeftMesh
    private const float DarkBlend = 0.5f;   // vanilla shades the dark sprites Color.Lerp(c, black, 0.5f)

    // Custom resolution iterates the avatar's cosmetics, so cache it and refresh on a cadence; applying the cached colours every frame (the vanilla lerp stops writing once settled) is cheap.
    private const float ResolveInterval = 0.5f;
    private static float _nextResolve;
    private static readonly Color?[] _custom = new Color?[ImageTypes.Length];
    private static readonly Color?[] _customDark = new Color?[DarkTypes.Length];

    [HarmonyPostfix]
    private static void Postfix(SpectateHeadUI __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        var pc = SemiFunc.PlayerGetLocal()?.playerCosmetics;
        if (pc == null) return;

        if (Time.time >= _nextResolve)
        {
            _nextResolve = Time.time + ResolveInterval;
            for (int i = 0; i < ImageTypes.Length; i++)
                _custom[i] = Resolve(pc, ImageTypes[i]);
            for (int i = 0; i < DarkTypes.Length; i++)
            {
                var c = Resolve(pc, DarkTypes[i]);
                _customDark[i] = c.HasValue ? Color.Lerp(c.Value, Color.black, DarkBlend) : (Color?)null;
            }
        }

        Apply(__instance.colorImages, _custom);
        Apply(__instance.colorImagesDark, _customDark);
    }

    // Overwrites each sprite's RGB with its resolved custom (keeping the vanilla-driven alpha) where set.
    private static void Apply(RawImage[]? images, Color?[] customs)
    {
        if (images == null) return;
        for (int i = 0; i < images.Length && i < customs.Length; i++)
        {
            if (!customs[i].HasValue || images[i] == null) continue;
            var c = customs[i]!.Value;
            images[i].color = new Color(c.r, c.g, c.b, images[i].color.a);
        }
    }

    // Effective custom RGB for a mesh type: an equipped cosmetic of that type with a custom wins, else the "Default" base-mesh custom ("__base_N__"). Null = no custom (keep the palette colour vanilla set).
    private static Color? Resolve(PlayerCosmetics pc, int typeIndex)
    {
        var visuals = pc.playerAvatarVisuals;
        if (visuals != null)
            foreach (var cos in visuals.GetComponentsInChildren<Cosmetic>(includeInactive: true))
            {
                if (cos == null || (int)cos.type != typeIndex) continue;
                var assetId = cos.cosmeticAsset?.assetId;
                if (assetId != null && PerCosmeticColors.TryGetCustomColor(assetId, out var c)) return c;
            }

        return PerCosmeticColors.TryGetCustomColor(VanillaTintHelper.BaseMeshAssetId(typeIndex), out var bc)
            ? bc : (Color?)null;
    }
}
