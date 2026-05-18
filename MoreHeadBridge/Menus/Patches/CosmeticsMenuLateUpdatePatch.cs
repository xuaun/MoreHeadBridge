// Postfix on MenuPageCosmetics.LateUpdate.
// • When hovering a cosmetic: shows its name (with [FAV]/[HIDE] prefix if applicable).
// • When nothing is hovered: fades in a usage hint after 2 s of inactivity.
//   The hint is shown in all Cosmetics sub-tabs (including FAV and HIDE).
//   In the Presets tab the label stays hidden.

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "LateUpdate")]
internal static class CosmeticsMenuLateUpdatePatch
{
    private const string Hint         = "Ctrl+click = Fav\nAlt+click = Hide";
    private const float  HintDelay    = 2f;    // seconds before fade begins
    private const float  HintFade     = 0.5f;  // fade-in duration
    private const float  HintAlpha    = 0.4f;  // target opacity

    private static readonly Color NormalColor = Color.white;

    // Accumulated time with no cosmetic hovered, reset whenever hover begins.
    private static float _noHoverTime;

    // Called from BatchIconGeneratorMenuClosePatch when the menu is destroyed,
    // so the next open starts the idle timer fresh instead of instantly showing
    // the hint from the previous session.
    internal static void OnMenuClosed() => _noHoverTime = 0f;

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

        // Always keep the parent active while in the Cosmetics tab;
        // visibility is controlled through the label's color alpha.
        label.transform.parent.gameObject.SetActive(true);

        var hovered = __instance.hoveredCosmeticButton;
        bool hasHover = false;
        if (hovered != null)
            hasHover = hovered.cosmeticAsset != null || IsLocked(hovered);

        if (hasHover)
        {
            _noHoverTime = 0f;

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

            label.text  = displayName;
            label.color = NormalColor;
        }
        else
        {
            // Accumulate idle time and compute fade-in alpha.
            _noHoverTime += Time.unscaledDeltaTime;

            float fadeProgress = Mathf.Clamp01((_noHoverTime - HintDelay) / HintFade);
            label.text  = Hint;
            label.color = new Color(1f, 1f, 1f, fadeProgress * HintAlpha);
        }
    }

    // A cosmetic is locked when its index is absent from MetaManager.cosmeticUnlocks.
    // Bridge / mod-injected assets (not in cosmeticAssets) are never considered locked.
    // Uses CosmeticsMenuState.GetAssetIndex() which caches the asset→index map for the
    // lifetime of the menu session, avoiding an O(n) List.IndexOf every LateUpdate frame.
    private static bool IsLocked(MenuElementCosmeticButton? btn)
    {
        var asset = btn?.cosmeticAsset;
        if (asset == null || MetaManager.instance == null) return false;

        int idx = CosmeticsMenuState.GetAssetIndex(asset);
        if (idx < 0) return false;

        return !MetaManager.instance.cosmeticUnlocks.Contains(idx);
    }
}
