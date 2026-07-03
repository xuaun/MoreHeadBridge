// Postfix on MenuPageCosmetics.LateUpdate: batch progress (overlay + status bar, inputs blocked), hovered name with [FAV]/[HIDE], idle hint after 2 s, auto-expanding panel. Hidden in the Presets tab.

using HarmonyLib;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "LateUpdate")]
internal static class CosmeticsMenuLateUpdatePatch
{
    private const string HintBase      = "Ctrl+click = Fav\nAlt+click = Hide";
    private const string HintOverride  = "\nShift+click = Edit";
    private static string Hint => Plugin.EnableCosmeticCustomizer.Value
        ? HintBase + HintOverride
        : HintBase;
    private const float  HintDelay    = 2f;    // seconds before fade begins
    private const float  HintFade     = 0.5f;  // fade-in duration
    private const float  HintAlpha    = 0.4f;  // target opacity

    private static readonly Color NormalColor     = Color.white;
    // Warm amber — distinct from both white (hover) and the semi-transparent hint.
    private static readonly Color GeneratingColor = new Color(1f, 0.80f, 0.20f, 1f);

    // During generation only ESC (Back) is allowed; the overlay Image (raycastTarget=true) blocks UI clicks.
    private static readonly List<InputKey> GeneratingAllowedKeys = new()
    {
        InputKey.Back, // ESC — closes the menu / stops the batch
    };

    // Track the last text we set so ForceMeshUpdate + panel resize only fires on changes.
    private static string _lastText = "";

    // Accumulated time with no cosmetic hovered, reset whenever hover begins.
    private static float _noHoverTime;

    // Called when the menu is destroyed, so the next open starts the idle timer fresh.
    internal static void OnMenuClosed()
    {
        _noHoverTime = 0f;
        _lastText    = "";
    }

    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        var label = CosmeticsMenuState.StatusLabel;
        if (label == null) return;

        bool inCosmetics = __instance.selectedTab == MenuPageCosmetics.CosmeticPageTab.Cosmetics;

        // Outside the Cosmetics tab: keep label hidden and reset timer.
        if (!inCosmetics)
        {
            _noHoverTime = 0f;
            label.transform.parent.gameObject.SetActive(false);
            return;
        }

        // Always keep the parent active while in the Cosmetics tab; visibility is via CanvasGroup.alpha (fades panel + text together).
        label.transform.parent.gameObject.SetActive(true);
        var group = CosmeticsMenuState.StatusLabelGroup;

        // ── Generation progress ────────────────────────────────────────────────
        // The REPOPopupPage from BatchIconGenerator handles UI blocking; DisableControlsExcept here covers game-level inputs as belt-and-suspenders.
        bool generating = BatchIconGenerator.IsGenerating;
        if (generating)
        {
            if (InputManager.instance != null)
                InputManager.instance.DisableControlsExcept(0.1f, GeneratingAllowedKeys);
            _noHoverTime = 0f;
            if (group != null) group.alpha = 1f;
            ApplyText(label, BatchIconGenerator.ProgressText, GeneratingColor);
            return;
        }

        // ── Hover ──────────────────────────────────────────────────────────────
        var hovered = __instance.hoveredCosmeticButton;
        bool hasHover = false;
        if (hovered != null)
            hasHover = hovered.cosmeticAsset != null || IsLocked(hovered);

        // Clears the post-delete capture suppression once the cursor leaves the deleted icon's button.
        CosmeticHoverPatch.HoverTick(hasHover ? hovered!.cosmeticAsset : null);

        if (hasHover)
        {
            _noHoverTime = 0f;
            if (group != null) group.alpha = 1f;

            // Vanilla skips CosmeticEquip(_isPreview:true) for already-equipped items, so CosmeticHoverPatch never fires for them — trigger icon capture here instead.
            if (!IsLocked(hovered) && hovered!.cosmeticAsset is { } hoverAsset
                && BridgeIds.IsBridgeAsset(hoverAsset)
                && !IconCapture.HasCache(hoverAsset)
                && MetaManager.instance != null
                && CosmeticsMenuState.GetAssetIndex(hoverAsset) is int hIdx
                && hIdx >= 0
                && MetaManager.instance.cosmeticEquipped.Contains(hIdx))
            {
                CosmeticHoverPatch.TryScheduleCapture(hoverAsset, __instance);
            }

            string displayName;
            if (IsLocked(hovered))
            {
                displayName = "Locked";
            }
            else
            {
                var asset = hovered!.cosmeticAsset;
                string baseName = asset?.assetName ?? asset?.name ?? "";

                BridgeFavoritesManager.EnsureLoaded();
                bool isFav  = BridgeFavoritesManager.IsFavorite(asset);
                bool isHide = BridgeFavoritesManager.IsHidden(asset);

                string prefix = "";
                if (isFav && isHide) prefix = "[FAV] [HIDE] ";
                else if (isFav)      prefix = "[FAV] ";
                else if (isHide)     prefix = "[HIDE] ";

                displayName = prefix + baseName;
            }

            ApplyText(label, displayName, NormalColor);
        }
        else
        {
            // ── Idle hint (fades in via CanvasGroup) ───────────────────────────
            _noHoverTime += Time.unscaledDeltaTime;
            float fadeProgress = Mathf.Clamp01((_noHoverTime - HintDelay) / HintFade);

            // group.alpha fades background and text as one unit; label.color stays at HintAlpha (group.alpha multiplies it).
            if (group != null) group.alpha = fadeProgress;

            // Only remeasure when text changes — avoids redundant mesh rebuilds every LateUpdate.
            if (Hint != _lastText)
                ApplyText(label, Hint, new Color(1f, 1f, 1f, HintAlpha));
            else
                label.text = Hint; // already sized and colored correctly
        }
    }

    // Sets label text + color. ForceMeshUpdate + resizes the panel only when the text changes, avoiding redundant mesh rebuilds every LateUpdate.
    private static void ApplyText(TextMeshProUGUI label, string text, Color color)
    {
        label.color = color;
        if (text == _lastText)
        {
            label.text = text;
            return;
        }
        _lastText  = text;
        label.text = text;
        label.ForceMeshUpdate();
        ResizeStatusPanel(label);
    }

    // Fit panel height to the rendered text using the tight vertical padding — the full horizontal padding would overflow onto the Confirm button.
    private static void ResizeStatusPanel(TextMeshProUGUI label)
    {
        var panelRT = label.transform.parent?.GetComponent<RectTransform>();
        if (panelRT == null) return;

        float h = Mathf.Max(
            CosmeticsMenuStartPatch.StatusLabelMinHeight,
            label.preferredHeight + CosmeticsMenuStartPatch.StatusLabelVerticalPadding * 2f);

        panelRT.sizeDelta = new Vector2(panelRT.sizeDelta.x, h);
    }

    // Locked = index absent from cosmeticUnlocks; bridge/mod-injected assets (not in cosmeticAssets) are never locked. GetAssetIndex caches the map per menu session.
    private static bool IsLocked(MenuElementCosmeticButton? btn)
    {
        var asset = btn?.cosmeticAsset;
        if (asset == null || MetaManager.instance == null) return false;

        int idx = CosmeticsMenuState.GetAssetIndex(asset);
        if (idx < 0) return false;

        return !MetaManager.instance.cosmeticUnlocks.Contains(idx);
    }
}
