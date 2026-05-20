// Postfix on MenuPageCosmetics.Start.
// Injects SEARCH and SELECTED category buttons, reorders the nav strip,
// and creates the search field, hover tooltip, and empty-state label.

using HarmonyLib;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class CosmeticsMenuStartPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.High)]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        // Only run on the Cosmetics tab page (not the Presets/Outfits tab).
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;

        try
        {
            HhhCosmeticLoader.ReapplyDefaultRarityToAll();
            CosmeticsMenuState.SetActivePage(__instance);

            if (Plugin.EnableMenuEnhancements.Value)
            {
                CosmeticsMenuState.EnsureCategories();
                InjectVirtualCategoryButtons(__instance);
                ReorderCategoryStrip(__instance);
                InjectSecondDivider(__instance);
                BuildVirtualCategoryTypeList(__instance);
                InjectStatusLabel(__instance);
                InjectSearchField(__instance);
                InjectEmptyStateLabel(__instance);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogWarning($"Menu injection error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Appends SEARCH, SELECTED, FAV, and HIDE buttons to the category strip.
    // ReorderCategoryStrip will place them in the correct visual order afterwards.
    private static void InjectVirtualCategoryButtons(MenuPageCosmetics page)
    {
        var entries = new[]
        {
            (CosmeticsMenuState.SearchCategory,    "SEARCH"),
            (CosmeticsMenuState.SelectedCategory,  "SELECTED"),
            (CosmeticsMenuState.FavoritesCategory, "FAV"),
            (CosmeticsMenuState.HiddenCategory,    "HIDE"),
        };

        foreach (var (cat, label) in entries)
        {
            if (cat == null) continue;

            // Skip if a button for this category already exists (Start called twice).
            bool exists = page.categoriesTransform
                .GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
                .Any(b => b.category == cat);
            if (exists) continue;

            var obj = Object.Instantiate(page.categoryButtonPrefab, page.categoriesTransform);
            var btn = obj.GetComponent<MenuElementButtonCosmeticCategory>();
            btn.category = cat;

            var tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
            tmp.fontSize = 20f;
            tmp.text     = label;
        }

        page.categoriesHolder.UpdateButtons();
    }

    // Sets sibling indices to produce:
    //   [PRESETS] [|] [SEARCH] [SELECTED] [|] [FAV] [HEAD] [BODY] [ARMS] [LEGS] [WORLD] [HIDE]
    // The second "|" is inserted by InjectSecondDivider after this runs.
    private static void ReorderCategoryStrip(MenuPageCosmetics page)
    {
        var desired = new (string[] Keys, bool IsDivider)[]
        {
            (new[] { "PRESETS", "PRESET", "OUTFITS", "OUTFIT" }, false),
            (new[] { "|" },                                        true),
            (new[] { "SEARCH" },                                   false),
            (new[] { "SELECTED", "EQUIPPED" },                     false),
            (new[] { "FAV" },                                      false),
            (new[] { "HEAD" },                                     false),
            (new[] { "BODY" },                                     false),
            (new[] { "ARMS" },                                     false),
            (new[] { "LEGS" },                                     false),
            // WORLD is matched here if already injected; otherwise WorldCosmeticsMenuStartPatch
            // places it after LEGS via MoveAfter, which inserts it before HIDE naturally.
            (new[] { "WORLD" },                                    false),
            (new[] { "HIDE" },                                     false),
        };

        var buttons = page.categoriesTransform
            .GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .ToList();

        Transform? divider = FindDivider(page);

        int siblingIndex = 0;
        foreach (var (keys, isDivider) in desired)
        {
            if (isDivider)
            {
                if (divider != null)
                    divider.SetSiblingIndex(siblingIndex++);
                continue;
            }

            var btn = buttons.FirstOrDefault(b => MatchesAnyLabel(b, keys));
            if (btn != null)
                btn.transform.SetSiblingIndex(siblingIndex++);
        }
    }

    // Clones the original "|" divider and places the clone between SELECTED and HEAD.
    private static void InjectSecondDivider(MenuPageCosmetics page)
    {
        var divider = FindDivider(page);
        if (divider == null) return;

        var selectedBtn = page.categoriesTransform
            .GetComponentsInChildren<MenuElementButtonCosmeticCategory>(true)
            .FirstOrDefault(b => b.category == CosmeticsMenuState.SelectedCategory);
        if (selectedBtn == null) return;

        // Guard against Start being called twice.
        foreach (Transform child in page.categoriesTransform)
        {
            if (child != divider
                && child.GetComponent<MenuElementButtonCosmeticCategory>() == null
                && child.name == divider.name + "_MHB")
                return;
        }

        var clone = Object.Instantiate(divider, page.categoriesTransform);
        clone.name = divider.name + "_MHB";
        clone.SetSiblingIndex(selectedBtn.transform.GetSiblingIndex() + 1);
    }

    // Returns the first direct child of categoriesTransform with no button component —
    // that is the static "|" divider.
    internal static Transform? FindDivider(MenuPageCosmetics page)
    {
        foreach (Transform child in page.categoriesTransform)
        {
            if (child.GetComponent<MenuElementButtonCosmeticCategory>() == null)
                return child;
        }
        return null;
    }

    // Populates typeList on the virtual categories so their sections appear in the
    // same order as the real nav strip (HEAD → BODY → ARMS → LEGS).
    // WORLD is always appended last by CosmeticsFilterPatch.InjectWorldSection.
    private static void BuildVirtualCategoryTypeList(MenuPageCosmetics page)
    {
        var search   = CosmeticsMenuState.SearchCategory;
        var selected = CosmeticsMenuState.SelectedCategory;
        if (search == null || selected == null) return;

        var seen   = new System.Collections.Generic.HashSet<SemiFunc.CosmeticType>();
        var result = new System.Collections.Generic.List<SemiFunc.CosmeticType>();

        foreach (Transform child in page.categoriesTransform)
        {
            var btn = child.GetComponent<MenuElementButtonCosmeticCategory>();
            if (btn?.category == null) continue;

            var cat = btn.category;
            if (CosmeticsMenuState.IsVirtual(cat)) continue;
            if (WorldCosmeticsMenuState.IsWorldCategory(cat)) continue;
            if (CosmeticsMenuState.IsPresetsCategory(cat)) continue;
            if (cat.typeList == null) continue;

            foreach (var type in cat.typeList)
            {
                if (seen.Add(type))
                    result.Add(type);
            }
        }

        // Give each virtual category its own list so mutations don't alias.
        search.typeList   = new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
        selected.typeList = new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);

        if (CosmeticsMenuState.FavoritesCategory != null)
            CosmeticsMenuState.FavoritesCategory.typeList =
                new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
        if (CosmeticsMenuState.HiddenCategory != null)
            CosmeticsMenuState.HiddenCategory.typeList =
                new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
    }

    // ── UI injection ─────────────────────────────────────────────────────────

    // Thin bar that shows the hovered cosmetic name or "Locked".
    private static void InjectStatusLabel(MenuPageCosmetics page)
    {
        if (CosmeticsMenuState.StatusLabel != null) return;

        var refTmp = page.GetComponentInChildren<TextMeshProUGUI>();
        if (refTmp == null) return;

        var panel = new GameObject("MHB_StatusLabel");
        panel.transform.SetParent(page.transform, false);

        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f,   1f);
        rt.anchorMax        = new Vector2(1f,   1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(-270f, -340f);
        rt.sizeDelta        = new Vector2(-610f,   17f);

        var bg   = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panel.transform, false);

        var textRT       = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6f, 0f);
        textRT.offsetMax = new Vector2(-6f, 0f);

        var tmp           = textGO.AddComponent<TextMeshProUGUI>();
        tmp.font          = refTmp.font;
        tmp.fontSize      = 16f;
        tmp.color         = Color.white;
        tmp.alignment     = TextAlignmentOptions.MidlineLeft;
        tmp.text          = "";
        tmp.raycastTarget = false;

        panel.SetActive(false);
        CosmeticsMenuState.SetStatusLabel(tmp);
    }

    // Search input field, visible only when the SEARCH tab is active.
    private static void InjectSearchField(MenuPageCosmetics page)
    {
        if (CosmeticsMenuState.SearchField != null)
        {
            // Re-apply layout so the SearchFieldPosition config takes effect without restart.
            ApplySearchFieldLayout(CosmeticsMenuState.SearchField);
            return;
        }

        var refTmp = page.GetComponentInChildren<TextMeshProUGUI>();
        if (refTmp == null) return;

        var fieldGO = new GameObject("MHB_SearchField");
        fieldGO.transform.SetParent(page.transform, false);
        fieldGO.AddComponent<RectTransform>();

        var bg   = fieldGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        var textGO       = new GameObject("Text");
        textGO.transform.SetParent(fieldGO.transform, false);
        var textRT       = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6f, 2f);
        textRT.offsetMax = new Vector2(-6f, -2f);
        var textTMP           = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.font          = refTmp.font;
        textTMP.color         = Color.white;
        textTMP.alignment     = TextAlignmentOptions.MidlineLeft;

        var phGO       = new GameObject("Placeholder");
        phGO.transform.SetParent(fieldGO.transform, false);
        var phRT       = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(6f, 2f);
        phRT.offsetMax = new Vector2(-6f, -2f);
        var phTMP           = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.font          = refTmp.font;
        phTMP.color         = new Color(1f, 1f, 1f, 0.45f);
        phTMP.alignment     = TextAlignmentOptions.MidlineLeft;
        phTMP.text          = "Type to Search...";

        var input = fieldGO.AddComponent<TMP_InputField>();
        input.textComponent  = textTMP;
        input.placeholder    = phTMP;
        input.characterLimit = 64;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.onValueChanged.AddListener(value =>
        {
            CosmeticsMenuState.SetSearchText(value ?? "");
            CosmeticsMenuState.ScheduleSearchRefresh();
        });

        ApplySearchFieldLayout(input);

        fieldGO.SetActive(false);
        CosmeticsMenuState.SetSearchField(input);
    }

    private static void ApplySearchFieldLayout(TMP_InputField field)
    {
        var rt = field.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin = new Vector2(0f,   1f);
        rt.anchorMax = new Vector2(1f,   1f);
        rt.pivot     = new Vector2(0.5f, 1f);

        if (Plugin.SearchFieldPosition.Value == SearchBarPosition.Top)
        {
            rt.anchoredPosition = new Vector2(60f, -60f);
            rt.sizeDelta        = new Vector2(-330f, 26f);
        }
        else
        {
            rt.anchoredPosition = new Vector2(-255f, -305f);
            rt.sizeDelta        = new Vector2(-580f,   25f);
        }

        if (field.textComponent != null)
            field.textComponent.fontSize = 17f;

        if (field.placeholder is TextMeshProUGUI ph)
            ph.fontSize = 17f;
    }

    // Label shown in the scroll area when SELECTED or SEARCH has nothing to display.
    private static void InjectEmptyStateLabel(MenuPageCosmetics page)
    {
        if (CosmeticsMenuState.EmptyStateLabel != null)
        {
            ApplyEmptyStateLabelLayout(CosmeticsMenuState.EmptyStateLabel);
            return;
        }

        var refTmp = page.GetComponentInChildren<TextMeshProUGUI>();
        if (refTmp == null) return;

        var go = new GameObject("MHB_EmptyState");
        go.transform.SetParent(page.transform, false);
        go.AddComponent<RectTransform>();

        var tmp           = go.AddComponent<TextMeshProUGUI>();
        tmp.font          = refTmp.font;
        tmp.color         = new Color(1f, 1f, 1f, 0.45f);
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.text          = "No items selected";
        tmp.raycastTarget = false;

        ApplyEmptyStateLabelLayout(go);

        go.SetActive(false);
        CosmeticsMenuState.SetEmptyStateLabel(go);
    }

    private static void ApplyEmptyStateLabelLayout(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin        = new Vector2(0f,   0.5f);
        rt.anchorMax        = new Vector2(1f,   0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(40f, 75f);
        rt.sizeDelta        = new Vector2(0f, 30f);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.fontSize = 24f;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool MatchesAnyLabel(MenuElementButtonCosmeticCategory btn, string[] keys)
    {
        foreach (var label in GetButtonLabels(btn))
        {
            string n = Normalize(label);
            if (n.Length == 0) continue;
            if (keys.Any(k => Normalize(k) == n)) return true;
        }
        return false;
    }

    private static System.Collections.Generic.IEnumerable<string> GetButtonLabels(
        MenuElementButtonCosmeticCategory btn)
    {
        if (btn.category?.categoryName != null) yield return btn.category.categoryName;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) yield return tmp.text;
    }

    private static string Normalize(string s)
        => new string(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
