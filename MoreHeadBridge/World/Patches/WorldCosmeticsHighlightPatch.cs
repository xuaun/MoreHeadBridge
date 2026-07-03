using HarmonyLib;
using System;
using System.Collections.Generic;

namespace MoreHeadBridge;

// Vanilla UpdateHighlight counts all Hat-type assets as one bucket; since worlds share Hat type, category buttons and section headers would show hat+world combined. These two patches split the count correctly.

[HarmonyPatch(typeof(MenuElementButtonCosmeticCategory), "UpdateHighlight")]
internal static class WorldCosmeticsHighlightPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementButtonCosmeticCategory __instance)
    {
        if (__instance == null) return;
        if (MetaManager.instance == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;

        // Virtual categories (SEARCH, SELECTED) never show new-unlock counts.
        if (CosmeticsMenuState.IsVirtual(__instance.category))
        {
            if (__instance.highlightObj?.text != null)
                __instance.highlightObj.text.text = "0";
            return;
        }

        int count;
        if (__instance.buttonType == MenuElementButtonCosmeticCategory.ButtonType.Category)
        {
            if (__instance.category == null || __instance.highlightObj == null) return;

            bool isWorldCategory = WorldCosmeticsMenuState.IsWorldCategory(__instance.category);
            bool includesHat = __instance.category.typeList != null &&
                               __instance.category.typeList.Contains(SemiFunc.CosmeticType.Hat);
            if (!isWorldCategory && !includesHat) return;

            count = CountNewCosmetics(asset =>
                isWorldCategory
                    ? HhhCosmeticLoader.IsWorldAsset(asset)
                    : MatchesCategory(asset, __instance.category) &&
                      !HhhCosmeticLoader.IsWorldAsset(asset));
        }
        else if (__instance.buttonType == MenuElementButtonCosmeticCategory.ButtonType.SubCategory)
        {
            if (__instance.highlightObj == null) return;

            // Virtual categories never show new-unlock counts on any subcategory type. Runs before the Hat-only branch so all types are covered, complementing VirtualCategoryHighlightPatch regardless of postfix order.
            var page = __instance.GetComponentInParent<MenuPageCosmetics>();
            if (CosmeticsMenuState.IsVirtual(page?.selectedCategory))
            {
                if (__instance.highlightObj.text != null)
                    __instance.highlightObj.text.text = "0";
                return;
            }

            if (__instance.subCategory != SemiFunc.CosmeticType.Hat) return;

            count = CountNewCosmetics(asset =>
                asset.type == __instance.subCategory &&
                !HhhCosmeticLoader.IsWorldAsset(asset));
        }
        else
        {
            return;
        }

        SetHighlightCount(__instance.highlightObj, count);
    }

    private static bool MatchesCategory(CosmeticAsset asset, CosmeticCategoryAsset category)
        => category.typeList != null && category.typeList.Contains(asset.type);

    private static int CountNewCosmetics(Func<CosmeticAsset, bool> predicate)
    {
        var meta       = MetaManager.instance;
        var historySet = new HashSet<int>(meta.cosmeticHistory);
        int count      = 0;

        foreach (int index in meta.cosmeticUnlocks)
        {
            if (index < 0 || index >= meta.cosmeticAssets.Count) continue;
            if (historySet.Contains(index)) continue;

            var asset = meta.cosmeticAssets[index];
            if (asset != null && predicate(asset))
                count++;
        }

        return count;
    }

    private static void SetHighlightCount(MenuElementCosmeticHighlight highlight, int count)
    {
        // Write count as vanilla does; MenuElementCosmeticHighlight.Update() handles badge visibility and text margin.
        if (highlight.text != null)
            highlight.text.text = count.ToString();
    }
}

// Vanilla UpdateHighlight on sections also counts hat+world combined; this postfix recalculates with the correct split and syncs the sticky header.
[HarmonyPatch(typeof(MenuElementCosmeticSection), "UpdateHighlight")]
internal static class WorldCosmeticsSectionHighlightPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticSection __instance)
    {
        if (__instance == null) return;
        if (MetaManager.instance == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        if (__instance.subCategory != SemiFunc.CosmeticType.Hat) return;
        if (__instance.highlightObj == null) return;

        // Virtual categories (SEARCH, SELECTED, FAV, HIDE) never show new-unlock counts.
        if (CosmeticsMenuState.IsVirtual(__instance.menuPageCosmetics?.selectedCategory))
        {
            if (__instance.highlightObj.text != null)
                __instance.highlightObj.text.text = "0";
            if (__instance.menuPageCosmetics != null &&
                __instance.menuPageCosmetics.selectedSubCategory == __instance.subCategory &&
                __instance.menuPageCosmetics.stickyHeader?.highlightObj?.text != null)
                __instance.menuPageCosmetics.stickyHeader.highlightObj.text.text = "0";
            return;
        }

        bool inWorldCategory = WorldCosmeticsMenuState.IsWorldCategory(
            __instance.menuPageCosmetics?.selectedCategory);

        var historySet = new HashSet<int>(MetaManager.instance.cosmeticHistory);
        var unlocksSet = new HashSet<int>(MetaManager.instance.cosmeticUnlocks);
        int count = 0;
        for (int i = 0; i < MetaManager.instance.cosmeticAssets.Count; i++)
        {
            if (historySet.Contains(i)) continue;
            if (!unlocksSet.Contains(i)) continue;

            var asset = MetaManager.instance.cosmeticAssets[i];
            if (asset == null || asset.type != SemiFunc.CosmeticType.Hat) continue;
            if (!asset.prefab.IsValid()) continue;

            bool isWorld = HhhCosmeticLoader.IsWorldAsset(asset);
            if (inWorldCategory ? isWorld : !isWorld)
                count++;
        }

        if (__instance.highlightObj.text != null)
            __instance.highlightObj.text.text = count.ToString();

        // Sync the sticky header exactly as vanilla does.
        if (__instance.menuPageCosmetics != null &&
            __instance.menuPageCosmetics.selectedSubCategory == __instance.subCategory &&
            __instance.menuPageCosmetics.stickyHeader?.highlightObj?.text != null)
        {
            __instance.menuPageCosmetics.stickyHeader.highlightObj.text.text = count.ToString();
        }
    }
}
