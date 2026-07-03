using HarmonyLib;

namespace MoreHeadBridge;

// ── Intercept clicks on our Tools buttons ────────────────────────────────────
// IL2CPP can't clear serialized persistent listeners on Button.onClick — patch OnSelect with a prefix instead: our buttons run our action and return false, so onClick (and vanilla TogglePopupReset) never fires.

[HarmonyPatch(typeof(MenuButton), "OnSelect")]
internal static class ToolsButtonOnSelectPatch
{
    [HarmonyPrefix]
    private static bool Prefix(MenuButton __instance)
    {
        if (!CosmeticsMenuStartPatch._buttonActions.TryGetValue(__instance, out var action))
            return true;

        try { action?.Invoke(); }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"ToolsButton action error: {ex.Message}");
        }
        return false;
    }
}

// ── Close Tools popup when any vanilla popup opens ──────────────────────────

[HarmonyPatch(typeof(MenuPageCosmetics), "TogglePopupColor")]
internal static class TogglePopupColorPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var p = CosmeticsMenuStartPatch._toolsPopupRef;
        if (p != null) p.SetState(_state: false);
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "TogglePopupRandomize")]
internal static class TogglePopupRandomizePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var p = CosmeticsMenuStartPatch._toolsPopupRef;
        if (p != null) p.SetState(_state: false);
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "TogglePopupReset")]
internal static class TogglePopupResetPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var p = CosmeticsMenuStartPatch._toolsPopupRef;
        if (p != null) p.SetState(_state: false);
    }
}
