// Offset/crown geometry for bridge cosmetics: builds the runtime CosmeticOffsetCondition (with mesh-centre re-basing for "fit" offsets) and the Crown Target Bridge / CosmeticPlayerCrown. Partial of MoreHeadCosmeticMountPatch — see the core file for shared state.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static partial class MoreHeadCosmeticMountPatch
{
    // Injects CosmeticOffsetCondition and/or BridgeCustomTypesBroadcaster. ownerPc is for the broadcaster (PlayerCosmetics isn't in the cosmetic's hierarchy); null/empty inputs are skipped.
    // suppressNativeCustomTypes: inject the broadcaster even with an empty set, flagged to suppress the cosmetic's native announce.
    internal static void InjectOffsetConditions(
        GameObject instance,
        CosmeticAsset asset,
        PlayerCosmetics? ownerPc,
        List<CosmeticOffsetEntry>? offsets,
        List<CosmeticCustomCondition.Type>? customTypes = null,
        bool suppressNativeCustomTypes = false)
    {
        // Seed built-in shape "fit" offsets so bridge cosmetics resize to the player's shape like vanilla. Worlds excluded (Hat-typed but not head-attached); user offsets always win per trigger.
        // Gated by the effective "use fit offsets" choice — pending value during a live preview, else the per-cosmetic override or the global default.
        bool useFit = OverridePreviewContext.IsActiveFor(ownerPc, asset.assetId)
            ? (OverridePreviewContext.Data?.UseFitOffsets ?? Plugin.UseVanillaPositionFixes.Value)
            : CustomizerStore.GetEffectiveUseFitOffsets(asset.assetId);
        if (useFit
            && BridgeIds.IsBridgeAsset(asset)
            && !HhhCosmeticLoader.IsWorldAsset(asset)
            && OffsetSeedDefaults.HasDefaults(asset.type))
        {
            offsets = OffsetSeedDefaults.MergeInto(asset.type, offsets);
        }

        if (offsets is not { Count: > 0 } && customTypes is not { Count: > 0 } && !suppressNativeCustomTypes) return;

        if (offsets is { Count: > 0 })
        {
            // SEEDED fit-offsets are generic vanilla AVERAGES (origin-anchored, scale ~1), and vanilla CosmeticOffsetCondition snaps to offset.* as ABSOLUTE root-local pos/rot/scale — applied verbatim to a .hhh authored away from its pivot, that threw it across the body and resized it. Re-base seeds onto THIS cosmetic so the fit scales/rotates AROUND the mesh CENTRE (not the root pivot): keep the mesh centre fixed, multiply scale, compose rotation as a quaternion. Origin-anchored cosmetics (centre ≈ pivot) → effectively a no-op; user offsets stay absolute.
            Vector3    d0Pos   = instance.transform.localPosition;
            Quaternion d0Rot   = instance.transform.localRotation;
            Vector3    d0Scale = instance.transform.localScale;

            // Mesh centre in the root's local frame, from the PREFAB: Setup zeroes the live mesh children's scale (equip-anim start) right before this runs, so live bounds are useless.
            Vector3 cRoot = Vector3.zero;
            if (HasFit(offsets) && asset.prefab?.Prefab is { } prefab)
                TryGetMeshCenterRootLocal(prefab, out cRoot);
            // Same centre expressed in the PARENT's local space at default — held put across the fit.
            Vector3 cPar = d0Pos + d0Rot * Vector3.Scale(d0Scale, cRoot);

            var comp = instance.AddComponent<CosmeticOffsetCondition>();
            comp.offsets = new List<CosmeticOffsetCondition.Offset>();

            foreach (var e in offsets)
            {
                Vector3 pos = new Vector3(e.PosX, e.PosY, e.PosZ);
                Vector3 rot = new Vector3(e.RotX, e.RotY, e.RotZ);
                Vector3 scl = new Vector3(e.ScaleX, e.ScaleY, e.ScaleZ);
                // Fit triggers (seed OR user) are RELATIVE fit values, re-based around the mesh centre.
                if (OffsetSeedDefaults.IsFitTrigger(e.TriggerType))
                {
                    Quaternion rf = d0Rot * Quaternion.Euler(rot);     // tilt in the cosmetic's own frame
                    Vector3    sf = Vector3.Scale(d0Scale, scl);        // multiply the authored scale
                    // Solve the root position so the scaled+rotated mesh centre lands back on cPar, then add the seed's small positional nudge.
                    pos = cPar - rf * Vector3.Scale(sf, cRoot) + pos;
                    rot = rf.eulerAngles;
                    scl = sf;
                }

                comp.offsets.Add(new CosmeticOffsetCondition.Offset
                {
                    setupName = e.TriggerType.ToString(),
                    // Only customTypes is used — keep all other trigger lists empty.
                    cosmeticTypes = new List<SemiFunc.CosmeticType>(),
                    customTypes = new List<CosmeticCustomCondition.Type> { e.TriggerType },
                    playerPoses = new List<PlayerAvatarVisuals.Pose>(),
                    cosmetics = new List<CosmeticAsset>(),
                    position = pos,
                    rotation = rot,
                    scale = scl,
                    lerpSpeed = Mathf.Max(0.1f, e.LerpSpeed),
                });
            }

            // Non-bridge modded: absorb the cosmetic's own native offset condition so two don't fight the transform.
            if (!BridgeIds.IsBridgeAsset(asset))
                NativeOffsetImport.AbsorbAndStrip(instance, comp);
        }

        if (customTypes is { Count: > 0 } || suppressNativeCustomTypes)
        {
            var broadcaster = instance.AddComponent<BridgeCustomTypesBroadcaster>();
            broadcaster.Types = customTypes is { Count: > 0 }
                ? new List<CosmeticCustomCondition.Type>(customTypes)
                : new List<CosmeticCustomCondition.Type>();
            broadcaster.OwnerPc = ownerPc;
            broadcaster.SuppressNative = suppressNativeCustomTypes;

            // Prime conditions now so the ConditionsSetup that runs right after finds them; otherwise they'd only appear next frame and offsets stay null until then.
            if (ownerPc != null && customTypes is { Count: > 0 })
            {
                foreach (var t in customTypes)
                    PrimeCustomCondition(ownerPc, t);
            }

            // Non-bridge: absorb native condition COMPONENTS (rare); the asset-list source is handled by SuppressNative.
            NativeCustomTypeImport.AbsorbAndStrip(instance, asset, broadcaster);
        }
    }

    private static readonly FieldInfo? _conditionsCustomField =
        AccessTools.Field(typeof(PlayerCosmetics), "conditionsCustom");
    private static readonly FieldInfo? _hccTimerField =
        AccessTools.Field(typeof(PlayerCosmetics.HideConditionCustom), "timer");

    private static void PrimeCustomCondition(PlayerCosmetics pc, CosmeticCustomCondition.Type type)
    {
        if (_conditionsCustomField?.GetValue(pc) is not System.Collections.IList list) return;
        foreach (var e in list)
            if (e is PlayerCosmetics.HideConditionCustom h && h.type == type)
            {
                _hccTimerField?.SetValue(h, 0.1f);
                return;
            }
        var entry = new PlayerCosmetics.HideConditionCustom { type = type };
        _hccTimerField?.SetValue(entry, 0.1f);
        list.Add(entry);
    }

    private static bool HasFit(List<CosmeticOffsetEntry> list)
    {
        foreach (var e in list) if (OffsetSeedDefaults.IsFitTrigger(e.TriggerType)) return true;
        return false;
    }

    // Combined renderer-mesh centre of a prefab in the prefab ROOT's local frame, from sharedMesh.bounds + authored child transforms (no instantiation, immune to runtime scale zeroing). False when the prefab has no renderable mesh (centre left at zero).
    private static bool TryGetMeshCenterRootLocal(GameObject root, out Vector3 center)
    {
        center = Vector3.zero;
        Matrix4x4 rootInv = root.transform.worldToLocalMatrix;
        Bounds b = default;
        bool any = false;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf != null && mf.sharedMesh != null)
                EncapsulateMesh(ref b, ref any, rootInv * mf.transform.localToWorldMatrix, mf.sharedMesh);
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != null && smr.sharedMesh != null)
                EncapsulateMesh(ref b, ref any, rootInv * smr.transform.localToWorldMatrix, smr.sharedMesh);

        if (any) center = b.center;
        return any;
    }

    private static void EncapsulateMesh(ref Bounds b, ref bool any, Matrix4x4 toRoot, Mesh mesh)
    {
        Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = toRoot.MultiplyPoint3x4(c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z));
            if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
            else b.Encapsulate(p);
        }
    }

    /// Injects/updates a CosmeticPlayerCrown on a Hat/HeadTopMesh instance, with a
    /// "Crown Target Bridge" child at the configured pos/rot/scale. Null crown removes it.
    /// Runs in the InstantiateCosmetic Postfix, before PlayerCrown.UpdateTarget scans cosmetics.
    internal static void ApplyCrownConfig(GameObject instance, CosmeticCrownConfig? crown)
    {
        const string bridgeTargetName = "Crown Target Bridge";

        if (crown == null)
        {
            var bridgeTarget = instance.transform.Find(bridgeTargetName);
            if (bridgeTarget != null)
            {
                var crownComp = instance.GetComponentInChildren<CosmeticPlayerCrown>(includeInactive: true);
                if (crownComp != null && crownComp.targetMain == bridgeTarget)
                    crownComp.targetMain = null;
                Object.Destroy(bridgeTarget.gameObject);
            }
            return;
        }

        var targetTr = instance.transform.Find(bridgeTargetName);
        if (targetTr == null)
        {
            var targetGO = new GameObject(bridgeTargetName);
            targetGO.transform.SetParent(instance.transform, false);
            targetTr = targetGO.transform;
        }

        targetTr.localPosition = new Vector3(crown.PosX, crown.PosY, crown.PosZ);
        targetTr.localRotation = Quaternion.Euler(crown.RotX, crown.RotY, crown.RotZ);
        targetTr.localScale = new Vector3(crown.ScaleX, crown.ScaleY, crown.ScaleZ);

        var comp = instance.GetComponentInChildren<CosmeticPlayerCrown>(includeInactive: true);
        if (comp == null)
            comp = instance.AddComponent<CosmeticPlayerCrown>();

        comp.targetMain = targetTr;
        comp.priority = crown.Priority;
        comp.disableSpring = crown.DisableSpring;
    }

    internal static void Mount(GameObject instance, Transform parent, GameObject sourcePrefab)
    {
        instance.transform.SetParent(parent, worldPositionStays: false);
        instance.transform.localPosition = sourcePrefab.transform.localPosition;
        instance.transform.localRotation = sourcePrefab.transform.localRotation;
        instance.transform.localScale = sourcePrefab.transform.localScale;
    }
}
