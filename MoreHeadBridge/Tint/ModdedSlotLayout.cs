using UnityEngine;

namespace MoreHeadBridge;

// Per-asset slot layout for modded (non-bridge) cosmetics that drive colour through vanilla PlayerMaterial.
// Maps each tintable renderer to a logical slot id so several renderers can share ONE colour slot (grouping),
// which the flat "one material = one slot" BTM model can't express. Currently only YoshiCarry.
//
// YoshiCarry registers one cosmetic per body slot (Body Bottom Mesh / Hat / Leg Left / Leg Right), each with
// assetId "yoshicarry:" + 3-letter slot token + colour token + "yoshi" (e.g. "bbmcustomyoshi", "bbmgreenyoshi",
// "hatredyoshi"). ONLY the Body Bottom Mesh ("bbm") cosmetic carries the multi-part Yoshi body we colour by part
// — the Hat / Leg cosmetics are single, vanilla-coloured pieces and must stay untouched.
//
// On the "bbm" cosmetic the three shared parts (mesh_body_bot / mesh_leg_l / mesh_leg_r, the "86899e" body
// material reused across every variant) are always their own slots; the Custom variant adds a leading "skin"
// slot grouping every OTHER tintable renderer (body, hands, eyelid, face) into one colour.
//
// Slot order (UI label = id + 1):
//   Fixed (Blue/Green/Red/Yellow): 0 = mesh_body_bot, 1 = mesh_leg_l, 2 = mesh_leg_r
//   Custom:                        0 = skin (everything else), 1 = mesh_body_bot, 2 = mesh_leg_l, 3 = mesh_leg_r
internal static class ModdedSlotLayout
{
    // Only the Body Bottom Mesh slot ("bbm") of YoshiCarry — the one holding the full multi-part Yoshi body.
    private const string YoshiPrefix = "yoshicarry:bbm";

    // Shared-part renderer names (same across every Yoshi variant).
    private const string BodyBot = "mesh_body_bot";
    private const string LegL = "mesh_leg_l";
    private const string LegR = "mesh_leg_r";

    internal static bool Handles(CosmeticAsset? asset)
        => asset != null && (asset.assetId ?? "").StartsWith(YoshiPrefix, System.StringComparison.OrdinalIgnoreCase);

    // True for the "Custom" Yoshi (colour token "custom" right after the prefix), which has the extra skin slot.
    // YoshiPrefix already includes the "bbm" type token, so the colour token starts immediately after it.
    private static bool IsCustom(CosmeticAsset asset)
    {
        string id = asset.assetId ?? "";
        if (id.Length <= YoshiPrefix.Length) return false;
        return id.Substring(YoshiPrefix.Length)
                 .StartsWith("custom", System.StringComparison.OrdinalIgnoreCase);
    }

    // Number of colour slots the asset exposes (skin + 3 shared on Custom; 3 shared otherwise).
    internal static int SlotCount(CosmeticAsset asset) => IsCustom(asset) ? 4 : 3;

    // Slot id for a renderer's GameObject, or -1 when it isn't part of any slot (caller leaves it untouched →
    // keeps its original/vanilla colour). The caller is responsible for only passing TINTABLE renderers.
    internal static int SlotIdForRenderer(CosmeticAsset asset, GameObject go)
    {
        bool custom = IsCustom(asset);
        int bodyBot = custom ? 1 : 0;
        int legL = custom ? 2 : 1;
        int legR = custom ? 3 : 2;

        string name = go.name;
        if (name == BodyBot) return bodyBot;
        if (name == LegL) return legL;
        if (name == LegR) return legR;

        // Anything else: on Custom it's "skin" (slot 0); on a fixed variant there are no other tintable parts,
        // so don't claim it.
        return custom ? 0 : -1;
    }
}
