using HarmonyLib;
using System;

namespace MoreHeadBridge;

// Swallows UpdateIcon NREs on bridge cosmetics — a blank icon button is acceptable.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "UpdateIcon")]
internal static class MenuIconNpeGuardPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(MenuElementCosmeticButton __instance, Exception? __exception)
    {
        if (__exception is not NullReferenceException) return __exception;
        if (!BridgeIds.IsBridgeAsset(__instance?.cosmeticAsset)) return __exception;
        return Plugin.ShowBridgeDebugLogs.Value ? __exception : null;
    }
}
