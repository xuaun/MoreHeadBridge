using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Filters cosmetic buttons in RefreshScrollContent so world cosmetics show only in the WORLD category (and vice-versa for normal categories). Also reflows section layout and manages sub-category button visibility for WORLD.
[HarmonyPatch(typeof(MenuPageCosmetics), "RefreshScrollContent")]
internal static class WorldCosmeticsMenuFilterPatch
{
    private const float SectionSpacing = 10f;
    private const float SectionHeader = 40f;

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (__instance == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;
        if (CosmeticsMenuState.IsPresetsCategory(__instance.selectedCategory)) return;
        // Virtual categories (SEARCH, SELECTED) handle their own visibility — skip this filter so CosmeticsFilterPatch has full control.
        if (CosmeticsMenuState.IsVirtual(__instance.selectedCategory)) return;

        // The WORLD category is built directly by CosmeticsFilterPatch — this patch only filters world assets OUT of the normal (non-world) vanilla category sections.
        if (WorldCosmeticsMenuState.IsWorldCategory(__instance.selectedCategory)) return;

        bool touchedAnyButton = false;
        BridgeFavoritesManager.EnsureLoaded();

        float yPos = 0f;
        foreach (var section in __instance.sections)
        {
            var allButtons = section.cosmeticListTransform
                .GetComponentsInChildren<MenuElementCosmeticButton>(includeInactive: true);

            var cosmeticButtons = allButtons
                .Where(b => b != null && b.cosmeticAsset != null)
                .ToArray();

            int remaining = 0;
            foreach (var btn in cosmeticButtons)
            {
                // Hide world assets from the normal sections; keep everything else.
                bool shouldShow = !HhhCosmeticLoader.IsWorldAsset(btn.cosmeticAsset);

                // Never re-show items the user has explicitly hidden
                if (shouldShow && BridgeFavoritesManager.IsHidden(btn.cosmeticAsset))
                    shouldShow = false;

                if (btn.gameObject.activeSelf != shouldShow)
                {
                    btn.gameObject.SetActive(shouldShow);
                    touchedAnyButton = true;
                }

                if (shouldShow)
                    remaining++;
            }

            if (remaining == 0)
            {
                if (section.gameObject.activeSelf)
                {
                    section.gameObject.SetActive(false);
                    touchedAnyButton = true;
                }
                continue;
            }

            if (!section.gameObject.activeSelf)
                section.gameObject.SetActive(true);

            ReflowSection(section, remaining, ref yPos);
        }

        ShowAllSubCategoryButtons(__instance);

        if (touchedAnyButton)
            RebuildScroll(__instance);
    }

    private static void ReflowSection(MenuElementCosmeticSection section, int remaining, ref float yPos)
    {
        var grid = section.cosmeticListTransform.GetComponent<GridLayoutGroup>();
        if (grid == null) return;

        int columns = Mathf.Max(1, grid.constraintCount);
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)(remaining + 1) / columns));
        float gridH = grid.cellSize.y * rows
                      + grid.spacing.y * (rows - 1)
                      + grid.padding.top + grid.padding.bottom;
        float sectionH = SectionHeader + gridH;

        var sectionRect = section.GetComponent<RectTransform>();
        sectionRect.localPosition = new Vector3(
            sectionRect.localPosition.x, yPos, sectionRect.localPosition.z);
        sectionRect.sizeDelta = new Vector2(sectionRect.sizeDelta.x, sectionH);

        var listRect = section.cosmeticListTransform.GetComponent<RectTransform>();
        listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, gridH);

        LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);

        yPos -= sectionH + SectionSpacing;
    }

    private static void ShowAllSubCategoryButtons(MenuPageCosmetics page)
    {
        foreach (Transform child in page.subCategoriesTransform)
        {
            var btn = child.GetComponent<MenuElementButtonCosmeticCategory>();
            if (btn != null && btn.buttonType == MenuElementButtonCosmeticCategory.ButtonType.SubCategory)
                child.gameObject.SetActive(true);
        }
    }

    private static void RebuildScroll(MenuPageCosmetics page)
    {
        var scrollRect = page.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect?.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
    }
}
