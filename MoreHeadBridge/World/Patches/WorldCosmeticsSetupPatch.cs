using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Worlds share Hat. Two SetupCosmeticsLogic problems: (A) the one-per-Hat hashSet2 makes hats and worlds fight for the slot; (B) preview mode overrides the int[] with cosmeticEquippedPreview, bypassing our prefix filter.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmeticsLogic")]
internal static class WorldCosmeticsSetupPatch
{
    // World cosmetic GameObjects we spawned, keyed by PlayerCosmetics for per-player cleanup.
    private static readonly Dictionary<PlayerCosmetics, List<GameObject>> _worldInstances = new();

    private sealed class PatchState
    {
        public List<CosmeticAsset>? PendingWorldAssets;
        public List<int>? PreviewIndicesRemoved;
    }

    [HarmonyPriority(Priority.VeryLow)]
    [HarmonyPrefix]
    private static bool Prefix(PlayerCosmetics __instance, ref int[] _cosmeticEquipped, bool _forced, ref PatchState __state)
    {
        __state = new PatchState();
        PruneDestroyedInstances();
        // Capture the full equipped list before filtering worlds out, so each client can dress its Mini-Semibot from the engine-synced outfit (incl. REMOTE players).
        MiniSemibotOutfitCache.RecordCosmetics(__instance, _cosmeticEquipped);
        if (MetaManager.instance == null) return true;

        // Track equip transitions so a genuine un-equip re-rolls the RandomPreset outfit (menu↔game re-spawns keep the same one).
        MiniSemibotSpawner.UpdateEquipState();

        if (WorldCosmeticsClearButtonPatch.IsUnequipAllRunning)
            return false;

        // Expression / icon-maker avatars never show world cosmetics (tiny preview frames). Worlds are still filtered out of the equip list to avoid hat-slot conflicts.
        if (IsNonSpawnMenuAvatar(__instance))
        {
            // A REMOTE mini reaches here too (MiniSemibotTag ⇒ non-spawn): its world membership is the OWNER's broadcast Type, NOT the global WorldAssetIds (that set carries the local VIEWER's World overrides, which would wrongly drop the owner's normal hat). Local minis/previews ARE the local player, so the global set is correct for them.
            int nonSpawnMiniActor = MiniSemibotSpawner.RemoteMiniActorOf(__instance);
            bool nonSpawnRemoteMini = nonSpawnMiniActor > 0;

            var nonSpawnFiltered = new List<int>(_cosmeticEquipped.Length);
            foreach (int idx in _cosmeticEquipped)
            {
                if (idx >= 0 && idx < MetaManager.instance.cosmeticAssets.Count)
                {
                    var a = MetaManager.instance.cosmeticAssets[idx];
                    if (a == null) { nonSpawnFiltered.Add(idx); continue; }
                    if (MoreHeadCosmeticMountPatch.IsWorldFor(a, nonSpawnRemoteMini ? nonSpawnMiniActor : 0)) continue;
                }
                nonSpawnFiltered.Add(idx);
            }
            _cosmeticEquipped = [.. nonSpawnFiltered];
            DestroyAllTracked(__instance);
            return true;
        }

        bool isPreviewMode = MetaManager.instance.cosmeticPreviewEnabled;
        // Expression preview avatar (here only via the Mini-Semibot opt-in): spawn ONLY the mini — regular world decorations make no sense in that tiny portrait frame.
        bool miniOnly = IsExpressionAvatar(__instance);
        // Remote avatars: world membership comes from the wearer's broadcast Type (or pre-override default) — WorldAssetIds may carry the VIEWER's own World override.
        bool isRemote = AvatarIdentity.TryGetRemoteActor(__instance, out int remoteActor);
        List<CosmeticAsset>? worldAssets = null;
        var filtered = new List<int>(_cosmeticEquipped.Length);

        foreach (int idx in _cosmeticEquipped)
        {
            if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count)
            {
                filtered.Add(idx);
                continue;
            }

            var asset = MetaManager.instance.cosmeticAssets[idx];
            if (asset == null) { filtered.Add(idx); continue; }

            if (MoreHeadCosmeticMountPatch.IsWorldFor(asset, isRemote ? remoteActor : 0))
            {
                if (!isPreviewMode && (!miniOnly || asset.assetId == MiniSemibotCosmetic.AssetId))
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

        _cosmeticEquipped = [.. filtered];
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

                // Expression preview avatar: only the Mini-Semibot may (re)spawn.
                if (miniOnly && asset.assetId != MiniSemibotCosmetic.AssetId) continue;

                // Ensure this world asset is (re)spawned in postfix.
                __state.PendingWorldAssets ??= [];
                if (!__state.PendingWorldAssets.Contains(asset))
                    __state.PendingWorldAssets.Add(asset);
            }

            __state.PreviewIndicesRemoved = removed;
        }

        // _forced=true: destroy all world instances so they fully re-spawn — Cosmetic.Setup() fires again and WorldEquipAnimationScalePatch applies the pending equip-anim mode from OverridePreviewContext.
        if (_forced)
            DestroyAllTracked(__instance);
        else
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

        // Owner-authoritative base-mesh visibility for meshSwitch types: vanilla Setup hid them by SHARED asset.type, leaking the viewer's mesh-type override onto others (and missing the owner's). Guarded so a failure can't cascade into the vanilla pipeline.
        try { MoreHeadCosmeticMountPatch.ReconcileMeshSwitchBaseMeshes(__instance); }
        catch (Exception ex) { BceConsole.LogWarning($"Mesh-switch reconcile failed: {ex.Message}"); }
        // Re-dress an existing Mini-Semibot to match the wearer's outfit. Guarded so a failure can't cascade into the vanilla pipeline.
        try { MiniSemibotSpawner.RefreshOutfit(__instance); }
        catch (Exception ex) { BceConsole.LogWarning($"Mini-Semibot RefreshOutfit failed: {ex.Message}"); }
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
        var avatarTransform = instance.playerAvatarVisuals.transform;

        if (!_worldInstances.TryGetValue(instance, out var list))
        {
            list = [];
            _worldInstances[instance] = list;
        }

        foreach (var asset in assets)
        {
            // Synthetic "Mini-Semibot": spawn a dressed mini player avatar instead of a static prefab. Guarded so a spawn failure can't abort the rest of the world-cosmetic pipeline.
            if (asset != null && asset.assetId == MiniSemibotCosmetic.AssetId)
            {
                try
                {
                    var mini = MiniSemibotSpawner.Spawn(instance, asset);
                    if (mini != null) list.Add(mini);
                }
                catch (Exception ex)
                {
                    BceConsole.LogWarning($"Mini-Semibot spawn failed: {ex.Message}");
                }
                continue;
            }

            if (asset == null) continue;
            var prefab = asset.prefab?.Prefab;
            if (prefab == null) continue;

            try
            {
                var go = UnityEngine.Object.Instantiate(prefab);

                // Populate the Cosmetic component so bridge patches and Cosmetic.Update() sub-methods (EquipAnimation, CustomTypesLogic) don't NPE on null fields.
                var cosmetic = go.GetComponent<Cosmetic>() ?? go.AddComponent<Cosmetic>();
                cosmetic.cosmeticAsset = asset;
                cosmetic.type = asset.type;
                cosmetic.rarity = asset.rarity;
                cosmetic.playerCosmetics = instance;
                cosmetic.cosmeticParent = null; // worlds float freely — no body-part parent

                // Provide cosmeticTypeAsset so EquipAnimation() / CustomTypesLogic() can safely read its fields every frame.
                int typeIdx = (int)asset.type;
                if (MetaManager.instance != null &&
                    typeIdx >= 0 && typeIdx < MetaManager.instance.cosmeticTypeAssets.Count)
                    cosmetic.cosmeticTypeAsset = MetaManager.instance.cosmeticTypeAssets[typeIdx];

                cosmetic.Setup();

                MoreHeadCosmeticMountPatch.MountWorldCosmetic(
                    go, avatarTransform, prefab, asset.assetId);

                // Apply physics/animation fixes to the live instance (worlds bypass InstantiateCosmetic, so MoreHeadCosmeticMountPatch's postfix won't fire). Remote wearers: broadcast data, never the viewer's local overrides.
                bool isRemote = AvatarIdentity.TryGetRemoteActor(instance, out int actor);
                BridgeSyncPayload? remoteData = null;
                if (isRemote)
                    CustomizerSync.TryGetRemote(actor, asset.assetId, out remoteData);
                CosmeticPrefabFixer.FixInstance(go, asset.assetId,
                    isRemote ? remoteData?.FixAnimation : null, isRemote);

                // Inject BridgeTintMaterial: worlds spawn after PlayerMaterialSetup(), but BTMs don't use playerMaterials — colours arrive via GetComponentsInChildren on the next SetupColorsLogic.
                bool? tintableOverride = isRemote
                    ? remoteData?.Tintable ?? CustomizerStore.GetRemoteFallbackTintable(asset)
                    : (bool?)null;
                if (tintableOverride != false)
                    BridgeTintHelper.InjectBridgeTintMaterials(go, asset, tintableOverride);

                // PartShrinkerSpawnPatch fires on InstantiateCosmetic, which worlds bypass.
                if (instance.playerAvatarVisuals != null)
                    PartShrinkerBridge.OnSpawn(go, instance.playerAvatarVisuals);

                // Apply offsets, custom-types and sway here: worlds bypass InstantiateCosmetic, so MoreHeadCosmeticMountPatch.Postfix never fires for them.
                ApplyOverridesToWorldCosmetic(go, asset, instance, isRemote, remoteData);

                list.Add(go);
            }
            catch (Exception ex)
            {
                BceConsole.LogWarning($"WorldCosmeticsSetupPatch: failed to spawn '{asset?.assetId}': {ex.Message}");
            }
        }
    }

    private static void ApplyOverridesToWorldCosmetic(GameObject go, CosmeticAsset asset, PlayerCosmetics instance,
                                                      bool isRemote, BridgeSyncPayload? remoteData)
    {
        List<CosmeticOffsetEntry>? offsets;
        List<CosmeticCustomCondition.Type>? customTypes;

        bool isPreview = OverridePreviewContext.IsActiveFor(instance, asset.assetId);
        if (isPreview)
        {
            var previewData = OverridePreviewContext.Data;
            if (previewData != null)
            {
                offsets = previewData.Offsets;
                customTypes = previewData.CustomTypes;
            }
            else
            {
                CustomizerStore.TryGet(asset.assetId, out var saved);
                offsets = saved?.Offsets;
                customTypes = saved?.CustomTypes;
            }
        }
        else if (isRemote)
        {
            // No broadcast payload = no override — never the viewer's local store.
            offsets = remoteData?.Offsets;
            customTypes = remoteData?.CustomTypes;
        }
        else
        {
            CustomizerStore.TryGet(asset.assetId, out var localData);
            offsets = localData?.Offsets;
            customTypes = localData?.CustomTypes;
        }

        MoreHeadCosmeticMountPatch.InjectOffsetConditions(go, asset, instance, offsets, customTypes);

        // Sway for world cosmetics (skip if vanilla springs already present). IsSwayEnabled resolves remote wearers (incl. minis) to their broadcast data.
        if (CosmeticSwayHelper.IsSwayEnabled(instance, asset.assetId)
            && go.GetComponentsInChildren<CosmeticSprings>(true).Length == 0)
        {
            var cosmetic = go.GetComponent<Cosmetic>();
            if (cosmetic != null)
            {
                var spring = go.AddComponent<BridgeSwaySpring>();
                spring.Init(cosmetic, CosmeticSwayHelper.GetIntensityFactor(instance, asset.assetId));
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

    // Destroys ALL tracked world instances for a PlayerCosmetics (used when _forced=true so each fully re-spawns and Cosmetic.Setup fires).
    private static void DestroyAllTracked(PlayerCosmetics instance)
    {
        if (!_worldInstances.TryGetValue(instance, out var list)) return;
        var avatar = instance.playerAvatarVisuals;
        foreach (var go in list)
        {
            if (go == null) continue;
            if (avatar != null) PartShrinkerBridge.OnRemove(go, avatar);
            UnityEngine.Object.Destroy(go);
        }
        list.Clear();
    }

    // Removes entries whose PlayerCosmetics key was destroyed (player left). PlayerCosmetics is a UnityEngine.Object, so destroyed instances compare equal to null.
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

    // True for menu avatars that must never show worlds: expression/icon-maker previews AND transient per-preset icon avatars (those run SetupCosmeticsLogic with the preset's full list — without the guard each preset icon spawns a full Mini-Semibot/world, flooding the scene and crashing on save). Resolve PlayerAvatarMenu via back-reference OR parent lookup (preset-icon clones leave visuals.playerAvatarMenu unwired); NOT PlayerAvatarMenu.instance, which the clones overwrite then die, leaving it null.
    private static bool IsNonSpawnMenuAvatar(PlayerCosmetics instance)
    {
        // The mini is ITSELF a cloned menu avatar with its own PlayerCosmetics — dressing it re-enters this prefix, and in PREVIEW mode the global cosmeticEquippedPreview still contains Mini-Semibot while hovered. Without this guard: mini spawns mini spawns mini → stack overflow → native crash, no log.
        if (instance.GetComponentInParent<MiniSemibotTag>() != null) return true;

        var visuals = instance.playerAvatarVisuals;
        if (visuals == null || !visuals.isMenuAvatar) return false;   // real in-game avatar → allowed

        var menu = visuals.playerAvatarMenu != null
            ? visuals.playerAvatarMenu
            : visuals.GetComponentInParent<PlayerAvatarMenu>();
        if (menu == null) return false;

        // The expression-preview avatar is normally excluded, but the Mini-Semibot popup opt-in lets ONLY the mini through — the world-collection loops still skip every other world asset for it. Icon-maker previews stay excluded.
        if (menu.expressionAvatar && MiniSemibotVisualPrefs.ShowInExpressionPreview)
            return false;

        return menu.expressionAvatar || menu.iconMakerAvatar;
    }

    // True for the in-game facial-expression preview avatar (5-9 keys HUD).
    private static bool IsExpressionAvatar(PlayerCosmetics instance)
    {
        var visuals = instance.playerAvatarVisuals;
        if (visuals == null || !visuals.isMenuAvatar) return false;
        var menu = visuals.playerAvatarMenu != null
            ? visuals.playerAvatarMenu
            : visuals.GetComponentInParent<PlayerAvatarMenu>();
        return menu != null && menu.expressionAvatar;
    }

    // Destroys world GameObjects no longer in the pending list, and removes kept assets from pending so SpawnPendingWorldCosmetics doesn't re-create them.
    private static void SelectiveDestroyTracked(PlayerCosmetics instance, List<CosmeticAsset>? pending)
    {
        if (!_worldInstances.TryGetValue(instance, out var list)) return;

        var pendingSet = pending != null ? new HashSet<CosmeticAsset>(pending) : null;

        var avatar = instance.playerAvatarVisuals;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i];
            if (go == null) { list.RemoveAt(i); continue; }

            var cosmetic = go.GetComponent<Cosmetic>();
            // The Mini-Semibot avatar root has no Cosmetic component — fall back to its marker tag.
            var asset = cosmetic?.cosmeticAsset ?? go.GetComponent<MiniSemibotTag>()?.Asset;
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
