using HarmonyLib;
using System;

namespace MoreHeadBridge;

// MenuElementCosmeticButton.UpdateIcon warns "No IconMaker found" then NPEs because
// the game code doesn't null-check after the warning. .hhh prefabs have no SemiIconMaker
// (they were never built for the vanilla UI), so the icon button stays blank — that's
// acceptable. The finalizer suppresses the crash without hiding other bugs.
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
