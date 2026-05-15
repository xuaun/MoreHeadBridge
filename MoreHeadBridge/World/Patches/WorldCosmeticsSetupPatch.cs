using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// World cosmetics share the Hat CosmeticType. Two problems in SetupCosmeticsLogic:
//   A) vanilla's one-per-Hat hashSet2 causes slot conflicts between hats and worlds
//   B) in preview mode, vanilla overrides the int[] param with cosmeticEquippedPreview,
//      bypassing our prefix filter — both types would race and only the first wins
// Fix: strip world indices from both lists in the prefix, bypass InstantiateCosmetic,
// and spawn world prefabs directly in the postfix.
// Priority.VeryLow runs after REPOLib's modded-index injection.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmeticsLogic")]
internal static class WorldCosmeticsSetupPatch
{
    // Per-PlayerCosmetics list of world cosmetic GameObjects we spawned directly.
    // Keyed by the PlayerCosmetics instance so we clean up correctly per player.
    private static readonly Dictionary<PlayerCosmetics, List<GameObject>> _worldInstances = new();

    private static List<CosmeticAsset>? _pendingWorldAssets;

    // Indices temporarily removed from cosmeticEquippedPreview; restored in postfix.
    private static List<int>? _previewIndicesRemoved;

    [HarmonyPriority(Priority.VeryLow)]
    [HarmonyPrefix]
    private static void Prefix(PlayerCosmetics __instance, ref int[] __0)
    {
        _pendingWorldAssets = null;
        _previewIndicesRemoved = null;
        if (MetaManager.instance == null) return;

        // ── Step 1: filter world from the explicit int[] parameter (__0) ──
        // In preview mode, Step 2 (cosmeticEquippedPreview) is the authority — __0 still
        // reflects the old cosmeticEquipped, so collecting from it would double-spawn.
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
                // Always strip from __0 so vanilla never calls InstantiateCosmetic on it.
                // Only collect for spawning when NOT in preview mode.
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
            _pendingWorldAssets = worldAssets;

        // ── Step 2: filter world from cosmeticEquippedPreview ──
        // Vanilla overrides __0 with cosmeticEquippedPreview during preview mode.
        // Worlds left in that list would hit hashSet2's one-per-Hat limit and block
        // the real hat (or vice versa). Temporarily remove them; restore in postfix.
        if (MetaManager.instance.cosmeticPreviewEnabled)
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
                _pendingWorldAssets ??= [];
                if (!_pendingWorldAssets.Contains(asset))
                    _pendingWorldAssets.Add(asset);
            }

            _previewIndicesRemoved = removed;
        }

        // ── Step 3: always destroy stale world instances ──
        // Safe because Step 2 already stripped worlds from preview before vanilla runs.
        // If no world is pending after this, the ghost is simply gone.
        DestroyTracked(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        // Always restore cosmeticEquippedPreview before spawning.
        RestorePreviewIndices();
        SpawnPendingWorldCosmetics(__instance);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(PlayerCosmetics __instance, Exception? __exception)
    {
        // Safety net: restore preview list and attempt spawn even on exception.
        RestorePreviewIndices();
        if (__exception != null)
            SpawnPendingWorldCosmetics(__instance);
        return __exception;
    }

    private static void RestorePreviewIndices()
    {
        var removed = _previewIndicesRemoved;
        _previewIndicesRemoved = null;
        if (removed == null || MetaManager.instance == null) return;

        var preview = MetaManager.instance.cosmeticEquippedPreview;
        foreach (int idx in removed)
        {
            if (!preview.Contains(idx))
                preview.Add(idx);
        }
    }

    private static void SpawnPendingWorldCosmetics(PlayerCosmetics instance)
    {
        var assets = _pendingWorldAssets;
        _pendingWorldAssets = null;

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

                // Provide cosmeticTypeAsset so EquipAnimation() / CustomTypesLogic()
                // can safely read its fields every frame.
                int typeIdx = (int)asset.type;
                if (MetaManager.instance != null &&
                    typeIdx >= 0 && typeIdx < MetaManager.instance.cosmeticTypeAssets.Count)
                    cosmetic.cosmeticTypeAsset = MetaManager.instance.cosmeticTypeAssets[typeIdx];

                // Start fully equipped: EquipAnimation() early-returns when equipLerp >= 1f.
                cosmetic.equipLerp = 1f;

                // Setup() is skipped for world cosmetics — propagate cosmetic ref manually.
                foreach (var blocked in go.GetComponentsInChildren<CosmeticBlocked>(true))
                    blocked.cosmetic = cosmetic;

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

    private static void DestroyTracked(PlayerCosmetics instance)
    {
        if (!_worldInstances.TryGetValue(instance, out var list)) return;

        var avatar = instance.playerAvatarVisuals;
        foreach (var go in list)
        {
            if (go == null) continue;

            // PartShrinkerRemovePatch fires on Cosmetic.Remove(), which we bypass.
            if (avatar != null)
                PartShrinkerBridge.OnRemove(go, avatar);

            UnityEngine.Object.DestroyImmediate(go);
        }

        list.Clear();
    }
}
