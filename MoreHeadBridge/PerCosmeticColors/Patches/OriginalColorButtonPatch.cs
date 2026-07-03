using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Injects into MenuPageColor.Start() for a tintable bridge cosmetic: (1) an "Original" button at grid slot 0 (vanilla buttons shift right one), and (2) a slot-selector row below the palette when the cosmetic has 2+ material slots.
// Grid math mirrors MenuPageColor.Start(): 38 px buttons, first at (0, 224), rows step −30 in Y; Original takes slot 0.
// Crash fix: disable the template's MenuButtonColor BEFORE the deferred Destroy — its Start()/LateStart() would still run and crash on colorsEquipped[-1].
[HarmonyPatch(typeof(MenuPageColor), "Start")]
internal static class OriginalColorButtonPatch
{
    // Identifies the injected slot-selector container so it's excluded from the colour-grid button count.
    private const string SlotSelectorName = "BridgeSlotSelector";

    [HarmonyPostfix]
    private static void Postfix(MenuPageColor __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        var asset = PerCosmeticColors.PendingAsset;

        var menuPage = __instance.GetComponentInParent<MenuPage>();
        var menuCosmetics = menuPage?.pageUnderThisPage?.GetComponent<MenuPageCosmetics>()?.menuPage?.playerAvatarMenu?.playerCosmetics;

        bool perCosmeticBridge = asset != null && BridgeIds.IsBridgeAsset(asset) && asset.tintable;

        // Modded cosmetic with a per-part slot layout (e.g. YoshiCarry): ModdedTintInjector gave it BridgeTintMaterials with grouped slots, so it gets the full bridge-style treatment ("M" original + slot selector), not just the "C" button.
        bool perCosmeticModded = asset != null
            && !BridgeIds.IsBridgeAsset(asset)
            && ModdedSlotLayout.Handles(asset)
            && CountMaterialSlotsForAsset(asset, menuCosmetics) > 1;

        bool perCosmeticBridgeLike = perCosmeticBridge || perCosmeticModded;

        // Other vanilla/modded tintable cosmetics: only the "C" button. CosmeticAsset.tintable is canonical (Cosmetic.Setup() keeps it matching PlayerMaterial.tintable) — no live-PM scan needed.
        bool perCosmeticNonBridge = asset != null
            && !BridgeIds.IsBridgeAsset(asset)
            && !perCosmeticModded
            && VanillaTintHelper.IsEligibleForCustomColor(asset);

        bool perCosmetic = perCosmeticBridgeLike || perCosmeticNonBridge;
        bool isSectionMode   = !perCosmetic && asset == null && HasEligibleBridgeForSection(__instance);
        bool hasSectionCustom = !perCosmetic && asset == null && VanillaTintHelper.HasAnyTintableForSection(__instance);

        if (!perCosmetic && !isSectionMode && !hasSectionCustom) return;

        // Non-bridge per-cosmetic without a slot layout: only "C" button, exit early.
        if (perCosmeticNonBridge && !perCosmeticBridgeLike)
        {
            InjectNonBridgeCButton(__instance, asset!);
            return;
        }

        // Section with ONLY vanilla/modded tintable cosmetics (no bridge "M"): "C" only, exit early.
        if (!perCosmetic && !isSectionMode && hasSectionCustom)
        {
            int ck = SectionColorButtonPatch.PendingWorldSection
                ? (int)CosmeticsFilterPatch.WorldSubCategory
                : __instance.colorKey;
            InjectSectionCButton(__instance, ck);
            return;
        }

        var holder = __instance.colorButtonHolder;
        if (holder == null) return;
        if (holder.childCount == 0) return;

        // ── Find the template: first non-injected child of the holder ─────────
        // Vanilla inserts buttons with SetSiblingIndex(0), so index 0 is the LAST colour button added (page is recreated each open — no stale injections).
        GameObject? templateGO = null;
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name != SlotSelectorName) { templateGO = child.gameObject; break; }
        }
        if (templateGO == null) return;

        // ── Collect all existing colour-button RectTransforms ─────────────────
        // Done before we add the Original button so the list stays clean.
        var colorRTs = new List<RectTransform>();
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name == SlotSelectorName) continue;
            var crt = child.GetComponent<RectTransform>();
            if (crt != null) colorRTs.Add(crt);
        }

        // ── Grid-end position for injected buttons ────────────────────────────
        // colorRTs[0] is the last palette slot (vanilla's SetSiblingIndex(0) reverses hierarchy order) — keep vanilla colours in place and add ours at the next free slot.
        const float step = 38f;
        float rowW = holder.rect.width;

        var origPos = colorRTs[0].anchoredPosition;   // last grid slot
        float nx = origPos.x + step;                  // next free slot after the palette
        float ny = origPos.y;
        if (nx > rowW) { nx = 0f; ny -= 30f; }

        // Template glyph (for font/material on injected overlays); taken from the palette template, not a specific button.
        var templateTmp = templateGO.GetComponentInChildren<TextMeshProUGUI>(true);

        bool animActive   = perCosmetic && PerCosmeticColors.HasAnimation(asset!.assetId);
        bool customActive = perCosmetic && PerCosmeticColors.HasCustomColor(asset!.assetId);

        // Resolved section key — real WorldSubCategory (999) even when colorKey was clamped; needed by the "M" section-mode init and the section "C" injection.
        int resolvedSectionKey = SectionColorButtonPatch.PendingWorldSection
            ? (int)CosmeticsFilterPatch.WorldSubCategory
            : __instance.colorKey;

        // ── "M" Original button at the grid-end slot ──────────────────────────
        // Bridge & section cosmetics get it; modded slot cosmetics (YoshiCarry) skip it — their no-override default is the vanilla type colour, not an author "original" to reset to.
        if (!perCosmeticModded)
        {
            var go = Object.Instantiate(templateGO, holder);
            go.name = "OriginalColorButton";

            // Remove vanilla colour logic before it runs Start()/LateStart().
            var vanillaComp = go.GetComponent<MenuButtonColor>();
            if (vanillaComp != null)
            {
                vanillaComp.enabled = false;
                Object.Destroy(vanillaComp);
            }

            // Tint: cosmetic's original colour (per-cosmetic) or white (section).
            Color origColor = perCosmetic
                ? BridgeOriginalColorButton.FindOriginalColor(asset!, menuCosmetics)
                : Color.white;
            var menuBtn = go.GetComponent<MenuButton>();
            if (menuBtn != null)
            {
                menuBtn.colorNormal = origColor + Color.black * 0.5f;   // vanilla swatch rest tint
                menuBtn.colorHover = origColor;
                menuBtn.colorClick = origColor + Color.white * BridgeOriginalColorButton.ClickWhiteAmount;   // re-primed each frame by BridgeOriginalColorButton
            }

            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(nx, ny);

            // Wire up the custom component.
            var btn = go.AddComponent<BridgeOriginalColorButton>();
            btn.menuPageColor = __instance;
            btn.originalColor = origColor;

            if (perCosmetic)
            {
                btn.cosmeticAsset = asset;
                // Indicator on Original only when no animation/custom is active AND (no override at all — bridge default — or the OriginalColorSentinel is stored).
                btn.initiallySelected = !animActive && !customActive
                    && ((!PerCosmeticColors.HasOverride(asset!.assetId)
                         && !PerCosmeticColors.HasAnySlotColor(asset.assetId))
                        || PerCosmeticColors.IsOriginalMode(asset.assetId));
            }
            else
            {
                btn.sectionMode = true;
                btn.sectionColorKey = resolvedSectionKey;
                btn.sectionPageMode = __instance.pageMode;
                btn.initiallySelected = false;
            }

            // "M" overlay — subtle marker that this is the bridge-original button.
            var mGlyph = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (mGlyph != null) StripShadow(mGlyph);   // base glyph (the coloured square) — kill its drop-shadow

            var labelGO = new GameObject("OriginalColorLabel");
            labelGO.transform.SetParent(go.transform, worldPositionStays: false);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "M";
            if (templateTmp != null)
            {
                tmp.font = templateTmp.font;
                BridgeSlotSelectorRow.ApplyOutline(tmp, templateTmp, 0.50f);   // overlay inherits clean material
            }
            tmp.fontSize = 12f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 1f, 1f, 0.35f);  // light enough to feel subtle
            tmp.raycastTarget = false;
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            // Must live on the button GO (which has the MenuButton), not the label, so it can read MenuButton.hovering — same as the slot/arrow proxies.
            var hover = go.AddComponent<OriginalLabelHoverAdjuster>();
            hover.labelRT = labelRT;

            AddTooltip(go, templateTmp, "Original Mod color");
        }

        // ── Accent buttons (C, then A) at successive grid slots ───────────────
        // Each accent calls AdvanceAccentSlot() before placing, so start one slot BEFORE the target. When "M" occupies the grid-end slot (nx,ny), the first accent lands after it; when "M" is skipped (modded), the first accent takes the grid-end slot itself.
        float accentX = perCosmeticModded ? nx - step : nx;
        float accentY = ny;
        float lowestBtnY = ny;
        void AdvanceAccentSlot()
        {
            accentX += step;
            if (accentX > rowW) { accentX = 0f; accentY -= 30f; }   // wrap like the vanilla grid
            lowestBtnY = Mathf.Min(lowestBtnY, accentY);
        }

        // Custom Colour ("C", right after "M") — opens CustomColorPopup, so it needs MenuLib. Per-cosmetic "Allow Custom Color" override wins, else the global bridge flag.
        if (perCosmetic && Plugin.MenuLibAvailable && CustomizerStore.GetEffectiveCustomColors(asset))
        {
            AdvanceAccentSlot();
            Color custTint = customActive && PerCosmeticColors.TryGetCustomColor(asset!.assetId, out var cc)
                ? cc : new Color(0.85f, 0.5f, 0.95f);   // soft purple accent when no custom set yet
            var (custGO, custLabelRT) = MakeAccentButton(holder, templateGO, templateTmp,
                "C", custTint, accentX, accentY, "Custom color");

            var custProxy = custGO.AddComponent<BridgeCustomColorButton>();
            custProxy.cosmeticAsset = asset;
            custProxy.labelRT = custLabelRT;
            custProxy.menuPageColor = __instance;
            custProxy.selectedColor = custTint;
            custProxy.initiallySelected = customActive;
        }

        // Animate Colour ("A") — opens ColorAnimationPopup, so it needs MenuLib (Custom takes selection priority). Per-cosmetic "Allow Animated Color" override wins, else the global bridge flag. Skipped for modded slot cosmetics (palette + per-slot custom only).
        if (perCosmetic && !perCosmeticModded && Plugin.MenuLibAvailable && CustomizerStore.GetEffectiveColorAnimations(asset))
        {
            AdvanceAccentSlot();
            Color animTint = new Color(0.55f, 0.8f, 1f);   // light-blue accent
            var (animGO, animLabelRT) = MakeAccentButton(holder, templateGO, templateTmp,
                "A", animTint, accentX, accentY, "Animated color");

            var animProxy = animGO.AddComponent<BridgeAnimateButton>();
            animProxy.cosmeticAsset = asset;
            animProxy.labelRT = animLabelRT;
            animProxy.menuPageColor = __instance;
            animProxy.selectedColor = animTint;
            animProxy.initiallySelected = animActive && !customActive;
        }

        // Section "C" — after the "M"/"A" buttons when any equipped cosmetic in this section is eligible for custom RGB (vanilla, bridge, or modded with config flag). Opens CustomColorPopup, so it needs MenuLib.
        if (!perCosmetic && Plugin.MenuLibAvailable && hasSectionCustom)
        {
            AdvanceAccentSlot();
            Color custTint = new Color(0.85f, 0.5f, 0.95f);
            var (custGO, custLabelRT) = MakeAccentButton(holder, templateGO, templateTmp,
                "C", custTint, accentX, accentY, "Custom color");

            var custProxy = custGO.AddComponent<BridgeCustomColorButton>();
            custProxy.menuPageColor = __instance;
            custProxy.labelRT = custLabelRT;
            custProxy.selectedColor = custTint;
            custProxy.initiallySelected = false;
            custProxy.sectionMode = true;
            custProxy.sectionColorKey = resolvedSectionKey;
            custProxy.sectionPageMode = __instance.pageMode;
        }

        // ── Slot selector: only for per-cosmetic mode ─────────────────────────
        if (perCosmetic)
        {
            int slotCount = CountMaterialSlotsForAsset(asset!, menuCosmetics);
            if (slotCount > 1)
            {
                const float titleH = 16f;
                const float titleGap = 2f;
                float slotRowY = Mathf.Min(origPos.y, lowestBtnY) - 30f - BridgeSlotSelectorRow.SlotSelectorH + 26f
                               - titleH - titleGap;
                InjectSlotSelector(holder, templateGO, asset!, slotCount, __instance, slotRowY);
                InjectSlotTitle(holder, slotRowY + BridgeSlotSelectorRow.SlotSelectorH + titleGap, titleH);
                // Via coroutine: layout groups may reset anchoredPosition this frame, and rt.rect.height needs one canvas layout pass to be valid.
                __instance.StartCoroutine(ShiftConfirmNextFrame(__instance, slotRowY));
            }
        }
    }

    // Vanilla/modded per-cosmetic mode: only the "C" button — no BridgeTintMaterial, so "M"/slots/"A" are meaningless.
    private static void InjectNonBridgeCButton(MenuPageColor menuPageColor, CosmeticAsset asset)
    {
        if (!Plugin.MenuLibAvailable) return; // "C" opens CustomColorPopup (MenuLib UI)
        var holder = menuPageColor.colorButtonHolder;
        if (holder == null || holder.childCount == 0) return;

        GameObject? templateGO = null;
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name != SlotSelectorName) { templateGO = child.gameObject; break; }
        }
        if (templateGO == null) return;

        var colorRTs = new System.Collections.Generic.List<RectTransform>();
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name == SlotSelectorName) continue;
            var crt = child.GetComponent<RectTransform>();
            if (crt != null) colorRTs.Add(crt);
        }
        if (colorRTs.Count == 0) return;

        bool customActive = PerCosmeticColors.HasCustomColor(asset.assetId);
        Color custTint = customActive && PerCosmeticColors.TryGetCustomColor(asset.assetId, out var cc)
            ? cc : new Color(0.85f, 0.5f, 0.95f);

        const float step = 38f;
        float rowW = holder.rect.width;
        var lastPos = colorRTs[0].anchoredPosition;
        float cx = lastPos.x + step;
        float cy = lastPos.y;
        if (cx > rowW) { cx = 0f; cy -= 30f; }

        var templateTmp = templateGO.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        var (custGO, custLabelRT) = MakeAccentButton(holder, templateGO, templateTmp,
            "C", custTint, cx, cy, "Custom color");

        var custProxy = custGO.AddComponent<BridgeCustomColorButton>();
        custProxy.cosmeticAsset = asset;
        custProxy.labelRT = custLabelRT;
        custProxy.menuPageColor = menuPageColor;
        custProxy.selectedColor = custTint;
        custProxy.initiallySelected = customActive;
    }

    // Injects a "C" (Custom Colour) section button when only vanilla/modded tintable cosmetics are in the section (no bridge cosmetics → no "M" before it).
    private static void InjectSectionCButton(MenuPageColor menuPageColor, int resolvedSectionKey)
    {
        if (!Plugin.MenuLibAvailable) return; // "C" opens CustomColorPopup (MenuLib UI)
        var holder = menuPageColor.colorButtonHolder;
        if (holder == null || holder.childCount == 0) return;

        GameObject? templateGO = null;
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name != SlotSelectorName) { templateGO = child.gameObject; break; }
        }
        if (templateGO == null) return;

        var colorRTs = new System.Collections.Generic.List<RectTransform>();
        for (int i = 0; i < holder.childCount; i++)
        {
            var child = holder.GetChild(i);
            if (child.name == SlotSelectorName) continue;
            var crt = child.GetComponent<RectTransform>();
            if (crt != null) colorRTs.Add(crt);
        }
        if (colorRTs.Count == 0) return;

        const float step = 38f;
        float rowW = holder.rect.width;
        var lastPos = colorRTs[0].anchoredPosition;
        float cx = lastPos.x + step;
        float cy = lastPos.y;
        if (cx > rowW) { cx = 0f; cy -= 30f; }

        var templateTmp = templateGO.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        Color custTint = new Color(0.85f, 0.5f, 0.95f);
        var (custGO, custLabelRT) = MakeAccentButton(holder, templateGO, templateTmp,
            "C", custTint, cx, cy, "Custom color");

        var custProxy = custGO.AddComponent<BridgeCustomColorButton>();
        custProxy.menuPageColor = menuPageColor;
        custProxy.labelRT = custLabelRT;
        custProxy.selectedColor = custTint;
        custProxy.initiallySelected = false;
        custProxy.sectionMode = true;
        custProxy.sectionColorKey = resolvedSectionKey;
        custProxy.sectionPageMode = menuPageColor.pageMode;
    }

    internal static bool HasEligibleBridgeForSection(MenuPageColor menuPageColor)
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return false;

        // colorKey is clamped to 0 (Hat) on World-section open to avoid a MenuButtonColor.LateStart() crash; use PendingWorldSection for the real intent.
        bool isWorldSection = menuPageColor.colorKey == (int)CosmeticsFilterPatch.WorldSubCategory
                           || SectionColorButtonPatch.PendingWorldSection;

        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (!BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset)) continue;

            bool assetIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
            if (isWorldSection)
            {
                // World section's "M" button: only world bridge cosmetics qualify.
                if (assetIsWorld) return true;
            }
            else
            {
                if (assetIsWorld)
                {
                    // Worlds are included in All/Cosmetics (multi-type), excluded from Body and specific-subcategory paint.
                    bool isMultiMode = menuPageColor.colorKey < 0;
                    bool isBodyMode  = menuPageColor.pageMode == MenuPageColor.ColorPageType.Body;
                    if (isMultiMode && !isBodyMode) return true;
                }
                else if (SectionScopeIncludes(menuPageColor, asset.type)) return true;
            }
        }
        return false;
    }

    // Mirrors vanilla's meshSwitch filter: World (999) → world bridge only; specific subcategory → its type; All → everything; Cosmetics → non-meshSwitch; Body → meshSwitch (where a bridge cosmetic forced to a mesh type belongs).
    internal static bool SectionScopeIncludes(MenuPageColor page, SemiFunc.CosmeticType type)
    {
        // World section: handled separately by HasEligibleBridgeForSection.
        if (page.colorKey == (int)CosmeticsFilterPatch.WorldSubCategory) return false;
        return VanillaTintHelper.SectionTypeScope(type, page.colorKey, page.pageMode);
    }

    private static IEnumerator ShiftConfirmNextFrame(MenuPageColor menuPageColor, float slotRowY)
    {
        yield return null;
        ShiftConfirmButton(menuPageColor, slotRowY);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Slot count = highest slot id + 1 across all the asset's BTMs. Default (no layout) that's the total material count (1 renderer × 7 materials = 7 slots); a grouped ModdedSlotLayout collapses several materials into fewer slots. Selector shows when > 1.
    private static int CountMaterialSlotsForAsset(CosmeticAsset asset, PlayerCosmetics? preferredCosmetics)
    {
        var btms = preferredCosmetics?.playerAvatarVisuals != null
            ? preferredCosmetics.playerAvatarVisuals.GetComponentsInChildren<BridgeTintMaterial>(true)
            : Object.FindObjectsOfType<BridgeTintMaterial>(true);

        int count = MaxSlotId(btms, asset) + 1;
        if (count > 0 || preferredCosmetics?.playerAvatarVisuals == null)
            return count;

        // Fallback when the current menu avatar path isn't fully built yet: broader scene scan instead of hiding the selector.
        return MaxSlotId(Object.FindObjectsOfType<BridgeTintMaterial>(true), asset) + 1;
    }

    private static int MaxSlotId(BridgeTintMaterial[] btms, CosmeticAsset asset)
    {
        int max = -1;
        foreach (var btm in btms)
        {
            if (btm?.cosmetic?.cosmeticAsset != asset) continue;
            int matCount = btm.materials?.Length ?? btm.originalPrimaryColors?.Length ?? 0;
            for (int i = 0; i < matCount; i++)
                if (btm.SlotIdOf(i) > max) max = btm.SlotIdOf(i);
        }
        return max;
    }

    private static void InjectSlotSelector(
        RectTransform holder, GameObject templateGO,
        CosmeticAsset asset, int slotCount, MenuPageColor menuPageColor, float rowY)
    {
        var containerGO = new GameObject(SlotSelectorName);
        containerGO.transform.SetParent(holder, worldPositionStays: false);

        var rt = containerGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(0f, rowY);
        rt.sizeDelta = new Vector2(holder.rect.width, BridgeSlotSelectorRow.SlotSelectorH);

        var row = containerGO.AddComponent<BridgeSlotSelectorRow>();
        row.slotCount = slotCount;
        row.containerWidth = holder.rect.width;
        row.cosmeticAsset = asset;
        row.buttonTemplate = templateGO;
        row.menuPageColor = menuPageColor;
    }

    private static void InjectSlotTitle(RectTransform holder, float rowY, float height)
    {
        var titleGO = new GameObject("SlotSelectorTitle");
        titleGO.transform.SetParent(holder, worldPositionStays: false);

        var rt = titleGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(0f, rowY);
        rt.sizeDelta = new Vector2(holder.rect.width, height);

        var existingTmp = holder.GetComponentInChildren<TextMeshProUGUI>(true);
        var tmp = titleGO.AddComponent<TextMeshProUGUI>();
        if (existingTmp != null)
        {
            tmp.font = existingTmp.font;
            tmp.fontSharedMaterial = existingTmp.fontSharedMaterial;
        }
        tmp.text = "Material slot:";
        tmp.fontSize = 16f;
        tmp.color = new Color(1f, 1f, 1f, 0.5f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
    }

    // Moves Confirm below the slot row; shift computed from the button's actual rect height so it works regardless of prefab anchor/pivot.
    private static void ShiftConfirmButton(MenuPageColor menuPageColor, float slotRowY)
    {
        var holder = menuPageColor.colorButtonHolder;
        var rt = FindConfirmRT(menuPageColor, holder);
        if (rt == null) return;

        // Target: confirm's anchoredPosition.y so the button body (height = rt.rect.height) sits 4 px below the slot row's bottom edge.
        float confirmH = rt.rect.height;
        float targetY = slotRowY - 12f;
        float needed = rt.anchoredPosition.y - targetY;
        if (needed > 0f)
            rt.anchoredPosition -= new Vector2(0f, needed);
    }

    private static RectTransform? FindConfirmRT(MenuPageColor menuPageColor, RectTransform holder)
    {
        // Name search first (vanilla button is "Menu Button - Confirm").
        foreach (Transform t in menuPageColor.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (((RectTransform?)t.GetComponent<RectTransform>())?.IsChildOf(holder) == true) continue;
            if (t.name.IndexOf("confirm", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            var rt = t.GetComponent<RectTransform>();
            if (rt != null) return rt;
        }

        // Fallback: lowest-Y MenuButton outside the holder.
        RectTransform? lowestRT = null;
        float bestY = float.PositiveInfinity;
        foreach (var mb in menuPageColor.GetComponentsInChildren<MenuButton>(true))
        {
            if (mb == null) continue;
            var rt = mb.GetComponent<RectTransform>();
            if (rt == null || rt.IsChildOf(holder)) continue;
            if (rt.anchoredPosition.y < bestY) { bestY = rt.anchoredPosition.y; lowestRT = rt; }
        }
        return lowestRT;
    }

    // Clones the swatch template into an accent button (C/A): strips the glyph shadow, tints with the vanilla rest/hover/click pattern, adds the letter overlay + tooltip. Caller attaches the click proxy.
    private static (GameObject go, RectTransform labelRT) MakeAccentButton(
        Transform holder, GameObject templateGO, TextMeshProUGUI? templateTmp,
        string letter, Color tint, float x, float y, string tooltip)
    {
        var go = Object.Instantiate(templateGO, holder);
        go.name = $"AccentColorButton_{letter}";

        var vanilla = go.GetComponent<MenuButtonColor>();
        if (vanilla != null) { vanilla.enabled = false; Object.Destroy(vanilla); }

        // Strip the base glyph's drop-shadow before the overlay is added (strip the square, not the letter).
        StripShadow(go.GetComponentInChildren<TextMeshProUGUI>(true));

        var menuBtn = go.GetComponent<MenuButton>();
        if (menuBtn != null)
        {
            menuBtn.colorNormal = tint + Color.black * 0.5f;   // vanilla swatch rest tint
            menuBtn.colorHover = tint;
            menuBtn.colorClick = tint + Color.white * 0.95f;
        }

        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);

        var labelGO = new GameObject("AccentLabel");
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = letter;
        if (templateTmp != null)
        {
            tmp.font = templateTmp.font;
            BridgeSlotSelectorRow.ApplyOutline(tmp, templateTmp, 0.50f);   // inherits clean material
        }
        tmp.fontSize = 12f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.6f);
        tmp.raycastTarget = false;
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        AddTooltip(go, templateTmp, tooltip);
        return (go, labelRT);
    }

    // Strips the TMP underlay drop-shadow on a PRIVATE material instance, so only this button is affected.
    private static void StripShadow(TextMeshProUGUI? tmp)
    {
        if (tmp == null || tmp.fontSharedMaterial == null) return;
        var m = Object.Instantiate(tmp.fontSharedMaterial);
        m.DisableKeyword("UNDERLAY_ON");
        if (m.HasProperty("_UnderlayColor")) m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0f));
        tmp.fontSharedMaterial = m;
        tmp.gameObject.AddComponent<MaterialDestroyer>().material = m;
    }

    private static void AddTooltip(GameObject button, TextMeshProUGUI? templateTmp, string text)
    {
        const float TooltipWidth = 86f;
        const float TooltipHeight = 16f;
        const float TooltipGapY = 6f;     // gap above the button
        const float TextPadX = 4f;        // inset so text doesn't touch the panel edges

        // Container with a dark semi-transparent background.
        var tipGO = new GameObject("Tooltip");
        tipGO.transform.SetParent(button.transform, worldPositionStays: false);
        var bg = tipGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        bg.raycastTarget = false;

        var rt = tipGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(TooltipWidth, TooltipHeight);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);   // top-centre of the button
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, TooltipGapY);

        // Label, stretched to fill the panel with a small horizontal inset.
        var textGO = new GameObject("Label");
        textGO.transform.SetParent(tipGO.transform, worldPositionStays: false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (templateTmp != null)
        {
            tmp.font = templateTmp.font;
            BridgeSlotSelectorRow.ApplyOutline(tmp, templateTmp, 0.50f);
        }
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.color = Color.white;

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(TextPadX, 0f);
        textRT.offsetMax = new Vector2(-TextPadX, 0f);

        tipGO.transform.SetAsLastSibling();
        tipGO.SetActive(false);

        button.AddComponent<ButtonTooltip>().tooltip = tipGO;
    }

    // Toggles a tooltip GameObject on while its MenuButton is hovered.
    internal sealed class ButtonTooltip : MonoBehaviour
    {
        internal GameObject? tooltip;
        private MenuButton? _btn;
        private void Awake() => _btn = GetComponent<MenuButton>();
        private void LateUpdate()
        {
            if (_btn == null || tooltip == null) return;
            if (tooltip.activeSelf != _btn.hovering)
                tooltip.SetActive(_btn.hovering);
        }
    }

    internal sealed class OriginalLabelHoverAdjuster : MonoBehaviour
    {
        internal RectTransform? labelRT;

        private MenuButton? _btn;

        private void Awake() => _btn = GetComponent<MenuButton>();

        private void LateUpdate()
        {
            if (_btn == null || labelRT == null) return;

            var pos = labelRT.anchoredPosition;
            float targetY = _btn.hovering ? 1f : 0f;
            if (Mathf.Approximately(pos.y, targetY)) return;
            labelRT.anchoredPosition = new Vector2(pos.x, targetY);
        }
    }
}
