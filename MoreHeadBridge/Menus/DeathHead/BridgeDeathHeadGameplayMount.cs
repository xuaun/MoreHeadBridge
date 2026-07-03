// ============================================================================
// Mounts bridge cosmetics on the gameplay death head (PlayerDeathHead.Trigger) and tears them down on Reset.
// Created lazily via GetOrAdd(deathHead); Remount() also runs from a SetupCosmeticsLogic postfix so loadout changes while dead are picked up.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class BridgeDeathHeadGameplayMount : MonoBehaviour
{
    private PlayerDeathHead? _deathHead;
    private readonly List<GameObject> _mounted = new();

    // ── Factory ───────────────────────────────────────────────────────────────

    internal static BridgeDeathHeadGameplayMount GetOrAdd(PlayerDeathHead deathHead)
    {
        var comp = deathHead.GetComponent<BridgeDeathHeadGameplayMount>();
        if (comp != null) return comp;
        comp = deathHead.gameObject.AddComponent<BridgeDeathHeadGameplayMount>();
        comp._deathHead = deathHead;
        return comp;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// Re-apply colors to already-mounted bridge cosmetics without recreating them.
    /// Called from SetupColorsLogic / SetupColorsAllLogic postfixes so color-only changes
    /// (not cosmetic changes) are reflected on the death head without an expensive Remount.
    internal void RecolorMountedBridge()
    {
        if (_deathHead == null) return;
        var mainPc = _deathHead.playerCosmetics;
        if (mainPc == null) return;

        bool isRemote = AvatarIdentity.TryGetRemoteActor(mainPc, out _);

        // The death-head's own PlayerCosmetics has a null playerAvatarVisuals, so the normal per-cosmetic colour passes skip it — re-apply per-cosmetic colours here.
        RecolorVanillaDeathHead(mainPc, isRemote);

        // Bridge cosmetics mounted on the death head (separate GOs, outside any visuals tree).
        foreach (var go in _mounted)
        {
            if (go == null) continue;
            var asset = go.GetComponentInChildren<Cosmetic>(true)?.cosmeticAsset;
            if (asset == null) continue;
            ApplyBridgeColors(go, asset, isRemote);
        }
    }

    /// Destroy old mounts, then spawn and configure bridge cosmetics on the death head.
    internal void Remount()
    {
        ClearMounts();
        if (_deathHead == null) return;

        var mainPc = _deathHead.playerCosmetics;
        if (mainPc == null) return;

        // Resolve remote actor (for multiplayer: read override data from CustomizerSync).
        bool isRemote = AvatarIdentity.TryGetRemoteActor(mainPc, out int actorNumber);

        // Death head's own PlayerCosmetics for cosmetic anchor information.
        var dhPc = _deathHead.GetComponentInChildren<PlayerCosmetics>(includeInactive: true);

        // Mount list comes from the OWNER's live-avatar PlayerCosmetics: MoreHeadCosmeticMountPatch deactivates the death-head pc's duplicate bridge instances, so its own list is incomplete.
        var sourcePc = (_deathHead.playerAvatar != null ? _deathHead.playerAvatar.playerCosmetics : null)
                       ?? mainPc;

        var toMount = CollectBridgeAssets(sourcePc, isRemote, actorNumber);
        foreach (var asset in toMount)
            MountSingle(asset, dhPc, mainPc, isRemote, actorNumber);

        // Re-run ConditionsSetup so offset conditions on mounted GOs are detected.
        MoreHeadCosmeticMountPatch.InvokeConditionsSetup(mainPc);

        // Trigger can fire without a following SetupColorsLogic, so apply per-cosmetic colours here too (RecolorMountedBridge covers the colour-change path).
        RecolorVanillaDeathHead(mainPc, isRemote);

        // Show-on-Death-Head for modded non-bridge cosmetics is re-evaluated every remount (not at instantiation) so the toggle works both ways — instances are reused across deaths.
        ApplyModdedDeathHeadVisibility(mainPc, isRemote, actorNumber);
    }

    // Active state per Show-on-Death-Head toggle, modded non-bridge only; bridge is owned by our mount, vanilla never touched.
    private static void ApplyModdedDeathHeadVisibility(PlayerCosmetics pc, bool isRemote, int actorNumber)
    {
        void Process(GameObject? go, CosmeticAsset? asset)
        {
            if (go == null || asset == null || !BridgeIds.IsModdedCosmetic(asset)) return;
            bool show = !IsHiddenOnDeathHead(asset, isRemote, actorNumber);
            if (go.activeSelf != show) go.SetActive(show);
        }

        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(pc);
        if (equipped != null)
            foreach (var c in equipped)
                Process(c != null ? c.gameObject : null,
                        c != null ? MoreHeadCosmeticMountPatch.GetCosmeticAsset(c) : null);
    }

    /// Destroy all mounted bridge GOs and re-run ConditionsSetup to clean up.
    internal void ClearMounts()
    {
        foreach (var go in _mounted)
        {
            if (go == null) continue;
            // Deactivate BEFORE the deferred Destroy so Cosmetic.Update stops immediately — otherwise one extra frame throws hundreds of NREs during level transitions.
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        _mounted.Clear();

        if (_deathHead?.playerCosmetics != null)
            MoreHeadCosmeticMountPatch.InvokeConditionsSetup(_deathHead.playerCosmetics);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static List<CosmeticAsset> CollectBridgeAssets(PlayerCosmetics mainPc, bool isRemote, int actorNumber)
    {
        var result = new List<CosmeticAsset>();

        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(mainPc);
        if (equipped != null)
        {
            foreach (var c in equipped)
            {
                if (c == null) continue;
                var asset = MoreHeadCosmeticMountPatch.GetCosmeticAsset(c);
                if (!IsEligible(asset, isRemote, actorNumber)) continue;
                result.Add(asset!);
            }
        }

        return result;
    }

    private static bool IsEligible(CosmeticAsset? asset, bool isRemote, int actorNumber)
        => asset != null
           && BridgeIds.IsBridgeAsset(asset)
           && DeathHeadPrefabProvider.SupportedTypes.Contains(EffectiveType(asset, isRemote, actorNumber))
           && !IsHiddenOnDeathHead(asset, isRemote, actorNumber);

    // The asset's type AS WORN by this death head's owner. Remote: their broadcast Type override (or pre-override fallback — asset.type may carry the VIEWER's own override, which must not decide what shows on another player's death head). Local: asset.type (already carries the local override).
    private static SemiFunc.CosmeticType EffectiveType(CosmeticAsset asset, bool isRemote, int actorNumber)
    {
        if (!isRemote) return asset.type;
        CustomizerSync.TryGetRemote(actorNumber, asset.assetId, out var rd);
        return MoreHeadCosmeticMountPatch.GetRemoteEffectiveType(asset, rd).cosmeticType;
    }

    // True when toggled off for the in-game death head — remote: synced override; local: CustomizerStore store. Absent/null/true → shown.
    internal static bool IsHiddenOnDeathHead(CosmeticAsset asset, bool isRemote, int actorNumber)
    {
        if (isRemote)
            return CustomizerSync.TryGetRemote(actorNumber, asset.assetId, out var d)
                   && d?.ShowOnDeathHead == false;
        return CustomizerStore.IsHiddenOnDeathHead(asset.assetId);
    }

    private void MountSingle(CosmeticAsset asset, PlayerCosmetics? dhPc,
                              PlayerCosmetics mainPc, bool isRemote, int actorNumber)
    {
        var prefab = asset.prefab?.Prefab;
        if (prefab == null) return;

        try
        {
            // ── Resolve anchor ────────────────────────────────────────────────
            // Anchor by the OWNER's effective type, not the shared asset.type (remote Type leak).
            var effectiveType = EffectiveType(asset, isRemote, actorNumber);
            Transform? anchor = null;
            if (dhPc?.cosmeticParents != null)
            {
                foreach (var cp in dhPc.cosmeticParents)
                {
                    if (cp.cosmeticType == effectiveType && cp.parent != null)
                    { anchor = cp.parent; break; }
                }
            }
            anchor ??= _deathHead!.transform; // fallback to death head root

            // ── Instantiate and mount ─────────────────────────────────────────
            var go = UnityEngine.Object.Instantiate(prefab, anchor, worldPositionStays: false);
            MoreHeadCosmeticMountPatch.Mount(go, anchor, prefab);

            // ── Fixes, tinting, colors ────────────────────────────────────────
            // Remote wearers: their broadcast data, never the viewer's local overrides.
            BridgeSyncPayload? remoteData = null;
            if (isRemote)
                CustomizerSync.TryGetRemote(actorNumber, asset.assetId, out remoteData);

            CosmeticPrefabFixer.FixInstance(go, asset.assetId,
                isRemote ? remoteData?.FixAnimation : null, isRemote);

            bool? tintableOverride = isRemote
                ? remoteData?.Tintable ?? CustomizerStore.GetRemoteFallbackTintable(asset)
                : (bool?)null;
            if (tintableOverride != false)
                BridgeTintHelper.InjectBridgeTintMaterials(go, asset, tintableOverride);
            ApplyBridgeColors(go, asset, isRemote);

            // ── Offsets and conditions ────────────────────────────────────────
            List<CosmeticOffsetEntry>? offsets = null;
            List<CosmeticCustomCondition.Type>? customTypes = null;
            DeathHeadFloorPose? floorPose = null;

            if (isRemote)
            {
                offsets     = remoteData?.Offsets;
                customTypes = remoteData?.CustomTypes;
                floorPose   = remoteData?.FloorPose;
            }
            else if (CustomizerStore.TryGet(asset.assetId, out var localData))
            {
                offsets     = localData?.Offsets;
                customTypes = localData?.CustomTypes;
                floorPose   = localData?.FloorPose;
            }

            // Inject ALL configured offsets, not just Player_DeathHead: head-shape conditions are active on the death-head model exactly like on the live avatar. Conditions that never activate there are harmless.
            // Ordering matters: ConditionUpdateAll picks the FIRST active offset and Player_DeathHead is ALWAYS active here — put it LAST so specific shape conditions win (OrderBy is stable).
            var dhOffsets = offsets is { Count: > 0 }
                ? offsets
                    .OrderBy(o => o.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead ? 1 : 0)
                    .ToList()
                : null;

            MoreHeadCosmeticMountPatch.InjectOffsetConditions(
                go, asset, mainPc, dhOffsets, customTypes);

            // ── Sway ──────────────────────────────────────────────────────────
            var swayMode = CustomizerStore.GetEffectiveSway(asset.assetId);
            if (swayMode is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong
                && go.GetComponentsInChildren<CosmeticSprings>(true).Length == 0)
            {
                var cosmetic = go.GetComponent<Cosmetic>();
                if (cosmetic != null)
                {
                    var spring = go.AddComponent<BridgeSwaySpring>();
                    spring.Init(cosmetic, CosmeticSwayHelper.SwayModeToFactor(swayMode));
                }
            }

            // ── Impact reaction (configured "blocked" pose) ───────────────────
            // "React when dead" (or legacy "React to Floor") → lerp to the impact pose on ground/wall contact (bridge equivalent of CosmeticBlocked). LateUpdate so it layers over offsets.
            if (_deathHead != null && floorPose != null
                && (floorPose.ReactWhenDead || floorPose.Enabled == true))
                go.AddComponent<BridgeDeathHeadBlocked>().Init(_deathHead, go.transform, floorPose);

            _mounted.Add(go);
        }
        catch (Exception ex)
        {
            BridgeLog.Trace(
                $"BridgeDeathHeadGameplayMount: failed to mount '{asset.assetId}': {ex.Message}");
        }
    }

    // Colours a mounted bridge cosmetic like the live avatar: override → apply (per-slot, then whole-asset); none → RestoreOriginalColor. Local overrides come from the PerCosmeticColors store; remote from the OWNER's sync component, never the death-head's own (empty) one.
    private void ApplyBridgeColors(GameObject go, CosmeticAsset asset, bool isRemote)
    {
        var btms = go.GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true);
        if (btms.Length == 0) return;

        string assetId = asset.assetId;
        var ownerSync = isRemote ? ResolveOwnerSync() : null;

        foreach (var btm in btms)
        {
            if (btm == null) continue;
            bool applied = isRemote
                ? ownerSync != null && ownerSync.ApplyRemoteToBridgeTint(btm, assetId)
                : PerCosmeticColors.ApplyLocalToBridgeTint(btm, assetId);
            if (!applied)
                btm.RestoreOriginalColor();
        }

        // Death-head mounts live outside any PlayerAvatarVisuals tree, so neither ColorAnimatorRefresher nor remote sync reaches them — attach the animator here; the static colour above is its per-frame base.
        AttachOrRefreshAnimator(go, asset, isRemote, ownerSync);
    }

    // Adds / re-binds / removes a BridgeColorAnimator on the mounted cosmetic based on whether it's animated (local store for local death heads, owner's synced specs for remote ones).
    private static void AttachOrRefreshAnimator(
        GameObject go, CosmeticAsset asset, bool isRemote, PerCosmeticColorSyncComponent? ownerSync)
    {
        AnimSet set = default;
        if (PerCosmeticColors.FeatureEnabled && Plugin.EnableBridgeColorAnimations.Value)
            set = isRemote
                ? (ownerSync != null ? ownerSync.GetRemoteAnimSet(asset.assetId) : default)
                : PerCosmeticColors.GetAnimSet(asset.assetId);

        // Host the animator on the mount root and scan it for tint materials, so BTMs are found wherever the Cosmetic component sits relative to them in the mounted hierarchy.
        var existing = go.GetComponent<BridgeColorAnimator>();
        if (set.Any)
        {
            var anim = existing ?? go.AddComponent<BridgeColorAnimator>();
            anim.Init(go, set);
            if (anim.IsEmpty) anim.Stop();
        }
        else if (existing != null)
            existing.Stop();
    }

    // Re-applies per-cosmetic colours to the death-head's own vanilla PlayerMaterials — local store for local death heads, owner's synced data for remote ones.
    private void RecolorVanillaDeathHead(PlayerCosmetics deathHeadPc, bool isRemote)
    {
        var pms = deathHeadPc.playerMaterials;
        if (pms == null) return;

        var ownerSync = isRemote ? ResolveOwnerSync() : null;
        if (isRemote)
            ownerSync?.ApplyVanillaOverridesTo(pms);
        else
            PerCosmeticColors.ApplyVanillaOverridesTo(pms);

        // The death head keeps its OWN base meshes (e.g. the little legs — the leg cosmetic mounts on the avatar, not here), so a base PM with an equipped cosmetic of its type must mimic THAT cosmetic's colour, not the "__base__" custom applied above — matching the live avatar and the Mini-Me. Owner-authoritative: remote reads ONLY the owner's synced data, local ONLY the local store (this branch runs only on our own death head). Isolated to deathHeadPc, never the shared avatar PMs.
        var colors = MetaManager.instance?.colors;
        if (colors == null) return;
        int[] equipped = deathHeadPc.colorsEquipped;
        foreach (var pm in pms)
        {
            if (pm == null || pm.cosmetic != null || !pm.tintable) continue;
            string? cosId = FindEquippedAssetId(deathHeadPc, pm.cosmeticType);
            if (cosId == null) continue;
            int ti = (int)pm.cosmeticType;
            int fallback = (equipped != null && ti >= 0 && ti < equipped.Length) ? equipped[ti] : -1;

            if (isRemote)
            {
                if (ownerSync == null) continue;
                if (ownerSync.TryGetRemoteCustomColor(cosId, out var rcc))
                    VanillaTintHelper.ApplyCustomRGB(pm, rcc);
                else
                    ApplyIndex(pm, ownerSync.GetEffectiveRemoteColorIndex(cosId, fallback), colors.Count);
            }
            else if (Plugin.EnableVanillaCustomColors.Value && PerCosmeticColors.TryGetCustomColor(cosId, out var cc))
                VanillaTintHelper.ApplyCustomRGB(pm, cc);
            else
                ApplyIndex(pm, PerCosmeticColors.GetEffectiveColorIndex(cosId, fallback), colors.Count);
        }
    }

    // Palette index onto one PM (skips invalid index). Mirrors DeathHeadColorizer.ApplyColor.
    private static void ApplyIndex(PlayerMaterial pm, int colorIdx, int colorCount)
    {
        if (colorIdx < 0 || colorIdx >= colorCount) return;
        pm.Setup();
        pm.ColorSet(PerCosmeticColors.PropAlbedo, PerCosmeticColors.PropEmission,
                    PerCosmeticColors.PropFresnel, colorIdx);
    }

    // The assetId of the first equipped cosmetic of the given type, or null.
    private static string? FindEquippedAssetId(PlayerCosmetics pc, SemiFunc.CosmeticType type)
    {
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

    // Synced colour data lives on the OWNING player's live-avatar PlayerCosmetics — resolve it through PlayerDeathHead.playerAvatar.
    private PerCosmeticColorSyncComponent? ResolveOwnerSync()
    {
        var ownerPc = _deathHead != null && _deathHead.playerAvatar != null
            ? _deathHead.playerAvatar.playerCosmetics
            : null;
        return ownerPc != null ? ownerPc.GetComponent<PerCosmeticColorSyncComponent>() : null;
    }

    private void OnDestroy()
    {
        foreach (var go in _mounted)
        {
            if (go == null) continue;
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        _mounted.Clear();
    }
}
