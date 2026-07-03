using HarmonyLib;
using System;

namespace MoreHeadBridge;

// UpdateIcon warns "No IconMaker found" then NPEs (no null-check after the warning). .hhh prefabs have no SemiIconMaker — blank icon button is acceptable; the finalizer suppresses just this crash.
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
