using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

// SetupCosmeticsLogic uses an internal per-type HashSet (hashSet2) that only spawns
// the FIRST cosmetic of each type. When AllowMultipleCosmetics is on, this patch
// collects extras (2nd, 3rd… of the same type) and spawns them via InstantiateCosmetic
// in the postfix, tracking them in a per-instance list for cleanup on the next call.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmeticsLogic")]
internal static class MultiEquipSetupPatch
{
    private static readonly Dictionary<PlayerCosmetics, List<GameObject>> _extraInstances = new();

    private sealed class PatchState
    {
        public List<CosmeticAsset>? PendingExtras;
    }

    private static MethodInfo? _instantiateCosmetic;
    private static MethodInfo? _playerMaterialSetup;
    private static MethodInfo? _setupColorsLogic;

    // Fields used by SkipEquipAnimation — cached once, used on every fresh extra spawn.
    private static FieldInfo? _iconCreationAvatarField;
    private static FieldInfo? _equipLerpField;
    private static FieldInfo? _meshParentsScaleField;
    private static FieldInfo? _colorsEquippedField;

    private static MethodInfo? GetInstantiateMethod() =>
        _instantiateCosmetic ??= AccessTools.Method(typeof(PlayerCosmetics), "InstantiateCosmetic");
    private static MethodInfo? GetPlayerMaterialSetup() =>
        _playerMaterialSetup ??= AccessTools.Method(typeof(PlayerCosmetics), "PlayerMaterialSetup");
    private static MethodInfo? GetSetupColorsLogic() =>
        _setupColorsLogic ??= AccessTools.Method(typeof(PlayerCosmetics), "SetupColorsLogic");

    [HarmonyPriority(50)]
    [HarmonyPrefix]
    private static void Prefix(PlayerCosmetics __instance, ref int[] __0, ref PatchState __state)
    {
        if (!Plugin.AllowMultipleCosmetics.Value)
        {
            DestroyAllTracked(__instance);
            return;
        }

        if (MetaManager.instance == null) return;

        __state = new PatchState();

        // Only use the local player's preview list when this IS the local menu avatar.
        bool usePreview = MetaManager.instance.cosmeticPreviewEnabled
            && __instance.playerAvatarVisuals?.isMenuAvatar == true;
        IEnumerable<int> source = usePreview
            ? (IEnumerable<int>)MetaManager.instance.cosmeticEquippedPreview
            : __0;

        // Build the desired extras set (2nd, 3rd… of the same type).
        // HashSet deduplicates automatically, avoiding an O(n) Contains inside the loop.
        var seen = new HashSet<SemiFunc.CosmeticType>();
        var desiredSet = new HashSet<CosmeticAsset>();

        foreach (int idx in source)
        {
            if (idx < 0 || idx >= MetaManager.instance.cosmeticAssets.Count) continue;
            var asset = MetaManager.instance.cosmeticAssets[idx];
            if (asset == null) continue;
            if (HhhCosmeticLoader.IsWorldAsset(asset)) continue; // handled by WorldCosmeticsSetupPatch
            if (!MultiEquipTypes.All.Contains(asset.type)) continue;

            if (!seen.Add(asset.type))
                desiredSet.Add(asset);
        }

        // Remove extras that are no longer needed; KEEP the ones that still are.
        // Keeping them avoids destroying+recreating, which would replay the equip animation.
        TrimTracked(__instance, desiredSet);

        // Only spawn extras that aren't already tracked (i.e. genuinely new).
        __state.PendingExtras = desiredSet.Count > 0 ? new List<CosmeticAsset>(desiredSet) : null;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, PatchState __state)
    {
        if (__state != null) SpawnExtras(__instance, __state);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(PlayerCosmetics __instance, Exception? __exception, PatchState __state)
    {
        if (__exception != null && __state != null)
            SpawnExtras(__instance, __state);
        return __exception;
    }

    private static void SpawnExtras(PlayerCosmetics instance, PatchState state)
    {
        var extras = state.PendingExtras;
        state.PendingExtras = null;

        if (extras == null || extras.Count == 0 || instance == null) return;

        var method = GetInstantiateMethod();
        if (method == null) return;

        if (!_extraInstances.TryGetValue(instance, out var list))
        {
            list = [];
            _extraInstances[instance] = list;
        }

        // Collect already-tracked assets so we don't re-spawn them.
        var alreadyTracked = new HashSet<CosmeticAsset>();
        foreach (var go in list)
        {
            if (go == null) continue;
            var c = go.GetComponent<Cosmetic>();
            if (c?.cosmeticAsset != null)
                alreadyTracked.Add(c.cosmeticAsset);
        }

        bool isPreview    = MetaManager.instance?.cosmeticPreviewEnabled == true;
        bool isIconAvatar = instance.playerAvatarVisuals?.playerAvatarMenu?.iconMakerAvatar == true;
        bool shouldSkipAnim = isPreview || isIconAvatar;

        var newlySpawned = new List<GameObject>();
        foreach (var asset in extras)
        {
            if (alreadyTracked.Contains(asset)) continue; // already alive — skip

            try
            {
                var go = (GameObject?)method.Invoke(instance, new object[] { asset });
                if (go != null)
                {
                    list.Add(go);
                    newlySpawned.Add(go);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"MultiEquipSetupPatch: failed to spawn extra '{asset?.assetId}': {ex.Message}");
            }
        }

        if (newlySpawned.Count == 0) return;

        try
        {
            GetPlayerMaterialSetup()?.Invoke(instance, null);

            _colorsEquippedField ??= AccessTools.Field(typeof(PlayerCosmetics), "colorsEquipped");
            if (_colorsEquippedField?.GetValue(instance) is int[] colors)
                // We pass only the colours array. SetupColorsLogic may have an optional
                // _forced parameter — omitting it here relies on vanilla's default (false),
                // meaning a normal incremental colour reapply rather than a full rebuild.
                GetSetupColorsLogic()?.Invoke(instance, new object[] { colors });
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"MultiEquipSetupPatch: color reapply failed: {ex.Message}");
        }

        if (shouldSkipAnim)
        {
            foreach (var go in newlySpawned)
                SkipEquipAnimation(go);
        }

        // Local player  → ApplyLocalVisibilityBody() sets "PlayerVisualsLocal"
        //                 on every Renderer under animBotRoot, hiding them from
        //                 the first-person camera.
        // Remote player → set "PlayerVisuals" on only the new GOs' PlayerMaterial
        //                 renderers (mirrors the else-branch in SetupCosmeticsLogic).
        var visuals = instance.playerAvatarVisuals;
        if (visuals != null && !visuals.isMenuAvatar)
        {
            if (visuals.playerAvatar?.isLocal == true)
            {
                visuals.ApplyLocalVisibilityBody();
            }
            else
            {
                int remoteLayer = LayerMask.NameToLayer("PlayerVisuals");
                foreach (var go in newlySpawned)
                {
                    foreach (var pm in go.GetComponentsInChildren<PlayerMaterial>(includeInactive: true))
                    {
                        if (pm != null) pm.gameObject.layer = remoteLayer;
                    }
                }
            }
        }
    }

    private static void SkipEquipAnimation(GameObject go)
    {
        try
        {
            var cosmetic = go.GetComponent<Cosmetic>();
            if (cosmetic == null) return;

            _iconCreationAvatarField ??= AccessTools.Field(typeof(Cosmetic), "iconCreationAvatar");
            _iconCreationAvatarField?.SetValue(cosmetic, true);

            _equipLerpField ??= AccessTools.Field(typeof(Cosmetic), "equipLerp");
            _equipLerpField?.SetValue(cosmetic, 1f);

            _meshParentsScaleField ??= AccessTools.Field(typeof(Cosmetic), "meshParentsScale");
            if (_meshParentsScaleField?.GetValue(cosmetic) is List<Vector3> scales)
            {
                var parents = cosmetic.meshParents;
                for (int i = 0; i < parents.Count && i < scales.Count; i++)
                {
                    if (parents[i] != null)
                        parents[i].localScale = scales[i];
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"MultiEquipSetupPatch: SkipEquipAnimation failed: {ex.Message}");
        }
    }

    private static void TrimTracked(PlayerCosmetics instance, HashSet<CosmeticAsset> desired)
    {
        if (!_extraInstances.TryGetValue(instance, out var list)) return;

        var avatar = instance.playerAvatarVisuals;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i];
            if (go == null) { list.RemoveAt(i); continue; }

            var c = go.GetComponent<Cosmetic>();
            if (c?.cosmeticAsset != null && desired.Contains(c.cosmeticAsset))
                continue; // Still needed — leave it alive.

            if (avatar != null)
                PartShrinkerBridge.OnRemove(go, avatar);
            UnityEngine.Object.Destroy(go);
            list.RemoveAt(i);
        }
    }

    // Full cleanup — used when the feature is disabled or the player disconnects.
    private static void DestroyAllTracked(PlayerCosmetics instance)
    {
        if (!_extraInstances.TryGetValue(instance, out var list)) return;

        var avatar = instance.playerAvatarVisuals;
        foreach (var go in list)
        {
            if (go == null) continue;
            if (avatar != null)
                PartShrinkerBridge.OnRemove(go, avatar);
            UnityEngine.Object.Destroy(go);
        }
        list.Clear();
    }
}
