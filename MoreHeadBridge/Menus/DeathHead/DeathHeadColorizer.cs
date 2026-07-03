// Colours the death-head preview like the in-game death head (vanilla PlayerMaterial.ColorSet), preferring the per-cosmetic override over the per-type colour. Must run BEFORE the strip destroys PlayerMaterial (the baked colour survives).

using UnityEngine;

namespace MoreHeadBridge;

internal static class DeathHeadColorizer
{
    // Tints the death head's body/head meshes; each prefers the per-cosmetic override of the equipped cosmetic of its type (so little legs match a recoloured leg mesh we don't swap), else the per-type colour.
    internal static void ApplyBodyColors(GameObject model, PlayerCosmetics? source)
    {
        var equipped = source?.colorsEquipped;
        var colors = MetaManager.instance?.colors;
        if (equipped == null || colors == null) return;

        var pms = model.GetComponentsInChildren<PlayerMaterial>(true);
        foreach (var pm in pms)
        {
            if (pm == null) continue;
            int typeIdx = (int)pm.cosmeticType;
            if (typeIdx < 0 || typeIdx >= equipped.Length) continue;
            int colorIdx = equipped[typeIdx];

            string? assetId = FindEquippedAssetId(source, pm.cosmeticType);
            if (assetId != null)
                colorIdx = PerCosmeticColors.GetEffectiveColorIndex(assetId, colorIdx);

            ApplyColor(pm, colorIdx, colors.Count);
        }

        // Custom RGB overrides the palette pass (base meshes + non-bridge PMs). A REMOTE mini is owner-authoritative — only the owner's synced data paints it (else our RGB leaks onto a friend); local/preview use the local store (honours EnableVanillaCustomColors).
        bool remoteSource = source != null && AvatarIdentity.IsRemoteMini(source);
        if (remoteSource)
            source!.GetComponent<PerCosmeticColorSyncComponent>()?.ApplyVanillaOverridesTo(pms);
        else
            PerCosmeticColors.ApplyVanillaOverridesTo(pms);

        // Hide-only types (legs) keep the death head's own base mesh, so re-assert the equipped leg COSMETIC's colour (custom RGB > palette/type) on base PMs that have a cosmetic of their type — ApplyVanillaOverridesTo clobbered it with the base-mesh custom. Owner-authoritative: remote reads synced, local reads the store.
        var remoteSync = remoteSource ? source!.GetComponent<PerCosmeticColorSyncComponent>() : null;
        foreach (var pm in pms)
        {
            if (pm == null || pm.cosmetic != null) continue;            // base-mesh PMs only
            string? cosId = FindEquippedAssetId(source, pm.cosmeticType);
            if (cosId == null) continue;                                // no cosmetic of this type → keep base
            int ti = (int)pm.cosmeticType;
            int fallback = (ti >= 0 && ti < equipped.Length) ? equipped[ti] : -1;

            if (remoteSource)
            {
                if (remoteSync == null) continue;
                if (remoteSync.TryGetRemoteCustomColor(cosId, out var rcc))
                    VanillaTintHelper.ApplyCustomRGB(pm, rcc);
                else
                    ApplyColor(pm, remoteSync.GetEffectiveRemoteColorIndex(cosId, fallback), colors.Count);
            }
            else if (Plugin.EnableVanillaCustomColors.Value && PerCosmeticColors.TryGetCustomColor(cosId, out var cc))
                VanillaTintHelper.ApplyCustomRGB(pm, cc);
            else
                ApplyColor(pm, PerCosmeticColors.GetEffectiveColorIndex(cosId, fallback), colors.Count);
        }
    }

    // Per-cosmetic override when set, else the player's per-type colour — used for the PREFAB-mounted mesh cosmetics (decorative clones already carry their colour).
    internal static void ApplyCosmeticColor(GameObject go, CosmeticAsset asset, PlayerCosmetics? source)
    {
        var equipped = source?.colorsEquipped;
        var colors = MetaManager.instance?.colors;
        if (colors == null) return;

        int typeColor = -1;
        if (equipped != null)
        {
            int ti = (int)asset.type;
            if (ti >= 0 && ti < equipped.Length) typeColor = equipped[ti];
        }

        // Owner-authoritative: a remote mini's per-cosmetic palette lives in its synced component, never the viewer's store.
        bool remote = source != null && AvatarIdentity.IsRemoteMini(source);
        var remoteSync = remote ? source!.GetComponent<PerCosmeticColorSyncComponent>() : null;
        int colorIdx = remoteSync != null
            ? remoteSync.GetEffectiveRemoteColorIndex(asset.assetId, typeColor)
            : PerCosmeticColors.GetEffectiveColorIndex(asset.assetId, typeColor);

        var pms = go.GetComponentsInChildren<PlayerMaterial>(true);
        foreach (var pm in pms)
        {
            if (pm == null) continue;
            pm.cosmeticType = asset.type;
            ApplyColor(pm, colorIdx, colors.Count);
        }

        // Custom RGB outranks the palette index (the loop only does palette). Bridge mesh cosmetics are cloned with the tint baked in elsewhere. Remote minis are owner-authoritative (synced, never gated by the viewer's EnableVanillaCustomColors); local/preview use the local store.
        if (!BridgeIds.IsBridgeAsset(asset))
        {
            Color custom = default;
            bool got = remoteSync != null
                ? remoteSync.TryGetRemoteCustomColor(asset.assetId, out custom)
                : Plugin.EnableVanillaCustomColors.Value && PerCosmeticColors.TryGetCustomColor(asset.assetId, out custom);
            if (got)
                foreach (var pm in pms)
                    if (pm != null) VanillaTintHelper.ApplyCustomRGB(pm, custom);
        }
    }

    private static void ApplyColor(PlayerMaterial pm, int colorIdx, int colorCount)
    {
        if (colorIdx < 0 || colorIdx >= colorCount) return;
        pm.Setup(); // populate the instance material (idempotent)
        pm.ColorSet(PerCosmeticColors.PropAlbedo, PerCosmeticColors.PropEmission,
                    PerCosmeticColors.PropFresnel, colorIdx);
    }

    private static string? FindEquippedAssetId(PlayerCosmetics? pc, SemiFunc.CosmeticType type)
    {
        if (pc == null) return null;
        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(pc);
        if (equipped == null) return null;
        foreach (var c in equipped)
        {
            if (c == null) continue;
            var a = MoreHeadCosmeticMountPatch.GetCosmeticAsset(c);
            if (a != null && a.type == type) return a.assetId;
        }
        return null;
    }
}
