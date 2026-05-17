using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// Vanilla's bulk-action buttons modify cosmeticEquipped and call CosmeticPlayerUpdateLocal,
// but never call RefreshScrollContent. When the SELECTED or SEARCH virtual category is
// active, the list goes stale — items don't appear/disappear until the user switches tab.
[HarmonyPatch]
internal static class CosmeticsBulkActionRefreshPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeBodyButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetAllButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetBodyButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetCosmeticsButton");
    }

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (!CosmeticsMenuState.IsVirtual(__instance.selectedCategory)) return;
        __instance.RefreshScrollContent();
    }
}
