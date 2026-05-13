// MoreHeadUtilities.PartShrinker.OnDisable() throws NullReferenceException whenever a
// cosmetic with that component is destroyed without prior setup (which is exactly what
// the IconRenderer does for its temp instances, but it also happens organically in
// the vanilla cosmetics flow). We can't fix the mod itself; we can suppress the spam
// in our process by attaching a Harmony finalizer at runtime if the type is present.
//
// Lives in its own file so it's easy to remove together with [BridgeIconRenderer] or
// keep around independently — it doesn't depend on the icon renderer.

using HarmonyLib;
using System;
using System.Reflection;

namespace MoreHeadBridge;

internal static class PartShrinkerSuppressor
{
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var t = AccessTools.TypeByName("MoreHeadUtilities.PartShrinker");
            if (t == null)
            {
                Plugin.Logger.LogDebug("MoreHeadUtilities not loaded — PartShrinker suppressor skipped.");
                return;
            }

            var target = AccessTools.Method(t, "OnDisable");
            if (target == null) return;

            var finalizer = typeof(PartShrinkerSuppressor).GetMethod(
                nameof(Finalizer),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            Plugin.Logger.LogDebug("PartShrinker.OnDisable NPE suppressor installed.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Could not install PartShrinker suppressor: {ex.Message}");
        }
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception is NullReferenceException) return null;
        return __exception;
    }
}
