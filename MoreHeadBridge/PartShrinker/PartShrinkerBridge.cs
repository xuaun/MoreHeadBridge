// ============================================================================
// [PartShrinkerBridge] — manually triggers MoreHeadUtilities' part-hiding
// system for bridge cosmetics.
//
// Everything goes through reflection — no compile-time dependency on
// MoreHeadUtilities. If the mod is missing, this is a no-op.
// ============================================================================

using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class PartShrinkerBridge
{
    private static bool _initialized;
    private static bool _available;
    private static Type? _shrinkerType;
    private static Type? _hiddenType;
    private static FieldInfo? _partField;
    private static FieldInfo? _hideChildrenField;
    private static MethodInfo? _addMethod;
    private static MethodInfo? _removeMethod;

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _shrinkerType = AccessTools.TypeByName("MoreHeadUtilities.PartShrinker");
            _hiddenType = AccessTools.TypeByName("MoreHeadUtilities.HiddenParts");
            if (_shrinkerType == null || _hiddenType == null)
            {
                Plugin.Logger.LogDebug("MoreHeadUtilities not loaded — PartShrinker bridge inactive.");
                return;
            }

            _partField = AccessTools.Field(_shrinkerType, "partToHide");
            _hideChildrenField = AccessTools.Field(_shrinkerType, "hideChildren");
            _addMethod = AccessTools.Method(_hiddenType, "AddHiddenPart");
            _removeMethod = AccessTools.Method(_hiddenType, "RemoveHiddenPart");

            _available = _partField != null && _hideChildrenField != null
                       && _addMethod != null && _removeMethod != null;

            if (_available)
                Plugin.Logger.LogInfo("PartShrinker bridge installed.");
            else
                Plugin.Logger.LogWarning("PartShrinker types found but reflection failed — disabled.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"PartShrinker bridge init error: {ex.Message}");
        }
    }

    internal static void OnSpawn(GameObject cosmetic, PlayerAvatarVisuals avatar)
        => Apply(cosmetic, avatar, isAdd: true);

    internal static void OnRemove(GameObject cosmetic, PlayerAvatarVisuals avatar)
        => Apply(cosmetic, avatar, isAdd: false);

    private static void Apply(GameObject cosmetic, PlayerAvatarVisuals avatar, bool isAdd)
    {
        EnsureInit();
        if (!_available || cosmetic == null || avatar == null) return;

        Component[] shrinkers;
        try { shrinkers = cosmetic.GetComponentsInChildren(_shrinkerType!, true); }
        catch { return; }
        if (shrinkers == null || shrinkers.Length == 0) return;

        // Ensure the avatar root has a HiddenParts component to receive the part list.
        var hp = avatar.GetComponent(_hiddenType!);
        if (hp == null)
        {
            try { hp = avatar.gameObject.AddComponent(_hiddenType!); }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"Could not add HiddenParts: {ex.Message}");
                return;
            }
        }

        var method = isAdd ? _addMethod! : _removeMethod!;
        foreach (var shrinker in shrinkers)
        {
            if (shrinker == null) continue;
            try
            {
                object part = _partField!.GetValue(shrinker);
                bool hideChild = (bool)_hideChildrenField!.GetValue(shrinker);
                // HiddenParts.AddHiddenPart(Part, bool, bool update = true) — keep the 3rd arg.
                method.Invoke(hp, new object[] { part, hideChild, true });

                // Disable the PartShrinker MonoBehaviour on Add so its own Update never
                // tries the walk-up (which would silently fail in vanilla hierarchy).
                if (isAdd && shrinker is MonoBehaviour mb) mb.enabled = false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"PartShrinker {(isAdd ? "Add" : "Remove")} failed: {ex.Message}");
            }
        }
    }
}

// Spawn-time trigger: right after the cosmetic is instantiated and Setup'd, fire the hide.
[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static class PartShrinkerBridge_SpawnPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null) return;
        if (!BridgeIds.IsBridgeAsset(_cosmeticAsset)) return;
        if (__instance?.playerAvatarVisuals == null) return;

        PartShrinkerBridge.OnSpawn(__result, __instance.playerAvatarVisuals);
    }
}

// Unequip trigger: just before Cosmetic.Remove destroys the cosmetic, unhide its parts.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Remove))]
internal static class PartShrinkerBridge_RemovePatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        if (__instance == null) return;
        if (!BridgeIds.IsBridgeAsset(__instance.cosmeticAsset)) return;
        if (__instance.playerCosmetics?.playerAvatarVisuals == null) return;

        PartShrinkerBridge.OnRemove(__instance.gameObject, __instance.playerCosmetics.playerAvatarVisuals);
    }
}
