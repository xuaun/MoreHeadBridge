using HarmonyLib;
using System;
using System.Collections.Generic;

namespace MoreHeadBridge;

// CustomTypesLogic() iterates several lists that can be null for bridge cosmetics; patching it directly covers both Start() and Update() callers. It only fires optional condition sets, so swallowing the NRE has no visual effect.
//
// Performance: once an asset is known to throw NRE, a Prefix skips the call
// entirely (no exception allocation overhead on subsequent Update frames).
[HarmonyPatch(typeof(Cosmetic), "CustomTypesLogic")]
internal static class CustomTypesLogicNpeGuardPatch
{
    // Assets whose CustomTypesLogic always throws NRE — skip them entirely.
    private static readonly HashSet<string> _skipAssets = new();

    [HarmonyPrefix]
    private static bool Prefix(Cosmetic __instance)
    {
        // Broadcaster took over this instance's announce → skip the native one.
        if (__instance != null
            && __instance.TryGetComponent<BridgeCustomTypesBroadcaster>(out var b) && b.SuppressNative)
            return false;

        var id = __instance?.cosmeticAsset?.assetId;
        if (id == null) return true;
        if (!_skipAssets.Contains(id)) return true;

        // Previously marked always-throwing — if the user later configured Condition Trigger Settings (customTypeList now non-empty), remove from the skip list so the types are announced again.
#pragma warning disable CS8602
        int customTypeCount = __instance.cosmeticAsset?.customTypeList?.Count ?? 0;
#pragma warning restore CS8602
        if (customTypeCount > 0)
        {
            _skipAssets.Remove(id);
            return true;
        }
        return false;
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Cosmetic __instance, Exception? __exception)
    {
        if (__exception is not NullReferenceException) return __exception;

        // Register the asset so the Prefix skips it next frames — avoids per-frame exception allocation for cosmetics that consistently fail (e.g. during level transitions).
        if (__instance?.cosmeticAsset?.assetId is string id)
            _skipAssets.Add(id);

        // Debug mode shows the error for bridge cosmetics; everything else is suppressed (no visual effect).
        bool isDebugBridge = Plugin.ShowBridgeDebugLogs.Value
                             && BridgeIds.IsBridgeAsset(__instance?.cosmeticAsset);
        return isDebugBridge ? __exception : null;
    }
}

// EquipAnimation() reads AssetManager.instance every frame while equipLerp < 1; during level transitions instance is null → a burst of NREs. It only drives the pop-in scale, so swallowing means the cosmetic just appears at full scale.
[HarmonyPatch(typeof(Cosmetic), "EquipAnimation")]
internal static class EquipAnimationNpeGuardPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception is NullReferenceException) return null;
        return __exception;
    }
}
