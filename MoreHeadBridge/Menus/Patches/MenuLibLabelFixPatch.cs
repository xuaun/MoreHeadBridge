using HarmonyLib;
using MenuLib.MonoBehaviors;
using UnityEngine;

namespace MoreHeadBridge;

// REPOLabel sizes its TMP with an absolute sizeDelta, but the template's TMP stretches over its parent, so that size gets added on top — labels end up twice as wide and tall, off-center. Anchoring the TMP to a point keeps the size literal. Registered manually — MenuLib is a soft dep.
internal static class MenuLibLabelFixPatch
{
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var method = AccessTools.Method(typeof(MenuLib.MenuAPI), nameof(MenuLib.MenuAPI.CreateREPOLabel));
            if (method == null) return;

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MenuLibLabelFixPatch), nameof(Postfix)));
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"MenuLibLabelFixPatch failed: {ex.Message}");
        }
    }

    private static void Postfix(REPOLabel __result)
    {
        if (__result == null || __result.labelTMP == null) return;
        var rect = __result.labelTMP.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
    }
}
