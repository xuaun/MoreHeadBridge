// Patches wiring the batch icon generator into the menu (button hooks + close-interrupt).

using HarmonyLib;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class BatchIconGeneratorStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        BatchIconGenerator.TryStart(__instance);
    }
}

// Hooks MenuPageCosmetics.OnDestroy to notify BatchIconGenerator when the menu
// closes mid-batch, and clears hover-capture state so the next open starts clean.
[HarmonyPatch(typeof(MenuPageCosmetics), "OnDestroy")]
internal static class BatchIconGeneratorMenuClosePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        BatchIconGenerator.NotifyMenuClosed();
        CosmeticHoverPatch.OnMenuClosed();
        CosmeticsMenuState.OnMenuClosed();
        CosmeticsMenuLateUpdatePatch.OnMenuClosed(); // E3: reset idle-hint timer
    }
}
