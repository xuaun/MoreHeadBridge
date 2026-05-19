using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// World cosmetics share the Hat CosmeticType. Two problems in SetupCosmeticsLogic:
//   A) vanilla's one-per-Hat hashSet2 causes slot conflicts between hats and worlds
//   B) in preview mode, vanilla overrides the int[] param with cosmeticEquippedPreview,
//      bypassing our prefix filter — both types would race and only the first wins
[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmeticsLogic")]
internal static class WorldCosmeticsSetupPatch
{
    // Per-PlayerCosmetics list of world cosmetic GameObjects we spawned directly.
    // Keyed by the PlayerCosmetics instance so we clean up correctly per player.
    private static readonly Dictionary<PlayerCosmetics, List<GameObject>> _worldInstances = new();

    private sealed class PatchState
    {
        public List<CosmeticAsset>? PendingWorldAssets;
        public List<int>? PreviewIndicesRemoved;
    }

    [HarmonyPriority(Priority.VeryLow)]
    [HarmonyPrefix]
    private static bool Prefix(PlayerCosmetics __instance, ref int[] __0, ref PatchState __state)
    {
        __state = new PatchState();
        PruneDestroyedInstances();
        if (MetaManager.instance == null) return true;

        if (WorldCosmeticsClearButtonPatch.IsUnequipAllRunning)
            return false;

        bool isPreviewMode = MetaManager.instance.cosmeticPreviewEnabled;
        List<CosmeticAsset>? worldAssets = null;
        var filtered = new List<int>(__0.Length);

        foreach (int idx in __0)
        {
            if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count)
            {
                filtered.Add(idx);
                continue;
            }

            var asset = MetaManager.instance.cosmeticAssets[idx];
            if (HhhCosmeticLoader.IsWorldAsset(asset))
            {
                if (!isPreviewMode)
                {
                    worldAssets ??= [];
                    worldAssets.Add(asset);
                }
            }
            else
            {
                filtered.Add(idx);
            }
        }

        __0 = [.. filtered];
        if (worldAssets != null)
            __state.PendingWorldAssets = worldAssets;

        if (isPreviewMode)
        {
            var preview = MetaManager.instance.cosmeticEquippedPreview;
            List<int>? removed = null;

            for (int i = preview.Count - 1; i >= 0; i--)
            {
                int idx = preview[i];
                if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;

                var asset = MetaManager.instance.cosmeticAssets[idx];
                if (!HhhCosmeticLoader.IsWorldAsset(asset)) continue;

                removed ??= [];
                removed.Add(idx);
                preview.RemoveAt(i);

                // Ensure this world asset is (re)spawned in postfix.
                __state.PendingWorldAssets ??= [];
                if (!__state.PendingWorldAssets.Contains(asset))
                    __state.PendingWorldAssets.Add(asset);
            }

            __state.PreviewIndicesRemoved = removed;
        }

        SelectiveDestroyTracked(__instance, __state.PendingWorldAssets);
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, PatchState __state)
    {
        if (__state == null) return;
        // Always restore cosmeticEquippedPreview before spawning.
        RestorePreviewIndices(__state);
        SpawnPendingWorldCosmetics(__instance, __state);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(PlayerCosmetics __instance, Exception? __exception, PatchState __state)
    {
        if (__state == null) return __exception;
        // Safety net: restore preview list and attempt spawn even on exception.
        RestorePreviewIndices(__state);
        if (__exception != null)
            SpawnPendingWorldCosmetics(__instance, __state);
        return __exception;
    }

    private static void RestorePreviewIndices(PatchState state)
    {
        var removed = state.PreviewIndicesRemoved;
        state.PreviewIndicesRemoved = null;
        if (removed == null || MetaManager.instance == null) return;

        var preview = MetaManager.instance.cosmeticEquippedPreview;
        foreach (int idx in removed)
        {
            if (!preview.Contains(idx))
                preview.Add(idx);
        }
    }

    private static void SpawnPendingWorldCosmetics(PlayerCosmetics instance, PatchState state)
    {
        var assets = state.PendingWorldAssets;
        state.PendingWorldAssets = null;

        if (assets == null || assets.Count == 0 || instance == null) return;
        if (instance.playerAvatarVisuals == null) return;

        var worldNode = MoreHeadCosmeticMountPatch.EnsureWorldFollower(
            instance.playerAvatarVisuals.transform);
        if (worldNode == null) return;

        if (!_worldInstances.TryGetValue(instance, out var list))
        {
            list = [];
            _worldInstances[instance] = list;
        }

        foreach (var asset in assets)
        {
            var prefab = asset.prefab?.Prefab;
            if (prefab == null) continue;

            try
            {
                var go = UnityEngine.Object.Instantiate(prefab);

                // Populate the Cosmetic component so bridge patches and Cosmetic.Update()
                // sub-methods (EquipAnimation, CustomTypesLogic) don't NPE on null fields.
                var cosmetic = go.GetComponent<Cosmetic>() ?? go.AddComponent<Cosmetic>();
                cosmetic.cosmeticAsset = asset;
                cosmetic.type = asset.type;
                cosmetic.rarity = asset.rarity;
                cosmetic.playerCosmetics = instance;
                cosmetic.cosmeticParent  = null; // worlds float freely — no body-part parent

                // Provide cosmeticTypeAsset so EquipAnimation() / CustomTypesLogic()
                // can safely read its fields every frame.
                int typeIdx = (int)asset.type;
                if (MetaManager.instance != null &&
                    typeIdx >= 0 && typeIdx < MetaManager.instance.cosmeticTypeAssets.Count)
                    cosmetic.cosmeticTypeAsset = MetaManager.instance.cosmeticTypeAssets[typeIdx];

                cosmetic.Setup();

                MoreHeadCosmeticMountPatch.Mount(go, worldNode, prefab);

                // PartShrinkerSpawnPatch fires on InstantiateCosmetic, which worlds bypass.
                if (instance.playerAvatarVisuals != null)
                    PartShrinkerBridge.OnSpawn(go, instance.playerAvatarVisuals);

                list.Add(go);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"WorldCosmeticsSetupPatch: failed to spawn '{asset?.assetId}': {ex.Message}");
            }
        }
    }

    internal static void SetAllWorldInstancesActive(bool active)
    {
        foreach (var list in _worldInstances.Values)
            foreach (var go in list)
                if (go != null) go.SetActive(active);
    }

    internal static void SetWorldAssetActive(CosmeticAsset asset, bool active)
    {
        foreach (var list in _worldInstances.Values)
            foreach (var go in list)
            {
                if (go == null) continue;
                var cosmetic = go.GetComponent<Cosmetic>();
                if (cosmetic != null && cosmetic.cosmeticAsset == asset)
                    go.SetActive(active);
            }
    }

    // Removes dictionary entries whose PlayerCosmetics key has been destroyed (player left).
    // PlayerCosmetics is a UnityEngine.Object, so destroyed instances compare equal to null.
    private static void PruneDestroyedInstances()
    {
        List<PlayerCosmetics>? stale = null;
        foreach (var key in _worldInstances.Keys)
        {
            if (key == null)
            {
                stale ??= [];
                stale.Add(key!); // intentionally collecting destroyed (null-equal) keys for removal
            }
        }
        if (stale != null)
            foreach (var key in stale)
                _worldInstances.Remove(key);
    }

    // Destroys world GameObjects that are no longer in the pending list and removes kept
    // assets from pending so SpawnPendingWorldCosmetics doesn't re-create them.
    private static void SelectiveDestroyTracked(PlayerCosmetics instance, List<CosmeticAsset>? pending)
    {
        if (!_worldInstances.TryGetValue(instance, out var list)) return;

        // HashSet for O(1) Contains/Remove instead of O(n) per iteration.
        var pendingSet = pending != null ? new HashSet<CosmeticAsset>(pending) : null;

        var avatar = instance.playerAvatarVisuals;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i];
            if (go == null) { list.RemoveAt(i); continue; }

            var cosmetic    = go.GetComponent<Cosmetic>();
            var asset       = cosmetic?.cosmeticAsset;
            // pendingSet.Remove returns true if present — serves as Contains + Remove in one step.
            bool stillNeeded = asset != null && (pendingSet?.Remove(asset) ?? false);

            if (stillNeeded)
            {
                // Keep the GO alive; remove from the original list so Spawn skips it.
                pending!.Remove(asset!);
            }
            else
            {
                if (avatar != null) PartShrinkerBridge.OnRemove(go, avatar);
                UnityEngine.Object.Destroy(go);
                list.RemoveAt(i);
            }
        }
    }
}
