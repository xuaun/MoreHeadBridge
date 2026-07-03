using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// Vanilla bulk actions change cosmeticEquipped without RefreshScrollContent; the SELECTED virtual tab depends on equips, so it goes stale until a tab switch without this refresh.

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
    private static void Postfix(MenuPageCosmetics __instance, MethodBase __originalMethod)
    {
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;
        if (!CosmeticsMenuState.IsSelected(__instance.selectedCategory)) return;
        int frames = __originalMethod.Name.StartsWith("Randomize") ? 3 : 2;
        __instance.StartCoroutine(DeferredRefresh(__instance, frames));
    }

    private static IEnumerator DeferredRefresh(MenuPageCosmetics page, int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null;

        if (page == null) yield break;
        if (page.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) yield break;
        if (!CosmeticsMenuState.IsSelected(page.selectedCategory)) yield break;
        page.RefreshScrollContent();
    }
}
