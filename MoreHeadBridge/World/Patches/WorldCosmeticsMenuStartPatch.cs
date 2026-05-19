using HarmonyLib;
using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

// Injects the WORLD category button into the cosmetics menu on Start.
[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class WorldCosmeticsMenuStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        if (__instance == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;
        // Guard against the Presets/Outfits tab — on that page selectedCategory can be
        // null during Start(), causing NPEs in UpdateColorButton.
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;

        WorldCosmeticsMenuState.CurrentPage = __instance;

        try
        {
            WorldCosmeticsMenuState.EnsureCategory();
            InjectWorldCategoryButton(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"WORLD menu injection failed: {ex.Message}");
        }
    }

    private static void InjectWorldCategoryButton(MenuPageCosmetics page)
    {
        if (WorldCosmeticsMenuState.Category == null) return;

        var existing = page.categoriesTransform
            .GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .FirstOrDefault(b => b.category == WorldCosmeticsMenuState.Category ||
                                 HasLabel(b, "WORLD"));
        if (existing != null)
        {
            existing.category = WorldCosmeticsMenuState.Category;
            return;
        }

        var obj = UnityEngine.Object.Instantiate(page.categoryButtonPrefab, page.categoriesTransform);
        var btn = obj.GetComponent<MenuElementButtonCosmeticCategory>();
        btn.category = WorldCosmeticsMenuState.Category;

        var tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.fontSize = 20f;
            tmp.text = "WORLD";
        }

        MoveAfter(page, obj.transform, "LEGS");
        page.categoriesHolder.UpdateButtons();
    }

    private static bool HasLabel(MenuElementButtonCosmeticCategory btn, string label)
    {
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();
        string value = btn.category?.categoryName ?? text?.text ?? "";
        return string.Equals(value.Trim(), label, StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveAfter(MenuPageCosmetics page, Transform item, string label)
    {
        var buttons = page.categoriesTransform
            .GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .ToList();

        var anchor = buttons.FirstOrDefault(b => HasLabel(b, label));
        if (anchor != null)
            item.SetSiblingIndex(anchor.transform.GetSiblingIndex() + 1);
    }
}
