using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Detects and applies tinting on bridge (.hhh) cosmetics via BridgeTintMaterial.
// Bridge prefabs have no PlayerMaterial, and vanilla's colour path is hard-wired to _AlbedoColor/_EmissionColor — detect the material's real colour property at injection time so tinting works on Standard / URP / Unlit / Hurtable. Emission only on Hurtable, matching vanilla.
// Flow: DetectTintable (registration) → InjectBridgeTintMaterials (equip) → ApplyTypeColors (SetupColorsLogic postfix).
internal static class BridgeTintHelper
{
    // Primary colour property candidates in priority order; the first that exists on the material becomes the primary colour channel.
    private static readonly string[] PrimaryColorProps =
    {
        "_AlbedoColor",  // REPO Hurtable shader
        "_BaseColor",    // URP Lit / Unlit (Unity 2019+)
        "_Color",        // Standard, Unlit/Color, many legacy / custom shaders
        "_TintColor",    // Particle legacy shaders
    };

    // Emission property name — only paired with _AlbedoColor (Hurtable), matching vanilla.
    private const string EmissionProp = "_EmissionColor";

    // ── Registration-time detection ────────────────────────────────────────────

    /// Scans the PREFAB's shared materials for any supported primary colour property.
    /// Returns true if at least one renderer exposes a tintable colour channel.
    /// Uses sharedMaterials — does NOT create instance materials.
    internal static bool DetectTintable(GameObject prefab)
    {
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;
                foreach (string prop in PrimaryColorProps)
                    if (mat.HasProperty(prop)) return true;
            }
        }
        return false;
    }

    // ── Equip-time injection ───────────────────────────────────────────────────

    /// Adds BridgeTintMaterial to every renderer on <paramref name="go"/> whose material
    /// exposes a known primary colour property. No-op if the asset is not tintable.
    /// tintableOverride: non-null replaces asset.tintable — remote instances pass their wearer's
    /// effective flag, since the shared asset.tintable may carry the viewer's own override.
    internal static void InjectBridgeTintMaterials(GameObject go, CosmeticAsset asset, bool? tintableOverride = null)
    {
        if (!(tintableOverride ?? asset.tintable)) return;

        // The root GO's Cosmetic component — linked so per-cosmetic colour lookup by assetId works in ApplyOverrides and ApplyToCosmetics.
        var cosmetic = go.GetComponentInChildren<Cosmetic>(includeInactive: true)
                    ?? go.GetComponent<Cosmetic>();

        // Sequential BTM index + running material-slot offset, assigned to each new BridgeTintMaterial so the slot picker can address individual material slots.
        int btmCount = 0;
        int totalMaterialSlots = 0;

        foreach (var r in go.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            // Renderers with a vanilla PlayerMaterial are already driven by PerCosmeticColors — injecting BridgeTintMaterial too would paint them twice. Only custom-shader renderers need us. (Modded cosmetics that DO want per-part slots on PlayerMaterial renderers go through ModdedTintInjector instead.)
            if (r.GetComponent<PlayerMaterial>() != null) continue;

            var btm = TryAddBridgeTintMaterial(r, asset, cosmetic, btmCount, totalMaterialSlots);
            if (btm == null) continue;
            btmCount++;
            totalMaterialSlots += btm.materials?.Length ?? r.sharedMaterials.Length;
        }
    }

    // Adds a BridgeTintMaterial to one renderer, detecting its colour property. Returns null when the renderer already has a BTM (re-equip guard) or exposes no supported colour property. Shared by the bridge equip path and ModdedTintInjector.
    // suppressEmission: modded cosmetics (YoshiCarry) author their materials as Hurtable but with tintType:0/tintFresnel:0 (albedo-only) — writing _EmissionColor blows them out, so the injector forces emission off.
    internal static BridgeTintMaterial? TryAddBridgeTintMaterial(
        Renderer r, CosmeticAsset asset, Cosmetic? cosmetic, int btmIndex, int materialSlotOffset, bool suppressEmission = false)
    {
        if (r.GetComponent<BridgeTintMaterial>() != null) return null;

        // Probe the first non-null shared material to detect properties.
        Material? probe = null;
        foreach (var mat in r.sharedMaterials)
            if (mat != null) { probe = mat; break; }
        if (probe == null) return null;

        string? primaryProp = null;
        foreach (string prop in PrimaryColorProps)
            if (probe.HasProperty(prop)) { primaryProp = prop; break; }
        if (primaryProp == null) return null;

        // Emission is added only when the primary is _AlbedoColor (Hurtable shader), matching vanilla PlayerMaterial's dual-channel behaviour — unless the caller suppresses it (albedo-only modded materials).
        bool hasEmission = !suppressEmission && primaryProp == "_AlbedoColor" && probe.HasProperty(EmissionProp);

        var btm = r.gameObject.AddComponent<BridgeTintMaterial>();
        btm.primaryPropId = Shader.PropertyToID(primaryProp);
        btm.emissionPropId = hasEmission ? Shader.PropertyToID(EmissionProp) : 0;
        btm.hasEmission = hasEmission;
        btm.cosmeticType = asset.type;
        btm.cosmetic = cosmetic;
        btm.btmIndex = btmIndex;
        btm.materialSlotOffset = materialSlotOffset;
        btm.Setup();
        return btm;
    }

    // ── Slot-colour live application ───────────────────────────────────────────

    /// Applies colorIndex to the single flat slot (0-based across all the cosmetic's BTMs) — immediate feedback in slot mode, no SetupColorsLogic wait.
    internal static void ApplySlotColorToLiveInstances(CosmeticAsset asset, int flatSlot, int colorIndex)
    {
        var allBtm = Object.FindObjectsOfType<BridgeTintMaterial>(true);
        foreach (var btm in allBtm)
        {
            if (btm?.cosmetic?.cosmeticAsset != asset) continue;
            if (btm.materials == null) continue;
            if (!RuntimeConfigApplier.IsLivePaintTarget(btm.cosmetic?.playerCosmetics)) continue;

            // A grouped layout can map several local materials to the same slot id — paint every match, not just one.
            for (int localSlot = 0; localSlot < btm.materials.Length; localSlot++)
            {
                if (btm.SlotIdOf(localSlot) != flatSlot) continue;
                if (colorIndex == PerCosmeticColors.OriginalColorSentinel)
                    btm.RestoreOriginalColorInSlot(localSlot);
                else
                    btm.ApplyColorToSlot(localSlot, colorIndex);
            }
        }
    }

    /// Applies colorIndex to EVERY slot of the asset's live BTMs — a per-cosmetic paint blocks vanilla CosmeticColorSet, so nothing else repaints. Essential for World cosmetics (BTM-only, under the world follower).
    internal static void ApplyWholeAssetColorToLiveInstances(CosmeticAsset asset, int colorIndex)
    {
        foreach (var btm in Object.FindObjectsOfType<BridgeTintMaterial>(true))
        {
            if (btm?.cosmetic?.cosmeticAsset != asset) continue;
            if (!RuntimeConfigApplier.IsLivePaintTarget(btm.cosmetic?.playerCosmetics)) continue;
            if (colorIndex == PerCosmeticColors.OriginalColorSentinel)
                btm.RestoreOriginalColor();
            else
                btm.ApplyColor(colorIndex);
        }
    }

    /// Applies an arbitrary RGB (the "C" custom colour) to every live BridgeTintMaterial of the
    /// asset — menu/preview avatars only; the in-game avatar and its mini apply at the menu confirm.
    internal static void ApplyWholeAssetRGBToLiveInstances(CosmeticAsset asset, Color color)
    {
        foreach (var btm in Object.FindObjectsOfType<BridgeTintMaterial>(true))
            if (btm?.cosmetic?.cosmeticAsset == asset
                && RuntimeConfigApplier.IsMenuPreviewPaintTarget(btm.cosmetic?.playerCosmetics))
                btm.ApplyColorRGB(color);
    }

    /// Applies a custom RGB to a single flat material slot of the asset's live instances.
    internal static void ApplySlotRGBToLiveInstances(CosmeticAsset asset, int flatSlot, Color color)
    {
        foreach (var btm in Object.FindObjectsOfType<BridgeTintMaterial>(true))
        {
            if (btm?.cosmetic?.cosmeticAsset != asset || btm.materials == null) continue;
            // Menu/preview avatars only — the in-game avatar and its mini apply at the menu confirm.
            if (!RuntimeConfigApplier.IsMenuPreviewPaintTarget(btm.cosmetic?.playerCosmetics)) continue;
            for (int localSlot = 0; localSlot < btm.materials.Length; localSlot++)
                if (btm.SlotIdOf(localSlot) == flatSlot)
                    btm.ApplyColorRGBToSlot(localSlot, color);
        }
    }

    // ── Paint eligibility ──────────────────────────────────────────────────────

    /// True when a bridge cosmetic takes section-paint / randomize colours: bridge + EnableBridgeTinting + tintable + no explicit Tintable=false override.
    internal static bool CanBridgeCosmeticReceivePaint(CosmeticAsset? asset)
    {
        if (asset == null) return false;
        if (!BridgeIds.IsBridgeAsset(asset)) return false;

        // Per-cosmetic override wins over both global setting and asset.tintable.
        if (CustomizerStore.TryGet(asset.assetId, out var data) && data.Tintable.HasValue)
            return data.Tintable.Value;

        if (!Plugin.EnableBridgeTinting.Value) return false;
        if (!asset.tintable) return false;
        return true;
    }

    // ── Type-colour application ────────────────────────────────────────────────

    /// Applies type colours (pc.colorsEquipped) to every BTM under playerAvatarVisuals — the bridge equivalent of vanilla's PlayerMaterial loop in the SetupColors*Logic postfixes.
    /// Runs for ALL PlayerCosmetics (local, menu, remote) so RPC-triggered colour logic also paints remote bridge cosmetics.
    internal static void ApplyTypeColors(PlayerCosmetics pc)
    {
        if (pc?.playerAvatarVisuals == null) return;

        var btms = pc.playerAvatarVisuals
                     .GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true);
        if (btms.Length == 0) return;

        int[] colorsEquipped = pc.colorsEquipped;
        if (colorsEquipped == null) return;

        // Remote avatars' overrides live in the synced component, not the local store — consulting the local store here would leak local paint onto remote avatars.
        var visuals = pc.playerAvatarVisuals;
        bool isRemote = !visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true;
        var remoteSync = isRemote ? pc.GetComponent<PerCosmeticColorSyncComponent>() : null;

        foreach (var btm in btms)
        {
            if (btm == null) continue;

            var assetId = btm.cosmetic?.cosmeticAsset?.assetId;
            var cosmeticAsset = btm.cosmetic?.cosmeticAsset;

            // Bridge cosmetics (author colours by default) and modded cosmetics with a per-part slot layout (e.g. YoshiCarry) are both painted by the override path — local ApplyOverrides, remote sync. Here we only set their DEFAULT when no override exists. Applying the type colour for these on remote would clobber the synced per-slot override, so we must skip it when an override is present.
            bool isBridge = cosmeticAsset != null && BridgeIds.IsBridgeAsset(cosmeticAsset);
            bool isModdedSlots = cosmeticAsset != null && !isBridge && ModdedSlotLayout.Handles(cosmeticAsset);
            if (isBridge || isModdedSlots)
            {
                // Custom RGB / animation count as "an override" only while their feature is enabled for this cosmetic — disabling the config reverts it to the original colour here. Palette overrides are never gated; remote overrides follow the REMOTE's choice (local config must not hide them).
                bool hasOverride;
                if (isRemote)
                {
                    hasOverride = PerCosmeticColors.FeatureEnabled && assetId != null
                                  && remoteSync != null && remoteSync.HasRemoteOverride(assetId);
                }
                else
                {
                    bool customAllowed = CustomizerStore.GetEffectiveCustomColors(cosmeticAsset!);
                    bool animAllowed = CustomizerStore.GetEffectiveColorAnimations(cosmeticAsset!);
                    hasOverride = PerCosmeticColors.FeatureEnabled && assetId != null
                        && (PerCosmeticColors.HasOverride(assetId)
                            || (animAllowed && PerCosmeticColors.HasAnimation(assetId))
                            || (customAllowed && (PerCosmeticColors.HasCustomColor(assetId)
                                                  || PerCosmeticColors.HasAnyCustomSlotColor(assetId))));
                }
                if (hasOverride) continue;            // override path owns the colour

                // No override: bridge → restore the author colour; modded → fall through to the vanilla type colour (its "ALL = vanilla" default).
                if (isBridge) { btm.RestoreOriginalColor(); continue; }
            }

            // Vanilla & modded-default cosmetics: apply type colour (skip "original mode" — ApplyOverrides owns it).
            if (PerCosmeticColors.IsOriginalMode(assetId)) continue;

            int typeIdx = (int)btm.cosmeticType;
            if (typeIdx < 0 || typeIdx >= colorsEquipped.Length) continue;
            int colorIdx = colorsEquipped[typeIdx];
            if (colorIdx < 0) continue; // -1 = uninitialised, skip
            btm.ApplyColor(colorIdx);
        }
    }
}
