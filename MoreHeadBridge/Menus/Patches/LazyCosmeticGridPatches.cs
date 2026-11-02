using HarmonyLib;

namespace MoreHeadBridge;

// Runs after the scroll box hides off-screen sections, so only the sections left active create rows.
[HarmonyPatch(typeof(MenuScrollBox), "LateUpdate")]
internal static class LazyCosmeticGridScrollPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuScrollBox __instance) => LazyCosmeticGrid.AfterScrollBoxLateUpdate(__instance);
}

[HarmonyPatch(typeof(MenuScrollBox), nameof(MenuScrollBox.RecalculateScrollHeight))]
internal static class LazyCosmeticGridScrollEdgePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuScrollBox __instance) => LazyCosmeticGrid.FixScrollEdge(__instance);
}
