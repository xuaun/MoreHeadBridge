using HarmonyLib;

namespace MoreHeadBridge;

// MenuScrollBox only hides off-screen sections in LateUpdate, and only when the scroller moved. Buttons created during Update run Start before that, so a tab change prepared every button of the tab. Runs that check right after the sections are created.
[HarmonyPatch(typeof(MenuPageCosmetics), nameof(MenuPageCosmetics.RefreshScrollContent))]
internal static class CosmeticsScrollCullPatch
{
    private static readonly LazyFieldRef<MenuScrollBox, float> _lastScrollerY = new("lastScrollerY", "off-screen section culling");
    private static readonly System.Reflection.MethodInfo? HideOffscreenChildren = AccessTools.Method(typeof(MenuScrollBox), "LateUpdate");

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        var box = __instance.menuScrollBox;
        if (box == null || HideOffscreenChildren == null) return;

        if (_lastScrollerY.TrySet(box, float.NaN))
            HideOffscreenChildren.Invoke(box, null);
    }
}
