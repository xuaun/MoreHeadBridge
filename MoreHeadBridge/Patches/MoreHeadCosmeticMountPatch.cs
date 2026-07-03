// Re-parent bridge cosmetics onto the same runtime anchors MoreHead uses, preserving each .hhh prefab's authored transform values.
// Vanilla parents cosmetics under its CosmeticParent anchors and resets local TRS; MoreHead .hhh prefabs are authored against MoreHead's rig, so bridge assets needing special placement are mounted here after vanilla instantiation.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static partial class MoreHeadCosmeticMountPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null) return;
        // Bridge cosmetics always; modded non-bridge only when the opt-in config is on.
        if (!BridgeIds.IsCustomizable(_cosmeticAsset)) return;
        bool isBridge = BridgeIds.IsBridgeAsset(_cosmeticAsset);

        bool isRemote = AvatarIdentity.TryGetRemoteActor(__instance, out int actor);

        BridgeSyncPayload? remoteData = null;
        if (isRemote)
            CustomizerSync.TryGetRemote(actor, _cosmeticAsset.assetId, out remoteData);

        // The death-head pc's own copy of a bridge cosmetic z-fights our mount (the "flicker") — BridgeDeathHeadGameplayMount owns it. Deactivate, do NOT Destroy: vanilla derefs the list entry on the next equip and a destroyed one NPEs (locks the menu). Modded non-bridge copies ARE legit; left alone.
        if (isBridge && __instance.playerAvatarVisuals == null && __instance.deathHead != null)
        {
            __result.SetActive(false);
            return;
        }

        // Everything downstream reads from this one resolution — never the shared CosmeticAsset.
        bool isPreview = OverridePreviewContext.IsActiveFor(__instance, _cosmeticAsset.assetId);
        var resolved = Resolve(_cosmeticAsset, isPreview, isRemote, remoteData);

        // Fixes on the live instance so overrides apply on re-equip without a restart; remote players pass their broadcasted FixAnimation so the loop override is honoured here.
        CosmeticPrefabFixer.FixInstance(__result, _cosmeticAsset.assetId,
            isRemote ? remoteData?.FixAnimation : null, isRemote);

        // Inject BridgeTintMaterial BEFORE SetupColorsLogic scans the hierarchy.
        if (resolved.Tintable != false)
            BridgeTintHelper.InjectBridgeTintMaterials(__result, _cosmeticAsset, resolved.Tintable);

        // Mount before InjectOffsetConditions so positionDefault is captured from the final transform. Bridge → bone mount (MoreHead rig); modded → vanilla anchors, re-anchored only when the effective type differs.
        if (isBridge)
            TryMount(__instance, __result, _cosmeticAsset, resolved.EffectiveType, resolved.IsWorld);
        else
            ReparentToVanillaAnchor(__instance, __result, resolved.EffectiveType);

        // Injected PER-INSTANCE (BridgeCustomTypesBroadcaster, not asset.customTypeList — the shared CosmeticAsset would bleed local config into remote cosmetics). Must precede ConditionsSetup's hierarchy scan.
        // HasRecord = an override record exists (owner took over customTypes; popup seeds from native) → suppress the native announce.
        bool suppressNativeCustomTypes = resolved.HasRecord
            && NativeCustomTypeImport.HasNativeAnnounceList(_cosmeticAsset);
        InjectOffsetConditions(__result, _cosmeticAsset, __instance, resolved.Offsets, resolved.CustomTypes, suppressNativeCustomTypes);

        // Inject CosmeticPlayerCrown for Hat/HeadTopMesh with a crown config.
        if (resolved.EffectiveType == SemiFunc.CosmeticType.Hat ||
            resolved.EffectiveType == SemiFunc.CosmeticType.HeadTopMesh)
        {
            ApplyCrownConfig(__result, resolved.Crown);
        }

        // On-body impact reaction ("React when alive"): applied to local AND remote live bodies so everyone sees it; the component self-gates to the live avatar.
        if (isBridge && resolved.FloorPose is { ReactWhenAlive: true }
            && __result.GetComponent<BridgeLiveBlocked>() == null)
        {
            var cosmeticComp = __result.GetComponent<Cosmetic>();
            if (cosmeticComp != null)
                __result.AddComponent<BridgeLiveBlocked>().Init(cosmeticComp, resolved.FloorPose);
        }

        // Hide-self conditions: works on local, menu and remote avatars (reads each avatar's own equipped cosmetics / conditions).
        if (resolved.Hide is { HasAny: true } && __result.GetComponent<BridgeHideCondition>() == null)
        {
            var cosmeticComp = __result.GetComponent<Cosmetic>();
            if (cosmeticComp != null)
                __result.AddComponent<BridgeHideCondition>().Init(cosmeticComp, resolved.Hide);
        }

        // Apply sway at mount time so configured intensity survives restarts. Remote sway isn't synced here; death-head sway lives in BridgeDeathHeadGameplayMount.MountSingle.
        if (!isRemote)
        {
            // During a preview re-instantiation, use the unsaved slider value; else the override.
            SwayMode? swayMode = isPreview && OverridePreviewContext.Data != null
                ? OverridePreviewContext.Data.EnableSway
                : CustomizerStore.GetEffectiveSway(_cosmeticAsset.assetId);
            // null (Default) → leave native CosmeticSprings on modded cosmetics untouched.
            bool hasExplicitSway = swayMode.HasValue;
            var nativeSprings = __result.GetComponentsInChildren<CosmeticSprings>(true);

            // Explicit override → suppress native springs so they don't fight BridgeSwaySpring.
            if (hasExplicitSway && nativeSprings.Length > 0)
                foreach (var cs in nativeSprings)
                    cs.enabled = false;

            if (swayMode is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong)
            {
                // Add our spring only if it's safe: no native springs, or they were just disabled.
                if (hasExplicitSway || nativeSprings.Length == 0)
                {
                    var cosmetic = __result.GetComponent<Cosmetic>();
                    if (cosmetic != null && __result.GetComponent<BridgeSwaySpring>() == null)
                    {
                        var spring = __result.AddComponent<BridgeSwaySpring>();
                        spring.Init(cosmetic, CosmeticSwayHelper.SwayModeToFactor(swayMode));
                    }
                }
            }
        }
        else if (!BridgeIds.IsBridgeAsset(_cosmeticAsset) && remoteData != null)
        {
            // Remote non-bridge modded: mirror the local sway logic from synced data (remote bridge cosmetics go through BridgeSwaySpringInjectPatch instead).
            SwayMode? remoteSwayMode = remoteData.EnableSway;
            bool hasExplicitRemoteSway = remoteSwayMode.HasValue;
            var nativeSprings = __result.GetComponentsInChildren<CosmeticSprings>(true);

            if (hasExplicitRemoteSway && nativeSprings.Length > 0)
                foreach (var cs in nativeSprings)
                    cs.enabled = false;

            if (remoteSwayMode is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong
                && (hasExplicitRemoteSway || nativeSprings.Length == 0))
            {
                var cosmetic = __result.GetComponent<Cosmetic>();
                if (cosmetic != null && __result.GetComponent<BridgeSwaySpring>() == null)
                {
                    var spring = __result.AddComponent<BridgeSwaySpring>();
                    spring.Init(cosmetic, CosmeticSwayHelper.SwayModeToFactor(remoteSwayMode));
                }
            }
        }
    }

    // Everything the mount pipeline needs about one cosmetic instance, resolved ONCE from whichever
    // source applies (preview / remote / local). Downstream code reads THIS, never the shared
    // CosmeticAsset — asset.type/tintable/WorldAssetIds may carry the viewer's own overrides, which
    // must not leak onto another player's cosmetic.
    private readonly struct ResolvedOverride
    {
        public readonly List<CosmeticOffsetEntry>? Offsets;
        public readonly List<CosmeticCustomCondition.Type>? CustomTypes;
        public readonly CosmeticCrownConfig? Crown;
        public readonly CosmeticHideConfig? Hide;
        public readonly DeathHeadFloorPose? FloorPose;
        public readonly bool HasRecord;   // an override record exists (preview edit / saved / synced)

        public readonly SemiFunc.CosmeticType EffectiveType;   // wearer's broadcast/pre-override type for remotes; asset.type otherwise
        public readonly bool IsWorld;
        public readonly bool? Tintable;   // remote: wearer's broadcast Tintable (or pre-override default); local/preview: null (asset flag applies)

        public ResolvedOverride(List<CosmeticOffsetEntry>? offsets, List<CosmeticCustomCondition.Type>? customTypes,
            CosmeticCrownConfig? crown, CosmeticHideConfig? hide, DeathHeadFloorPose? floorPose, bool hasRecord,
            SemiFunc.CosmeticType effectiveType, bool isWorld, bool? tintable)
        {
            Offsets = offsets; CustomTypes = customTypes; Crown = crown;
            Hide = hide; FloorPose = floorPose; HasRecord = hasRecord;
            EffectiveType = effectiveType; IsWorld = isWorld; Tintable = tintable;
        }
    }

    // ADD-OVERRIDE-FIELD: a per-instance override field consumed at mount time is resolved here for all three
    // sources (preview / remote / local) — add it to ResolvedOverride and to each branch below.
    private static ResolvedOverride Resolve(CosmeticAsset asset, bool isPreview, bool isRemote, BridgeSyncPayload? remoteData)
    {
        // Effective type/world/tintable: remote instances never read the (possibly viewer-mutated)
        // shared asset state; local/preview use it directly (RefreshFull already set asset.type for previews).
        var (effectiveType, isWorld) = isRemote
            ? GetRemoteEffectiveType(asset, remoteData)
            : (asset.type, HhhCosmeticLoader.IsWorldAsset(asset));
        bool? tintable = isRemote
            ? remoteData?.Tintable ?? CustomizerStore.GetRemoteFallbackTintable(asset)
            : (bool?)null;

        if (isPreview)
        {
            // Live-preview path: pending data from CosmeticOverridePreview.
            var p = OverridePreviewContext.Data;
            if (p != null)
                return new ResolvedOverride(p.Offsets, p.CustomTypes, p.Crown, p.HideConditions, p.FloorPose, hasRecord: true,
                    effectiveType, isWorld, tintable);

            // Initial preview pass (RefreshFull(null)): show the SAVED override so the avatar opens reflecting it. Live edits always pass non-null pending data, so this never interferes.
            CustomizerStore.TryGet(asset.assetId, out var saved);
            return new ResolvedOverride(saved?.Offsets, saved?.CustomTypes, saved?.Crown, saved?.HideConditions, saved?.FloorPose, hasRecord: saved != null,
                effectiveType, isWorld, tintable);
        }
        if (isRemote)
            return new ResolvedOverride(remoteData?.Offsets, remoteData?.CustomTypes, remoteData?.Crown, remoteData?.HideConditions, remoteData?.FloorPose, hasRecord: remoteData != null,
                effectiveType, isWorld, tintable);

        CustomizerStore.TryGet(asset.assetId, out var local);
        return new ResolvedOverride(local?.Offsets, local?.CustomTypes, local?.Crown, local?.HideConditions, local?.FloorPose, hasRecord: local != null,
            effectiveType, isWorld, tintable);
    }

    /// World membership for an asset worn by <paramref name="remoteActor"/> (0/-1 = local, menu or
    /// preview): remote resolves from the owner's broadcast (never the viewer-mutated shared state).
    internal static bool IsWorldFor(CosmeticAsset asset, int remoteActor)
    {
        if (remoteActor > 0)
        {
            CustomizerSync.TryGetRemote(remoteActor, asset.assetId, out var rd);
            return GetRemoteEffectiveType(asset, rd).isWorld;
        }
        return HhhCosmeticLoader.IsWorldAsset(asset);
    }

    // Effective (type, world) for a REMOTE instance: the wearer's broadcast Type override when sent, else the asset's PRE-OVERRIDE defaults — asset.type/WorldAssetIds may carry the viewer's own override, which must not leak onto another player's cosmetic.
    internal static (SemiFunc.CosmeticType cosmeticType, bool isWorld) GetRemoteEffectiveType(
        CosmeticAsset asset, BridgeSyncPayload? remoteData)
        => remoteData?.Type != null
            ? CustomizerStore.MapOverrideToVanilla(remoteData.Type.Value)
            : CustomizerStore.GetRemoteFallbackType(asset);

    // Extracted mounting logic — no early returns that would skip InjectOffsetConditions.
    // effectiveType/isWorld come pre-resolved from ResolvedOverride (never the shared asset state).
    private static void TryMount(PlayerCosmetics instance, GameObject result, CosmeticAsset asset,
                                  SemiFunc.CosmeticType effectiveType, bool isWorld)
    {
        if (instance == null || instance.playerAvatarVisuals == null) return;

        var sourcePrefab = asset.prefab?.Prefab;
        if (sourcePrefab == null) return;

        if (isWorld)
        {
            MountWorldCosmetic(result, instance.playerAvatarVisuals.transform, sourcePrefab, asset.assetId);
            return;
        }

        string? targetBone;
        if (effectiveType == SemiFunc.CosmeticType.ArmRight) targetBone = "ANIM ARM R SCALE";
        else if (effectiveType == SemiFunc.CosmeticType.ArmLeft) targetBone = "code_arm_l";
        else
        {
            // Hat, HeadBottom, etc.: vanilla already parented per asset.type — re-anchor only when the
            // effective type differs (a remote wearer's synced override; locals always match asset.type).
            if (effectiveType != asset.type)
                ReparentToVanillaAnchor(instance, result, effectiveType);
            return;
        }

        var bone = FindByName(instance.playerAvatarVisuals.transform, targetBone);
        if (bone != null)
            Mount(result, bone, sourcePrefab);
    }

    // Parents an instance under the vanilla anchor for the given type, mirroring vanilla's TRS reset.
    private static void ReparentToVanillaAnchor(PlayerCosmetics instance, GameObject result,
                                                SemiFunc.CosmeticType effectiveType)
    {
        if (instance?.cosmeticParents == null) return;

        var parent = instance.cosmeticParents.Find(p => p.cosmeticType == effectiveType);
        if (parent?.parent == null) return;

        result.transform.SetParent(parent.parent, worldPositionStays: false);
        if (parent.resetTransform)
        {
            result.transform.localPosition = Vector3.zero;
            result.transform.localRotation = Quaternion.identity;
            result.transform.localScale = Vector3.one;
        }
    }

    // ── Helpers exposed for CosmeticOverridePreview ──────────────────────────

    internal static List<Cosmetic>? GetEquippedCosmetics(PlayerCosmetics pc)
        => _cosmeticEquippedField?.GetValue(pc) as List<Cosmetic>;

    internal static CosmeticAsset? GetCosmeticAsset(Cosmetic c)
        => _cosmeticAssetField?.GetValue(c) as CosmeticAsset;

    internal static void InvokeConditionsSetup(PlayerCosmetics pc)
        => _conditionsSetupMethod?.Invoke(pc, null);

    // SetupColorsLogic(pc.colorsEquipped) directly, NOT SetupColors(): that reads the LOCAL player's MetaManager.colorsEquipped (wrong for remote PCs) and may collapse per-type colours via SetupColorsAllLogic.
    internal static void InvokeSetupColorsLogic(PlayerCosmetics pc)
    {
        if (pc?.colorsEquipped == null) return;
        _setupColorsLogicMethod?.Invoke(pc, new object[] { pc.colorsEquipped });
    }

    /// Resets a CosmeticOffsetCondition's transform to its captured defaults then destroys the component.
    /// DestroyImmediate so callers' immediate ConditionsSetup rebuild doesn't re-list a deferred-doomed component.
    internal static void ResetAndDestroy(Transform t, CosmeticOffsetCondition comp)
    {
        ResetTransformToDefault(t, new[] { comp });
        Object.DestroyImmediate(comp);
    }

    /// Resets the transform to the FIRST component's captured defaults — the clean baseline; later
    /// duplicates captured theirs while an offset was applied — then destroys every component.
    internal static void ResetAndDestroyAll(Transform t, CosmeticOffsetCondition[] comps)
    {
        ResetTransformToDefault(t, comps);
        foreach (var c in comps) Object.DestroyImmediate(c);
    }

}
