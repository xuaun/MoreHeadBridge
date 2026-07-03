// Postfix on MenuPageCosmetics.Start: injects SEARCH/SELECTED category buttons, reorders the nav strip, creates the search field, hover tooltip and empty-state label.

using HarmonyLib;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class CosmeticsMenuStartPatch
{
    // ── Layout constants ──────────────────────────────────────────────────────
    // calibrated against the vanilla cosmetics menu — retune here after game updates

    // Status label (hover name bar) — anchored to top of page
    private const float StatusLabelOffsetX = -270f;
    private const float StatusLabelOffsetY = -330f;
    private const float StatusLabelSizeDeltaX = -610f;
    internal const float StatusLabelMinHeight = 17f;   // single-line minimum; panel auto-expands
    private const float StatusLabelFontSize = 16f;
    internal const float StatusLabelPadding = 6f; // horizontal text inset
    internal const float StatusLabelVerticalPadding = 1f; // vertical inset — kept tight to avoid overflow onto Confirm button
    // Pivot at center — panel grows equally above/below this Y. Value = top-edge Y offset − half the minimum single-line height.
    private const float StatusLabelCenterY = StatusLabelOffsetY - StatusLabelMinHeight / 2f;

    // Search field — "Top" position (above the scroll area)
    private const float SearchTopOffsetX = 60f;
    private const float SearchTopOffsetY = -60f;
    private const float SearchTopSizeDeltaX = -330f;
    private const float SearchTopHeight = 26f;

    // Search field — "Bottom" position (inside the scroll area footer)
    private const float SearchBottomOffsetX = -255f;
    private const float SearchBottomOffsetY = -290f;
    private const float SearchBottomSizeDeltaX = -580f;
    private const float SearchBottomHeight = 25f;

    // Search field — "Bottom" position when Cosmetic Customizer is enabled (extra height is needed)
    private const float SearchBottomCCOffsetY = -280f;

    private const float SearchFontSize = 17f;

    // Empty-state label (centred in scroll area)
    private const float EmptyStateLabelOffsetX = 40f;
    private const float EmptyStateLabelOffsetY = 75f;
    private const float EmptyStateLabelHeight = 30f;
    private const float EmptyStateLabelFontSize = 24f;

    // Tools button — horizontal offset from the cloned reset popup (toggle + dropdown)
    private const float ToolsButtonOffsetX = 40f;

    [HarmonyPostfix, HarmonyPriority(Priority.High)]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        // Only run on the Cosmetics tab page (not the Presets/Outfits tab).
        if (__instance.selectedTab != MenuPageCosmetics.CosmeticPageTab.Cosmetics) return;

        try
        {
            HhhCosmeticLoader.OnMenuOpen(__instance);
            CosmeticsMenuState.SetActivePage(__instance);

            // MenuTakeoverBroken: a takeover companion failed to apply — don't inject UI nothing will drive.
            if (Plugin.EnableMenuEnhancements.Value && !BridgePatcher.MenuTakeoverBroken)
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

            if (Plugin.ShowToolsButton.Value && !BridgePatcher.MenuTakeoverBroken)
                InjectToolsButton(__instance);

            // Capture a real bare-avatar photo for the Mini-Semibot icon (one-shot, if enabled & not cached).
            MiniSemibotIconCapture.TryStart(__instance);
        }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"Menu injection error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Appends SEARCH, SELECTED, FAV, and HIDE buttons to the category strip; ReorderCategoryStrip places them in the correct visual order afterwards.
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

            // don't destroy badge child — VirtualCategoryHighlightPatch needs it
            var badgeHighlight = obj.GetComponentInChildren<MenuElementCosmeticHighlight>();
            var tmp = badgeHighlight != null
                ? obj.GetComponentsInChildren<TextMeshProUGUI>()
                       .FirstOrDefault(t => t != badgeHighlight.text)
                  ?? obj.GetComponentInChildren<TextMeshProUGUI>()
                : obj.GetComponentInChildren<TextMeshProUGUI>();
            tmp.fontSize = 20f;
            tmp.text = label;
        }

        page.categoriesHolder.UpdateButtons();
    }

    // Target order: [PRESETS] [|] [SEARCH] [SELECTED] [|] [FAV] [HEAD] [BODY] [ARMS] [LEGS] [WORLD] [HIDE] — the second "|" is added by InjectSecondDivider.
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
            // if not yet injected, WorldCosmeticsMenuStartPatch inserts it after LEGS
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

    // the "|" divider is the only direct child with no button component
    internal static Transform? FindDivider(MenuPageCosmetics page)
    {
        foreach (Transform child in page.categoriesTransform)
        {
            if (child.GetComponent<MenuElementButtonCosmeticCategory>() == null)
                return child;
        }
        return null;
    }

    // typeList on virtual categories follows the real nav order (HEAD→BODY→ARMS→LEGS); WORLD is always appended last by InjectWorldSection.
    private static void BuildVirtualCategoryTypeList(MenuPageCosmetics page)
    {
        var search = CosmeticsMenuState.SearchCategory;
        var selected = CosmeticsMenuState.SelectedCategory;
        if (search == null || selected == null) return;

        var seen = new System.Collections.Generic.HashSet<SemiFunc.CosmeticType>();
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
        search.typeList = new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
        selected.typeList = new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);

        if (CosmeticsMenuState.FavoritesCategory != null)
            CosmeticsMenuState.FavoritesCategory.typeList =
                new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
        if (CosmeticsMenuState.HiddenCategory != null)
            CosmeticsMenuState.HiddenCategory.typeList =
                new System.Collections.Generic.List<SemiFunc.CosmeticType>(result);
    }

    // ── UI injection ─────────────────────────────────────────────────────────

    private static void InjectStatusLabel(MenuPageCosmetics page)
    {
        if (CosmeticsMenuState.StatusLabel != null) return;

        var refTmp = page.GetComponentInChildren<TextMeshProUGUI>();
        if (refTmp == null) return;

        var panel = new GameObject("MHB_StatusLabel");
        panel.transform.SetParent(page.transform, false);

        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        // Pivot at vertical center so expanding the height grows equally up AND down.
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(StatusLabelOffsetX, StatusLabelCenterY);
        rt.sizeDelta = new Vector2(StatusLabelSizeDeltaX, StatusLabelMinHeight);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panel.transform, false);

        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(StatusLabelPadding, 0f);
        textRT.offsetMax = new Vector2(-StatusLabelPadding, 0f);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.font = refTmp.font;
        tmp.fontSize = StatusLabelFontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text = "";
        tmp.raycastTarget = false;

        // CanvasGroup lets LateUpdate fade panel + text as one unit
        var canvasGroup = panel.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        panel.SetActive(false);
        CosmeticsMenuState.SetStatusLabel(tmp);
        CosmeticsMenuState.SetStatusLabelGroup(canvasGroup);
    }

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

        var canvasGroup = fieldGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        var bg = fieldGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(fieldGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6f, 2f);
        textRT.offsetMax = new Vector2(-6f, -2f);
        var textTMP = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.font = refTmp.font;
        textTMP.color = Color.white;
        textTMP.alignment = TextAlignmentOptions.MidlineLeft;

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(fieldGO.transform, false);
        var phRT = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(6f, 2f);
        phRT.offsetMax = new Vector2(-6f, -2f);
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.font = refTmp.font;
        phTMP.color = new Color(1f, 1f, 1f, 0.45f);
        phTMP.alignment = TextAlignmentOptions.MidlineLeft;
        phTMP.text = "Type to Search...";

        var input = fieldGO.AddComponent<TMP_InputField>();
        input.textComponent = textTMP;
        input.placeholder = phTMP;
        input.characterLimit = 64;
        input.lineType = TMP_InputField.LineType.SingleLine;
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

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);

        if (Plugin.SearchFieldPosition.Value == SearchBarPosition.Top)
        {
            rt.anchoredPosition = new Vector2(SearchTopOffsetX, SearchTopOffsetY);
            rt.sizeDelta = new Vector2(SearchTopSizeDeltaX, SearchTopHeight);
        }
        else if (Plugin.EnableCosmeticCustomizer.Value)
        {
            rt.anchoredPosition = new Vector2(SearchBottomOffsetX, SearchBottomCCOffsetY);
            rt.sizeDelta = new Vector2(SearchBottomSizeDeltaX, SearchBottomHeight);
        } else
        {
            rt.anchoredPosition = new Vector2(SearchBottomOffsetX, SearchBottomOffsetY);
            rt.sizeDelta = new Vector2(SearchBottomSizeDeltaX, SearchBottomHeight);
        }

        if (field.textComponent != null)
            field.textComponent.fontSize = SearchFontSize;

        if (field.placeholder is TextMeshProUGUI ph)
            ph.fontSize = SearchFontSize;
    }

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

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = refTmp.font;
        tmp.color = new Color(1f, 1f, 1f, 0.45f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = "No items selected";
        tmp.raycastTarget = false;

        ApplyEmptyStateLabelLayout(go);

        go.SetActive(false);
        CosmeticsMenuState.SetEmptyStateLabel(go);
    }

    private static void ApplyEmptyStateLabelLayout(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(EmptyStateLabelOffsetX, EmptyStateLabelOffsetY);
        rt.sizeDelta = new Vector2(0f, EmptyStateLabelHeight);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.fontSize = EmptyStateLabelFontSize;
    }

    // Tracks the injected popup so the vanilla toggle patches can close it.
    internal static MenuElementCosmeticButtonPopup? _toolsPopupRef;

    // Actions for our cloned buttons, keyed by MenuButton — ToolsButtonOnSelectPatch dispatches from here, bypassing onClick (IL2CPP can't reliably clear its serialized listeners).
    internal static System.Collections.Generic.Dictionary<MenuButton, System.Action>
        _buttonActions = new();

    // Assigned on each InjectToolsButton run; invoke to re-evaluate the disabled states of all 3 toolbar items.
    internal static System.Action? RefreshToolsButtons;

    // Background color of the dropdown buttons as-cloned (captured on first wire call); restores the original color when re-enabling a button.
    private static Color? _dropdownBgDefaultColor;

    // Injects the "Tools" dropdown (Generate Icons, Clear All Icons, Cosmetic Settings) to the right of the three vanilla top buttons.
    private static void InjectToolsButton(MenuPageCosmetics page)
    {
        if (page.resetPopup == null) return;
        var parent = page.resetPopup.transform.parent;
        if (parent == null) return;
        if (parent.Find("MHB_ToolsBtn") != null) return;

        var orig = page.resetPopup;

        var cloneToggleGO = Object.Instantiate(orig.gameObject, parent);
        cloneToggleGO.name = "MHB_ToolsBtn";
        var refPos = orig.transform.localPosition;
        cloneToggleGO.transform.localPosition = new Vector3(refPos.x + ToolsButtonOffsetX, refPos.y, refPos.z);

        var toolsPopup = cloneToggleGO.GetComponent<MenuElementCosmeticButtonPopup>();
        if (toolsPopup == null) return;

        // The dropdown GO is a SIBLING of the toggle GO — Instantiate on the toggle never touches it, so clone it separately and assign the field directly.
        if (orig.dropdownObj == null) return;
        var cloneDropdownGO = Object.Instantiate(orig.dropdownObj, parent);
        cloneDropdownGO.name = "MHB_ToolsDropdown";
        var origDropPos = orig.dropdownObj.transform.localPosition;
        cloneDropdownGO.transform.localPosition = new Vector3(origDropPos.x + ToolsButtonOffsetX, origDropPos.y, origDropPos.z);

        // Assign the cloned dropdown directly — a runtime field write, not a serialized-field remap, so it works in IL2CPP.
        toolsPopup.dropdownObj = cloneDropdownGO;

        // Relink the fields that ARE within the toggle-button hierarchy.
        RelinkField(orig.transform, cloneToggleGO.transform, orig.toggleButton?.transform,
            t => toolsPopup.toggleButton = t.GetComponent<MenuButton>());
        RelinkField(orig.transform, cloneToggleGO.transform, orig.mainBgObj?.transform,
            t => toolsPopup.mainBgObj = t.gameObject);
        RelinkField(orig.transform, cloneToggleGO.transform, orig.toggleSemiUI?.transform,
            t => toolsPopup.toggleSemiUI = t.GetComponent<SemiUI>());

        // Awake ran during Instantiate and populated dropdownButtons from the original's dropdown — clear and repopulate from the clone.
        toolsPopup.dropdownButtons.Clear();
        foreach (var mb in cloneDropdownGO.GetComponentsInChildren<MenuButton>(true))
            toolsPopup.dropdownButtons.Add(mb);

        _toolsPopupRef = toolsPopup;
        toolsPopup.SetState(_state: false, _skipAnim: true);

        // ToolsButtonOnSelectPatch intercepts OnSelect, so this fires instead of onClick (which still holds the serialized TogglePopupReset listener).
        var toggleMenuBtn = toolsPopup.toggleButton;
        if (toggleMenuBtn != null)
        {
            var capturedPopup = toolsPopup;
            var capturedPage = page;
            _buttonActions[toggleMenuBtn] = () =>
            {
                capturedPage.colorPopup?.SetState(_state: false);
                capturedPage.randomizePopup?.SetState(_state: false);
                capturedPage.resetPopup?.SetState(_state: false);
                capturedPopup.SetState(!capturedPopup.dropdownActive);
            };
        }

        // ── Swap icon; "M" overlay in background color ────────────────────────
        var clothesIcon = Resources.FindObjectsOfTypeAll<Sprite>()
            .FirstOrDefault(s => s.name == "clothes_icon");

        var iconImg = toggleMenuBtn?.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(img => img.gameObject.name == "Icon");
        var bgImg = toggleMenuBtn?.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(img => img.gameObject.name == "Background");
        var mColor = bgImg != null ? bgImg.color : Color.black;

        if (iconImg != null)
        {
            if (clothesIcon != null) iconImg.sprite = clothesIcon;

            var mGO = new GameObject("MHB_M");
            mGO.transform.SetParent(iconImg.transform, false);
            var mRT = mGO.AddComponent<RectTransform>();
            mRT.anchorMin = Vector2.zero;
            mRT.anchorMax = Vector2.one;
            mRT.offsetMin = Vector2.zero;
            mRT.offsetMax = Vector2.zero;
            mRT.anchoredPosition = new Vector2(0f, 6f);
            var mTMP = mGO.AddComponent<TextMeshProUGUI>();
            var refTmp = page.GetComponentInChildren<TextMeshProUGUI>();
            if (refTmp != null) mTMP.font = refTmp.font;
            mTMP.text = "M";
            mTMP.fontSize = 9f;
            mTMP.fontStyle = FontStyles.Bold;
            mTMP.alignment = TextAlignmentOptions.Center;
            mTMP.color = mColor;
            mTMP.raycastTarget = false;
        }

        // ── Wire dropdown items ───────────────────────────────────────────────
        if (toolsPopup.dropdownButtons.Count < 3) return;

        var btn0 = toolsPopup.dropdownButtons[0];
        var btn1 = toolsPopup.dropdownButtons[1];
        var btn2 = toolsPopup.dropdownButtons[2];
        var capturedPage2 = page;

        System.Action generateAction = () =>
        {
            if (capturedPage2 == null) return;
            Plugin.GenerateAllIcons.Value = true;
            BatchIconGenerator.OnBatchCompleted = () => RefreshToolsButtons?.Invoke();
            BatchIconGenerator.TryStart(capturedPage2);
        };
        System.Action clearAction = () =>
        {
            Plugin.DeleteIconCache.Value = true;
            IconCacheCleaner.Run();
            RefreshToolsButtons?.Invoke();
        };
        // Sync Customizer opens a MenuLib popup: gate the call (its JIT would TypeLoad without MenuLib) and grey the button out so the gate is visible.
        System.Action settingsAction = () =>
        {
            if (Plugin.MenuLibAvailable) CosmeticSettingsPopup.Show();
        };

        WireDropdownButton(btn0, "Generate\nIcons", generateAction, !HasIconsToGenerate(), 10f, 11f);
        WireDropdownButton(btn1, "Clear All\nIcons", clearAction, !HasIconsToDelete(), 10f, 11f);
        WireDropdownButton(btn2, "Sync\nCustomizer", settingsAction,
            !HasCosmeticSettingsData() || !Plugin.MenuLibAvailable, 8.75f, 11f);

        // Capture a refresh delegate that can re-evaluate states without reopening the menu.
        RefreshToolsButtons = () =>
        {
            UpdateToolsButton(btn0, generateAction, HasIconsToGenerate());
            UpdateToolsButton(btn1, clearAction, HasIconsToDelete());
            UpdateToolsButton(btn2, settingsAction, HasCosmeticSettingsData());
        };
    }

    // Maps target's relative path under origRoot onto cloneRoot and calls assign. Silently no-ops when target isn't a descendant (e.g. the sibling dropdownObj).
    private static void RelinkField(Transform origRoot, Transform cloneRoot,
                                    Transform? target, System.Action<Transform> assign)
    {
        if (target == null) return;
        var path = GetRelativePath(origRoot, target);
        if (path == null) return;
        var found = string.IsNullOrEmpty(path) ? cloneRoot : cloneRoot.Find(path);
        if (found != null) assign(found);
    }

    private static string? GetRelativePath(Transform root, Transform target)
    {
        if (target == root) return "";
        var parts = new System.Collections.Generic.List<string>();
        var cur = target;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        if (cur != root) return null;
        parts.Reverse();
        return string.Join("/", parts);
    }

    // Full initial setup of a cloned dropdown button: hides the vanilla Icon image, reduces font size, applies the initial state.
    private static void WireDropdownButton(
        MenuButton btn, string label, System.Action action, bool startDisabled = false,
        float fontSizeMin = 8f, float fontSizeMax = 11f)
    {
        // Hide the vanilla Icon image — our dropdown items are text-only.
        foreach (var img in btn.GetComponentsInChildren<Image>(true))
            if (img.gameObject.name == "Icon") img.gameObject.SetActive(false);

        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = fontSizeMin;
            tmp.fontSizeMax = fontSizeMax;
            tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        btn.buttonTextString = label;

        // Capture the default background color once so UpdateToolsButton can restore it.
        var bgImg = btn.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(img => img.gameObject.name == "Background");
        if (bgImg != null)
            _dropdownBgDefaultColor ??= bgImg.color;

        // Disable the confirmation-popup guard so OnSelect reaches our interceptor.
        if (btn.menuButtonPopUp != null)
            btn.menuButtonPopUp.disabled = true;

        if (tmp != null) tmp.text = label;
        ApplyToolsButtonState(btn, bgImg, action, !startDisabled);
    }

    // Re-evaluates a wired dropdown button's enabled/disabled state; called by RefreshToolsButtons after generate/clear operations complete.
    private static void UpdateToolsButton(MenuButton btn, System.Action action, bool canExecute)
    {
        var bgImg = btn.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(img => img.gameObject.name == "Background");
        ApplyToolsButtonState(btn, bgImg, action, canExecute);
    }

    private static void ApplyToolsButtonState(
        MenuButton btn, Image? bgImg, System.Action action, bool canExecute)
    {
        btn.disabled = !canExecute;
        if (canExecute) _buttonActions[btn] = action;
        else _buttonActions.Remove(btn);

        if (bgImg == null) return;
        bgImg.color = canExecute
            ? (_dropdownBgDefaultColor ?? bgImg.color)
            : new Color(0.38f, 0.38f, 0.38f, 0.55f);
    }

    // ── Toolbar button state helpers ─────────────────────────────────────────

    private static bool HasIconsToGenerate()
    {
        var meta = MetaManager.instance;
        if (meta == null) return false;
        foreach (var id in HhhCosmeticLoader.RegisteredAssetIds)
        {
            // The Mini-Semibot icon is an avatar PHOTO, never a batch-rendered mesh icon — skip it so a missing photo doesn't keep "Generate Icons" lit with nothing to produce.
            if (id == MiniSemibotCosmetic.AssetId) continue;
            var asset = meta.cosmeticAssets.Find(a => a != null && a.assetId == id);
            if (asset != null && !IconCapture.HasCache(asset)) return true;
        }
        return false;
    }

    private static bool HasIconsToDelete()
    {
        var meta = MetaManager.instance;
        if (meta == null) return false;
        foreach (var id in HhhCosmeticLoader.RegisteredAssetIds)
        {
            // The Mini-Semibot photo survives Clear All Icons (it can't be re-rendered) — skip it here too, or its cache keeps the button enabled right after a clear.
            if (id == MiniSemibotCosmetic.AssetId) continue;
            var asset = meta.cosmeticAssets.Find(a => a != null && a.assetId == id);
            if (asset != null && IconCapture.HasCache(asset)) return true;
        }
        return false;
    }

    // Gates the Sync Customizer button: enabled only when at least one OTHER player is in the room (GetRemotePlayersWithData excludes the local player, empty in singleplayer).
    private static bool HasCosmeticSettingsData()
        => CustomizerSync.GetRemotePlayersWithData().Count > 0;

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

        // buttonTextString is serialized and survives translation mods (XUnity rewrites the rendered TMP text, which made PRESETS unmatchable).
        var menuButton = btn.GetComponentInChildren<MenuButton>() ?? btn.GetComponentInParent<MenuButton>();
        if (menuButton != null && !string.IsNullOrWhiteSpace(menuButton.buttonTextString))
            yield return menuButton.buttonTextString;

        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) yield return tmp.text;
    }

    private static string Normalize(string s)
        => new string(s.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
