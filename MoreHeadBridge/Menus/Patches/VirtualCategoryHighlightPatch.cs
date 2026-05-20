using HarmonyLib;

namespace MoreHeadBridge;

// Vanilla's UpdateHighlight counts every unlocked cosmetic not yet in cosmeticHistory
// and checks whether its type is in category.typeList. Our virtual categories (FAV,
// HIDE, SEARCH, SELECTED) have all CosmeticTypes in their typeList so they would
// accumulate every "new unlock" badge. Virtual categories have no new-unlock context,
// so we always force their count to zero.
[HarmonyPatch(typeof(MenuElementButtonCosmeticCategory), "UpdateHighlight")]
internal static class VirtualCategoryHighlightPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementButtonCosmeticCategory __instance)
    {
        if (!CosmeticsMenuState.IsVirtual(__instance.category)) return;

        var highlight = __instance.GetComponentInChildren<MenuElementCosmeticHighlight>();
        if (highlight != null)
            highlight.text.text = "0";
    }
}
