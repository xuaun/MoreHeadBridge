// Postfix on MenuPageCosmetics.LateUpdate.
// Updates the hover tooltip label with the hovered cosmetic's name or "Locked".

using HarmonyLib;
using TMPro;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuPageCosmetics), "LateUpdate")]
internal static class CosmeticsMenuLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        var label = CosmeticsMenuState.StatusLabel;
        if (label == null) return;

        var hovered = __instance.hoveredCosmeticButton;
        if (hovered == null || (hovered.cosmeticAsset == null && !IsLocked(hovered)))
        {
            label.text = "";
            label.transform.parent.gameObject.SetActive(false);
            return;
        }

        label.text = IsLocked(hovered)
            ? "Locked"
            : hovered.cosmeticAsset?.assetName ?? hovered.cosmeticAsset?.name ?? "";

        label.transform.parent.gameObject.SetActive(true);
    }

    // A cosmetic is locked when its index is absent from MetaManager.cosmeticUnlocks.
    // Bridge / mod-injected assets (not in cosmeticAssets) are never considered locked.
    private static bool IsLocked(MenuElementCosmeticButton btn)
    {
        var asset = btn.cosmeticAsset;
        if (asset == null || MetaManager.instance == null) return false;

        int idx = MetaManager.instance.cosmeticAssets.IndexOf(asset);
        if (idx < 0) return false;

        return !MetaManager.instance.cosmeticUnlocks.Contains(idx);
    }
}
