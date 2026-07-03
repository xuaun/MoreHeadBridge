using HarmonyLib;
using MenuLib.MonoBehaviors;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

// MenuLib names every REPOButton GameObject "Button - {labelText}"; MoreHead's BUTTON_NAME is a rich-text rainbow "MOREHEAD" starting with "<color=#FF0000>M", so the GO name starts with this.
internal static class MoreHeadButtonId
{
    internal const string Prefix = "Button - <color=#FF0000>M";
    // The raw label text MoreHead passes to MenuAPI.CreateREPOButton (GO name = "Button - " + this); matched at creation time before the GO is even named.
    internal const string LabelPrefix = "<color=#FF0000>M";
}

// Zero-flash hide. The OnEnable patch can't catch the button at creation (MenuLib instantiates the active template — OnEnable fires — and renames it only on the NEXT line, so the name check fails and it renders for a frame). This postfix runs at the END of CreateREPOButton (rainbow label set, GO exists): deactivating same-frame means the button never renders. Registered manually — MenuLib is a soft dep.
internal static class HideMoreHeadButtonCreatePatch
{
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var method = AccessTools.Method(typeof(MenuLib.MenuAPI), nameof(MenuLib.MenuAPI.CreateREPOButton));
            if (method == null)
            {
                BridgeLog.Trace("MenuAPI.CreateREPOButton not found — MoreHead button zero-flash hide skipped");
                return;
            }

            var postfix = typeof(HideMoreHeadButtonCreatePatch).GetMethod(
                nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            BridgeLog.Trace("MoreHead button zero-flash hide applied");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Could not apply MoreHead button zero-flash hide: {ex.Message}");
        }
    }

    private static void Postfix(REPOButton __result, string text)
    {
        if (!Plugin.HideMoreHeadButton.Value || __result == null || text == null) return;
        if (text.StartsWith(MoreHeadButtonId.LabelPrefix, StringComparison.Ordinal))
            __result.gameObject.SetActive(false);
    }
}

// Hide the instant MenuButton enables (same frame as creation) so it never renders. The reactive Apply() below re-shows and catches pre-built buttons, but runs a frame late — the old flash.
[HarmonyPatch(typeof(MenuButton), "OnEnable")]
internal static class HideMoreHeadButtonOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuButton __instance)
    {
        if (!Plugin.HideMoreHeadButton.Value || __instance == null) return;
        if (__instance.gameObject.name.StartsWith(MoreHeadButtonId.Prefix))
            __instance.gameObject.SetActive(false);
    }
}

[HarmonyPatch(typeof(MenuManager), "Start")]
internal static class HideMoreHeadUIPatch
{
    [HarmonyPriority(Priority.Low)] // run after MoreHead's MenuManagerStartPatch creates the buttons
    [HarmonyPostfix]
    private static void Postfix() => Apply(Plugin.HideMoreHeadButton.Value);

    // FindObjectsOfType<MenuButton>(true) includes inactive objects so hidden buttons can be re-shown.
    internal static void Apply(bool hide)
    {
        foreach (var btn in UnityEngine.Object.FindObjectsOfType<MenuButton>(true))
        {
            if (btn.gameObject.name.StartsWith(MoreHeadButtonId.Prefix))
                btn.gameObject.SetActive(!hide);
        }
    }
}

[HarmonyPatch(typeof(MenuPage), nameof(MenuPage.PageStateSet))]
internal static class HideMoreHeadUIPageStatePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuPage.PageState pageState)
    {
        if (!Plugin.HideMoreHeadButton.Value) return;
        if (pageState is MenuPage.PageState.Opening or MenuPage.PageState.Active or MenuPage.PageState.Activating)
            RuntimeConfigApplier.HideMoreHeadButtonsSoon();
    }
}
