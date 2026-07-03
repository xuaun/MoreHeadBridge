using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// After SetupColorsLogic: 1) type colours onto BridgeTintMaterials (bridge bypasses vanilla's PlayerMaterial loop), 2) per-cosmetic overrides on top, 3) attach/detach BridgeColorAnimators — 2 and 3 local/menu only.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class SetupColorsLogicOverridePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        BridgeTintHelper.ApplyTypeColors(__instance);
        PerCosmeticColors.ApplyOverrides(__instance);
        ColorAnimatorRefresher.RefreshLiveAnimators(__instance);
    }
}

// Runs after SetupColors returns (covers both SetupColorsAllLogic and SetupColorsLogic paths): re-applies palette/custom to every base-mesh PM so Paint All / Body always updates Default meshes.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColors")]
internal static class SetupColorsBaseMeshPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        VanillaTintHelper.ReapplyBaseMeshColors(__instance);
    }
}

// Same as above for SetupColorsAllLogic (called when every type shares the same colour).
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsAllLogic")]
internal static class SetupColorsAllLogicOverridePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        BridgeTintHelper.ApplyTypeColors(__instance);
        PerCosmeticColors.ApplyOverrides(__instance);
        ColorAnimatorRefresher.RefreshLiveAnimators(__instance);
    }
}

// Ensures every animated bridge cosmetic on the LOCAL/MENU avatar carries a live BridgeColorAnimator, and cosmetics without an animation spec don't.
// Runs after ApplyTypeColors + ApplyOverrides set the static colour, so removing an animator leaves the cosmetic on its correct static colour. Remote avatars handled by PerCosmeticColorSyncComponent.
internal static class ColorAnimatorRefresher
{
    // Local / menu avatar entry point: animation specs come from the local PerCosmeticColors store.
    internal static void RefreshLiveAnimators(PlayerCosmetics pc)
    {
        var visuals = pc?.playerAvatarVisuals;
        if (visuals == null) return;
        if (!visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true) return;

        // A remote player's Mini-Semibot passes the menu/local gate but its animators come from the OWNER's synced specs (RemoteColorSync), not the local store — skip it here.
        if (AvatarIdentity.IsRemoteMini(pc)) return;

        // RandomPreset mini animators come from the PRESET — if reached outside the preset context, re-establish it so the preset's specs drive the mini, not the live store.
        if (!PerCosmeticColors.MiniPresetContextActive)
        {
            int presetSlot = MiniSemibotSpawner.PresetSlotOf(pc);
            if (presetSlot >= 0)
            {
                PerCosmeticColors.RunWithPresetContext(presetSlot, () => RefreshLiveAnimators(pc!));
                return;
            }
        }

        // Feature gate: when the per-cosmetic colour system is off, strip any animators and stop.
        if (!PerCosmeticColors.FeatureEnabled)
        {
            Apply(pc!, _ => default);
            return;
        }

        // Per-asset gate: animates only when the effective "Animated Colours" is on (override, else global); disabled assets resolve to an empty set and get stripped. During a preset-hover preview, the previewed specs take priority over the live store.
        System.Func<string, AnimSet> baseLookup = PerCosmeticColors.PresetPreviewActive
            ? PerCosmeticColors.GetPreviewAnimSet
            : PerCosmeticColors.GetAnimSet;
        Apply(pc!, id => CustomizerStore.GetEffectiveColorAnimations(id) ? baseLookup(id) : default);
    }

    // Clears the animation (whole-asset, or one slot) and re-binds live animators so a fresh static colour sticks. Always refreshes — a static colour on one slot must punch its hole in a running whole-asset animation.
    internal static void StopAnimation(string? assetId, int slot = -1)
    {
        if (assetId == null) return;
        if (slot >= 0)
        {
            if (PerCosmeticColors.RemoveSlotAnimationNoSave(assetId, slot))
                PerCosmeticColors.SaveSlotAnimations();
        }
        else
        {
            bool changed = PerCosmeticColors.HasAnimation(assetId) || PerCosmeticColors.HasAnySlotAnimation(assetId);
            PerCosmeticColors.RemoveAnimationNoSave(assetId);
            PerCosmeticColors.RemoveSlotAnimationsNoSave(assetId);
            if (changed) { PerCosmeticColors.SaveAnimations(); PerCosmeticColors.SaveSlotAnimations(); }
        }
        foreach (var pc in UnityEngine.Object.FindObjectsOfType<PlayerCosmetics>())
            RefreshLiveAnimators(pc);
    }

    // Re-binds every local/menu animator. Called after a per-slot static colour is painted so a running whole-asset animation re-reads the static slots and leaves the painted slot fixed.
    internal static void RefreshLocal()
    {
        foreach (var pc in UnityEngine.Object.FindObjectsOfType<PlayerCosmetics>())
            RefreshLiveAnimators(pc);
    }

    // Shared core: animated cosmetics get a BridgeColorAnimator bound to current specs; no-longer-animated ones lose theirs. lookup = AnimSet per assetId; callers do the local/remote gating.
    internal static void Apply(PlayerCosmetics pc, Func<string, AnimSet> lookup)
    {
        var visuals = pc?.playerAvatarVisuals;
        if (visuals == null) return;

        var processed = new HashSet<Cosmetic>();
        foreach (var btm in visuals.GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true))
        {
            var cosmetic = btm?.cosmetic;
            var assetId = cosmetic?.cosmeticAsset?.assetId;
            if (cosmetic == null || assetId == null) continue;
            if (!processed.Add(cosmetic)) continue;

            var set = lookup(assetId);
            if (!set.Any) continue;

            var animator = cosmetic.GetComponent<BridgeColorAnimator>()
                           ?? cosmetic.gameObject.AddComponent<BridgeColorAnimator>();
            animator.Init(cosmetic.gameObject, set); // (re)bind — specs or instance may have changed
            if (animator.IsEmpty) animator.Stop(); // animated no live slots
        }

        // Stop stale animators (cosmetic no longer animated, or asset cleared). Stop() clears bindings immediately (Update no-ops this frame); Destroy fires at frame end.
        foreach (var animator in visuals.GetComponentsInChildren<BridgeColorAnimator>(includeInactive: true))
        {
            var assetId = animator.GetComponent<Cosmetic>()?.cosmeticAsset?.assetId;
            if (assetId == null || !lookup(assetId).Any)
                animator.Stop();
        }
    }
}
