using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Custom RGB for vanilla/modded cosmetics via PlayerMaterial: its private `material` field (instanced) is read by reflection so colour properties can be set directly (ColorSet only takes palette indices).
// Also provides section-level helpers for the section "C" button (applies the same custom RGB to every eligible cosmetic in one click).
internal static class VanillaTintHelper
{
    // Reflection accessor for the private `material` field on PlayerMaterial.
    private static readonly LazyFieldRef<PlayerMaterial, Material> _materialRef =
        new("material", "custom RGB colors");
    private static Material? MaterialOf(PlayerMaterial pm)
        => _materialRef.TryGet(pm, out var mat) ? mat : null;

    // ── Per-cosmetic eligibility ───────────────────────────────────────────────

    /// True when this non-bridge cosmetic should expose the "C" button.
    /// Uses CosmeticAsset.tintable as the canonical tintability flag — vanilla guarantees
    /// consistency between the asset flag and the PlayerMaterial.tintable field in Cosmetic.Setup().
    internal static bool IsEligibleForCustomColor(CosmeticAsset? asset)
    {
        if (asset == null || BridgeIds.IsBridgeAsset(asset)) return false;
        if (!asset.tintable) return false;
        // Per-cosmetic "Allow Custom Color" override wins; otherwise the global modded/vanilla flag.
        return CustomizerStore.GetEffectiveCustomColors(asset);
    }

    // ── Apply helpers ──────────────────────────────────────────────────────────

    /// Applies a custom RGB colour to one PlayerMaterial, respecting its tintType and
    /// tintFresnel flags — mirrors PlayerMaterial.ColorSet but with an arbitrary Color.
    internal static void ApplyCustomRGB(PlayerMaterial pm, Color color)
    {
        if (pm == null || !pm.tintable) return;
        var mat = MaterialOf(pm);
        if (mat == null) return;

        if (pm.tintType == PlayerMaterial.TintType.Albedo || pm.tintType == PlayerMaterial.TintType.Both)
        {
            Color cur = mat.GetColor(PerCosmeticColors.PropAlbedo);
            mat.SetColor(PerCosmeticColors.PropAlbedo, new Color(color.r, color.g, color.b, cur.a));
        }
        if (pm.tintType == PlayerMaterial.TintType.Emission || pm.tintType == PlayerMaterial.TintType.Both)
        {
            Color cur = mat.GetColor(PerCosmeticColors.PropEmission);
            mat.SetColor(PerCosmeticColors.PropEmission, new Color(color.r, color.g, color.b, cur.a));
        }
        if (pm.tintFresnel)
            mat.SetColor(PerCosmeticColors.PropFresnel, new Color(color.r, color.g, color.b, color.a));
    }

    /// Repaints a PM to its palette colour (the PC's last-applied colorsEquipped) — used when an override
    /// stops applying, since vanilla's colorsEquipped diff won't repaint a PM that was painted over.
    internal static void RepaintPalette(PlayerMaterial pm, PlayerCosmetics pc)
    {
        int typeIdx = (int)pm.cosmeticType;
        var colors = pc.colorsEquipped;
        if (colors == null || typeIdx < 0 || typeIdx >= colors.Length) return;
        int color = colors[typeIdx];
        if (color < 0 || MetaManager.instance == null || color >= MetaManager.instance.colors.Count) return;
        pm.ColorSet(PerCosmeticColors.PropAlbedo,
                    PerCosmeticColors.PropEmission,
                    PerCosmeticColors.PropFresnel,
                    color);
    }

    /// Applies a custom RGB colour to every PlayerMaterial for this asset on all live
    /// PlayerCosmetics instances — used for live preview in the RGB popup.
    internal static void ApplyCustomRGBToLiveInstances(CosmeticAsset asset, Color color)
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(true))
        {
            if (pc?.playerMaterials == null) continue;
            // Local player's own avatars only — never remote players / remote minis / preset minis.
            if (!RuntimeConfigApplier.IsLivePaintTarget(pc)) continue;
            foreach (var pm in pc.playerMaterials)
            {
                if (pm == null || !pm.tintable) continue;
                if (pm.cosmetic?.cosmeticAsset != asset) continue;
                ApplyCustomRGB(pm, color);
            }
        }
    }

    // ── Base mesh support ──────────────────────────────────────────────────────
    // meshSwitch types have a "Default" menu button = the base player mesh (pm.cosmetic == null, no CosmeticAsset), identified by a synthetic assetId so it fits the same _customColors map.
    internal static string BaseMeshAssetId(int cosmeticTypeIndex)
        => $"__base_{cosmeticTypeIndex}__";

    /// True when the assetId is a synthetic base-mesh key (not a real cosmetic).
    internal static bool IsBaseMeshId(string assetId)
        => assetId.StartsWith("__base_") && assetId.EndsWith("__");

    // ── Explicit base mesh palette re-application ─────────────────────────
    // Called after all SetupColors internals (Postfix on PlayerCosmetics.SetupColors) so base-mesh PMs always end on the correct palette/custom colour regardless of which path ran.

    internal static void ReapplyBaseMeshColors(PlayerCosmetics? pc)
    {
        if (!Plugin.EnableVanillaCustomColors.Value) return;
        if (pc?.playerMaterials == null) return;
        var visuals = pc.playerAvatarVisuals;
        if (visuals == null) return;
        if (!visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true) return;

        // A remote player's mini is a menu avatar (passes the gate above) but its base-mesh customs come from the
        // owner's synced data — never the local store. Mirrors the ApplyOverrides / RefreshLiveAnimators guards.
        if (AvatarIdentity.IsRemoteMini(pc)) return;

        // A RandomPreset mini's base meshes take their customs from the PRESET. SetupColors also fires autonomously (ReapplyLocalCosmeticColors hits every local PC incl. the mini) — re-establish the preset context here or the player's live base-mesh customs leak onto the mini. Mirrors the ApplyOverrides guard.
        if (!PerCosmeticColors.MiniPresetContextActive)
        {
            int presetSlot = MiniSemibotSpawner.PresetSlotOf(pc);
            if (presetSlot >= 0)
            {
                PerCosmeticColors.RunWithPresetContext(presetSlot, () => ReapplyBaseMeshColors(pc));
                return;
            }
        }

        foreach (var pm in pc.playerMaterials)
        {
            if (pm == null || pm.cosmetic != null || !pm.tintable) continue;
            // Honours preset-preview state (preview custom > live custom > palette).
            PerCosmeticColors.ApplyBaseMeshColor(pm, pc.colorsEquipped);
        }
    }

    // ── Section helpers ────────────────────────────────────────────────────────
    // Section "C" button: same custom RGB for every eligible cosmetic in the section. Eligible = tintable + the matching per-type config flag enabled.

    /// True when the given cosmetic accepts custom RGB colours based on the current config.
    internal static bool IsEligibleForSectionCustom(CosmeticAsset asset)
    {
        if (!asset.tintable) return false;
        // Per-cosmetic "Allow Custom Color" override wins; otherwise the global flag for its kind.
        return CustomizerStore.GetEffectiveCustomColors(asset);
    }

    /// True when any equipped cosmetic (or base mesh) in this section is eligible for custom RGB.
    internal static bool HasAnyTintableForSection(MenuPageColor menuPageColor)
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return false;
        int colorKey = SectionColorButtonPatch.PendingWorldSection
            ? (int)CosmeticsFilterPatch.WorldSubCategory
            : menuPageColor.colorKey;

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || !IsEligibleForSectionCustom(asset)) continue;
            if (CosmeticMatchesSection(asset, colorKey, menuPageColor.pageMode)) return true;
        }

        // Also check the section's base-mesh PMs ("Default") — enables the "C" button when only the no-cosmetic state is active in the section.
        if (BaseMeshTypesForSection(colorKey, menuPageColor.pageMode).Count > 0) return true;
        return false;
    }

    /// meshSwitch type indices whose base mesh falls in a section paint (mirrors CosmeticMatchesSection): World → none; specific subcategory → that type if meshSwitch; All/Body → every meshSwitch; Cosmetics → none. Only types with a tintable base PM.
    private static List<int> BaseMeshTypesForSection(int ck, MenuPageColor.ColorPageType pageMode)
    {
        var result = new List<int>();
        if (!Plugin.EnableVanillaCustomColors.Value) return result;
        if (ck == (int)CosmeticsFilterPatch.WorldSubCategory) return result;
        var metaTypes = MetaManager.instance?.cosmeticTypeAssets;
        if (metaTypes == null) return result;

        foreach (var cta in metaTypes)
        {
            if (cta == null || !cta.meshSwitch) continue;   // base meshes are meshSwitch types
            int ti = (int)cta.type;
            if (ck >= 0)
            {
                if (ti != ck) continue;                     // specific subcategory: only that type
            }
            else if (pageMode == MenuPageColor.ColorPageType.Cosmetics)
            {
                continue;                                   // Cosmetics excludes meshSwitch types
            }
            if (HasTintableBaseMesh(ti)) result.Add(ti);
        }
        return result;
    }

    /// Removes the base mesh ("Default" head/arm/etc.) custom colours for every meshSwitch type the
    /// section covers, without saving. Used by the section "Original" button so the Default meshes
    /// revert to their palette colour alongside the cosmetics. Returns true when any was removed.
    internal static bool RemoveSectionBaseMeshCustomsNoSave(int colorKey, MenuPageColor.ColorPageType pageMode)
    {
        bool any = false;
        foreach (int ti in BaseMeshTypesForSection(colorKey, pageMode))
            any |= PerCosmeticColors.RemoveCustomColorNoSave(BaseMeshAssetId(ti));
        return any;
    }

    /// True when the CosmeticParent for the given type has at least one tintable base mesh PM.
    /// Mirrors the logic in MenuElementCosmeticButton's tintableButton visibility check.
    internal static bool HasTintableBaseMesh(int cosmeticTypeIndex)
    {
        var cosmeticParents = PlayerAvatar.instance?.playerCosmetics?.cosmeticParents;
        if (cosmeticParents == null) return false;
        foreach (var cp in cosmeticParents)
        {
            if ((int)cp.cosmeticType != cosmeticTypeIndex) continue;
            foreach (Transform baseMesh in cp.baseMeshes)
            {
                var pm = baseMesh.GetComponent<PlayerMaterial>();
                if (pm != null && pm.tintable) return true;
            }
        }
        return false;
    }

    /// Applies a custom RGB to the live instances of every eligible cosmetic in the section.
    /// Includes base mesh PMs (pm.cosmetic == null) for the specific-type case.
    /// Used for the live preview while the popup RGB sliders are being dragged.
    internal static void ApplyCustomRGBToSectionLive(int colorKey, MenuPageColor.ColorPageType pageMode, Color color)
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;
        int ck = SectionColorButtonPatch.PendingWorldSection
            ? (int)CosmeticsFilterPatch.WorldSubCategory : colorKey;

        // Equipped CosmeticAssets.
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || !IsEligibleForSectionCustom(asset)) continue;
            if (!CosmeticMatchesSection(asset, ck, pageMode)) continue;
            if (BridgeIds.IsBridgeAsset(asset))
                BridgeTintHelper.ApplyWholeAssetRGBToLiveInstances(asset, color);
            else
                ApplyCustomRGBToLiveInstances(asset, color);
        }

        // Base mesh PMs (the "Default" mesh) for every meshSwitch type the section covers (a specific subcategory, or all of them for Paint All / Body).
        var baseTypes = BaseMeshTypesForSection(ck, pageMode);
        if (baseTypes.Count > 0)
        {
            foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(true))
            {
                if (pc?.playerMaterials == null) continue;
                // Local player's own avatars only — never remote players / remote minis / preset minis.
                if (!RuntimeConfigApplier.IsLivePaintTarget(pc)) continue;
                foreach (var pm in pc.playerMaterials)
                {
                    if (pm == null || !pm.tintable || pm.cosmetic != null) continue;
                    if (baseTypes.Contains((int)pm.cosmeticType))
                        ApplyCustomRGB(pm, color);
                }
            }
        }
    }

    /// Saves a custom RGB for every eligible cosmetic (and base mesh) in the section.
    /// Uses batched NoSave writes then flushes once.
    internal static void SaveCustomColorToSection(int colorKey, MenuPageColor.ColorPageType pageMode, Color color)
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;
        int ck = SectionColorButtonPatch.PendingWorldSection
            ? (int)CosmeticsFilterPatch.WorldSubCategory : colorKey;
        bool any = false, anyAnim = false;

        // Equipped CosmeticAssets.
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || !IsEligibleForSectionCustom(asset)) continue;
            if (!CosmeticMatchesSection(asset, ck, pageMode)) continue;
            PerCosmeticColors.SetCustomColorNoSave(asset.assetId, color);
            any = true;
            anyAnim = true;
        }

        // Base mesh (the "Default" mesh) — for every meshSwitch type the section covers.
        foreach (int ti in BaseMeshTypesForSection(ck, pageMode))
        {
            PerCosmeticColors.SetCustomColorNoSave(BaseMeshAssetId(ti), color);
            any = true;
        }

        if (!any) return;
        PerCosmeticColors.SaveCustom();
        PerCosmeticColors.Save();
        PerCosmeticColors.SaveSlots();
        PerCosmeticColors.SaveAnimations();
        PerCosmeticColors.SaveSlotAnimations();
        PerCosmeticColors.SaveCustomSlots();
        if (anyAnim) ColorAnimatorRefresher.RefreshLocal();
        RuntimeConfigApplier.ReapplyLocalCosmeticColors();
        if (SemiFunc.IsMultiplayer()) PerCosmeticColorNetworkSync.BroadcastAll();
    }

    /// Removes the custom RGB override for every eligible cosmetic (and base mesh) in the section.
    internal static void RemoveCustomColorFromSection(int colorKey, MenuPageColor.ColorPageType pageMode)
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;
        int ck = SectionColorButtonPatch.PendingWorldSection
            ? (int)CosmeticsFilterPatch.WorldSubCategory : colorKey;
        bool any = false;

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || !IsEligibleForSectionCustom(asset)) continue;
            if (!CosmeticMatchesSection(asset, ck, pageMode)) continue;
            if (PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId)) any = true;
        }

        // Also remove base mesh entries — for every meshSwitch type the section covers.
        foreach (int ti in BaseMeshTypesForSection(ck, pageMode))
        {
            if (PerCosmeticColors.RemoveCustomColorNoSave(BaseMeshAssetId(ti))) any = true;
        }

        if (!any) return;
        PerCosmeticColors.SaveCustom();
        RuntimeConfigApplier.ReapplyLocalCosmeticColors();
        if (SemiFunc.IsMultiplayer()) PerCosmeticColorNetworkSync.BroadcastAll();
    }

    // ── Section scope filter ───────────────────────────────────────────────────
    // Single source of truth for "does this cosmetic belong to the colour section being painted".
    // BridgeOriginalColorButton.MatchesSectionFilter delegates here; OriginalColorButtonPatch.SectionScopeIncludes
    // shares the non-world tail via SectionTypeScope. (CosmeticColorSetPatch's bridge loop filters by the single
    // type being painted, so it is intentionally separate.)
    internal static bool CosmeticMatchesSection(CosmeticAsset asset, int colorKey, MenuPageColor.ColorPageType pageMode)
    {
        bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
        bool isWorldSection = colorKey == (int)CosmeticsFilterPatch.WorldSubCategory;

        if (isWorldSection) return assetIsWorld;

        if (assetIsWorld)
        {
            bool isMultiMode = colorKey < 0;
            bool isBodyMode  = pageMode == MenuPageColor.ColorPageType.Body;
            return isMultiMode && !isBodyMode;
        }

        return SectionTypeScope(asset.type, colorKey, pageMode);
    }

    // Non-world type-scope test shared by the section filters: specific subcategory → exact type;
    // All → everything; Cosmetics → non-meshSwitch types; Body → meshSwitch types.
    internal static bool SectionTypeScope(SemiFunc.CosmeticType type, int colorKey, MenuPageColor.ColorPageType pageMode)
    {
        if (colorKey >= 0) return (int)type == colorKey;
        if (pageMode == MenuPageColor.ColorPageType.All) return true;

        var meta = MetaManager.instance;
        int typeIdx = (int)type;
        if (meta?.cosmeticTypeAssets == null || typeIdx < 0 || typeIdx >= meta.cosmeticTypeAssets.Count)
            return true; // fallback: include when type info unavailable

        bool isMeshSwitch = meta.cosmeticTypeAssets[typeIdx].meshSwitch;
        return pageMode == MenuPageColor.ColorPageType.Cosmetics ? !isMeshSwitch : isMeshSwitch;
    }
}
