using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// Vanilla's bulk-action buttons modify cosmeticEquipped and call CosmeticPlayerUpdateLocal,
// but never call RefreshScrollContent. When the SELECTED virtual category is active the
// displayed list depends on which cosmetics are equipped, so it goes stale after a bulk
// action — items don't appear/disappear until the user switches tab without this refresh.

[HarmonyPatch]
internal static class CosmeticsBulkActionRefreshPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeAllButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeBodyButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "RandomizeCosmeticsButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetAllButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetBodyButton");
        yield return AccessTools.Method(typeof(MenuPageCosmetics), "ResetCosmeticsButton");
    }

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;
        if (!CosmeticsMenuState.IsSelected(__instance.selectedCategory)) return;
        __instance.RefreshScrollContent();
    }
}
