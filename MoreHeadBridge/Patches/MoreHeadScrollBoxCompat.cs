using HarmonyLib;

namespace MoreHeadBridge;

// MoreHead's MenuScrollBox.Update prefix reads MenuElementHover.isHovering and MenuSelectableElement.menuID as fields; when they're properties its static initializer throws and the prefix aborts every scroll box in the game. Removes only that prefix, and only while its initializer fails.
internal static class MoreHeadScrollBoxCompat
{
    private const string MoreHeadGuid = "Mhz.REPOMoreHead";

    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(MenuScrollBox), "Update");
            var info = original != null ? Harmony.GetPatchInfo(original) : null;
            if (info == null) return;

            foreach (var patch in info.Prefixes)
            {
                var patchType = patch.PatchMethod.DeclaringType;
                if (patch.owner != MoreHeadGuid || patchType == null) continue;

                try
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(patchType.TypeHandle);
                    continue;
                }
                catch (System.TypeInitializationException) { }

                harmony.Unpatch(original, patch.PatchMethod);
                BridgeLog.UserWarning("MoreHead's scroll-box patch is incompatible with this game version — disabled it so menus can scroll.");
            }
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"MoreHeadScrollBoxCompat failed: {ex.Message}");
        }
    }
}
