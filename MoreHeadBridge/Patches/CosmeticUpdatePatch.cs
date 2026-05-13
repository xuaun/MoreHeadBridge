using HarmonyLib;
using System;

namespace MoreHeadBridge;

// Cosmetic.CustomTypesLogic() iterates over cosmeticAsset.customTypeList /
// cosmeticTypeAsset.customTypeList / playerCosmetics — any of which can be null
// for bridge cosmetics that don't have the full vanilla setup. It is called from
// both Cosmetic.Start() AND Cosmetic.Update() — patching it directly catches both
// paths with a single finalizer. CustomTypesLogic only fires optional condition
// sets (e.g. CosmeticPlayerCrown), so swallowing the NPE has no visual effect.
[HarmonyPatch(typeof(Cosmetic), "CustomTypesLogic")]
internal static class CosmeticUpdatePatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Cosmetic __instance, Exception? __exception)
    {
        if (__exception is not NullReferenceException) return __exception;
        if (!BridgeIds.IsBridgeAsset(__instance?.cosmeticAsset)) return __exception;
        return Plugin.ShowBridgeDebugLogs.Value ? __exception : null;
    }
}
