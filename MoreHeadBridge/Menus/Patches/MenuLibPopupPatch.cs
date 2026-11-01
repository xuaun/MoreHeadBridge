using HarmonyLib;
using MenuLib.MonoBehaviors;

namespace MoreHeadBridge;

// MenuLib opens popups at priority 0, so over a higher page (main menu, lobby, color page) they stay non-interactable; matches them to the top page. Also wires navigation on the bridge's own popups. Registered manually — MenuLib is a soft dep.
internal static class MenuLibPopupPatch
{
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var open = AccessTools.Method(typeof(REPOPopupPage), nameof(REPOPopupPage.OpenPage));
            if (open != null)
                harmony.Patch(open, postfix: new HarmonyMethod(typeof(MenuLibPopupPatch), nameof(OpenPostfix)));

            var update = AccessTools.Method(typeof(REPOScrollView), nameof(REPOScrollView.UpdateElements));
            if (update != null)
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(MenuLibPopupPatch), nameof(UpdateElementsPostfix)));
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"MenuLibPopupPatch failed: {ex.Message}");
        }
    }

    private static void OpenPostfix(REPOPopupPage __instance)
    {
        var page = __instance.menuPage;
        if (page == null || MenuManager.instance == null) return;

        page.interactionPriority = 0;   // a cached page keeps the value from its last open
        page.interactionPriority = MenuManager.instance.HighestOpenPriority();

        // PopupUI.AttachGuards marks the bridge's popups; other mods' pages may have their own layout.
        if (__instance.GetComponent<PopupScrollGuard>() != null)
            PopupNav.Attach(__instance);
    }

    private static void UpdateElementsPostfix(REPOScrollView __instance)
    {
        var nav = __instance.popupPage != null ? __instance.popupPage.GetComponent<PopupNav>() : null;
        if (nav != null) nav.MarkDirty();
    }
}
