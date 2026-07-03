using HarmonyLib;

namespace MoreHeadBridge;

// Virtual categories list every CosmeticType, so vanilla's UpdateHighlight would pile every "new unlock" badge onto them — force their count to zero.
[HarmonyPatch(typeof(MenuElementButtonCosmeticCategory), "UpdateHighlight")]
internal static class VirtualCategoryHighlightPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MenuElementButtonCosmeticCategory __instance)
    {
        bool isVirtualCategory = CosmeticsMenuState.IsVirtual(__instance.category);
        bool isVirtualSubCategory = __instance.buttonType == MenuElementButtonCosmeticCategory.ButtonType.SubCategory
            && CosmeticsMenuState.IsVirtual(__instance.GetComponentInParent<MenuPageCosmetics>()?.selectedCategory);

        if (!isVirtualCategory && !isVirtualSubCategory) return true;

        var highlight = __instance.GetComponentInChildren<MenuElementCosmeticHighlight>();
        if (highlight != null)
            highlight.text.text = "0";

        return false;
    }
}

[HarmonyPatch(typeof(MenuElementCosmeticSection), "UpdateHighlight")]
internal static class VirtualCategorySectionHighlightPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MenuElementCosmeticSection __instance)
    {
        if (!CosmeticsMenuState.IsVirtual(__instance.menuPageCosmetics?.selectedCategory))
            return true;

        if (__instance.highlightObj?.text != null)
            __instance.highlightObj.text.text = "0";

        if (__instance.menuPageCosmetics != null &&
            __instance.menuPageCosmetics.selectedSubCategory == __instance.subCategory &&
            __instance.menuPageCosmetics.stickyHeader?.highlightObj?.text != null)
        {
            __instance.menuPageCosmetics.stickyHeader.highlightObj.text.text = "0";
        }

        return false;
    }
}
