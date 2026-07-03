using HarmonyLib;
using System.Linq;

namespace MoreHeadBridge;

// IsEquipped() for "X" buttons uses cosmeticSection.cosmeticIndexes, which conflates hats and worlds
// (both share CosmeticType.Hat). This postfix replaces the result so each side sees only its own assets:
// world "X" checks only world indices; hat "X" excludes world indices from the equipped check.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "IsEquipped")]
internal static class WorldCosmeticsIsEquippedPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticButton __instance, ref bool __result)
    {
        if (__instance.cosmeticAsset != null) return;   // not an "X" button
        if (MetaManager.instance == null) return;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return;

        var section = __instance.cosmeticSection;
        if (section == null) return;

        bool isWorldSection =
            section.subCategory == CosmeticsFilterPatch.WorldSubCategory
            || section.gameObject.name == CosmeticsFilterPatch.WorldSectionName
            || (section.subCategory == SemiFunc.CosmeticType.Hat
                && WorldCosmeticsMenuState.IsWorldCategory(
                    WorldCosmeticsMenuState.CurrentPage?.selectedCategory));

        if (isWorldSection)
        {
            // "X" (unequip all worlds) is selected when no world cosmetic is equipped.
            __result = !MetaManager.instance.cosmeticEquipped
                .Any(idx => idx >= 0
                            && idx < MetaManager.instance.cosmeticAssets.Count
                            && HhhCosmeticLoader.IsWorldAsset(MetaManager.instance.cosmeticAssets[idx]));
            return;
        }

        if (section.subCategory == SemiFunc.CosmeticType.Hat)
        {
            __result = !MetaManager.instance.cosmeticEquipped
                .Any(idx => idx >= 0
                            && idx < MetaManager.instance.cosmeticAssets.Count
                            && MetaManager.instance.cosmeticAssets[idx].type == SemiFunc.CosmeticType.Hat
                            && !HhhCosmeticLoader.IsWorldAsset(MetaManager.instance.cosmeticAssets[idx]));
        }
    }
}
