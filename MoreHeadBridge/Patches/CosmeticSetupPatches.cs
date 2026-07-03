using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// ── CosmeticSetupPatch ────────────────────────────────────────────────────────
// Runs just before Cosmetic.Setup() on every bridge cosmetic.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Setup))]
internal static class CosmeticSetupPatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        if (__instance == null) return;
        var asset = __instance.cosmeticAsset;

        // ── Owner-authoritative type (fixes the shared asset.type leak) ──────────────
        // asset.type is SHARED and each viewer mutates it with their own Type override, so for a REMOTE cosmetic vanilla Setup would hide the base head mesh by the VIEWER's type, not the owner's. Re-point type/typeAsset/parent to the OWNER's resolved type BEFORE Setup. Covers bridge + modded-non-bridge; local/menu need no fix.
        if (BridgeIds.IsCustomizable(asset))
        {
            var ownerPc = __instance.playerCosmetics;
            if (ownerPc != null && AvatarIdentity.TryGetRemoteActor(ownerPc, out int ownerActor))
            {
                CustomizerSync.TryGetRemote(ownerActor, asset.assetId, out var ownerData);
                var ownerType = MoreHeadCosmeticMountPatch.GetRemoteEffectiveType(asset, ownerData).cosmeticType;
                if (ownerType != __instance.type)
                {
                    var meta = MetaManager.instance;
                    int oi = (int)ownerType;
                    if (meta?.cosmeticTypeAssets != null && oi >= 0 && oi < meta.cosmeticTypeAssets.Count)
                        __instance.cosmeticTypeAsset = meta.cosmeticTypeAssets[oi];
                    var ownerParent = ownerPc.cosmeticParents?.Find(x => x.cosmeticType == ownerType);
                    if (ownerParent != null) __instance.cosmeticParent = ownerParent;
                    __instance.type = ownerType;
                }
            }
        }

        if (!BridgeIds.IsBridgeAsset(asset)) return;

        // customTypeList can be null and CustomTypesLogic() iterates it unconditionally (NRE) — initialise it so bridge cosmetics can announce custom condition types.
        if (__instance.cosmeticTypeAsset != null
            && __instance.cosmeticTypeAsset.customTypeList == null)
            __instance.cosmeticTypeAsset.customTypeList = new List<CosmeticCustomCondition.Type>();

        // Setup() logs an error for Hat/HeadTopMesh without CosmeticPlayerCrown — add a bare one to satisfy the check. ApplyCrownConfig configures it later (targetMain null → PlayerCrown skips it, no crown by default).
        if ((__instance.type == SemiFunc.CosmeticType.Hat ||
             __instance.type == SemiFunc.CosmeticType.HeadTopMesh) &&
            __instance.GetComponentInChildren<CosmeticPlayerCrown>(includeInactive: true) == null)
        {
            __instance.gameObject.AddComponent<CosmeticPlayerCrown>();
        }

        if (__instance.meshParents == null)
            __instance.meshParents = new List<Transform>();

        // For mesh-switch types Setup() hides every meshParent and re-parents onto baseMeshParents (vanilla mesh counts); on a bridge cosmetic that hides most of it and strips its tint. Leave meshParents empty so it stays where TryMount placed it, visible and tinted.
        bool meshSwitch = __instance.cosmeticTypeAsset != null && __instance.cosmeticTypeAsset.meshSwitch;
        if (!meshSwitch && __instance.meshParents.Count == 0)
        {
            foreach (Transform child in __instance.transform)
                __instance.meshParents.Add(child);
        }
    }

    // After a mesh-SWITCH cosmetic mounts its swap meshes, re-assert part-hiding so a freshly-mounted swap stays hidden when a shrinker hides that slot (HiddenParts can't see it). Self-gates to meshSwitch; cheap no-op without a shrinker.
    [HarmonyPostfix]
    private static void Postfix(Cosmetic __instance)
    {
        if (__instance == null || __instance.cosmeticTypeAsset == null
            || !__instance.cosmeticTypeAsset.meshSwitch) return;
        var avatar = __instance.playerCosmetics != null ? __instance.playerCosmetics.playerAvatarVisuals : null;
        PartShrinkerBridge.EnforceSwapHiding(avatar);
    }
}
