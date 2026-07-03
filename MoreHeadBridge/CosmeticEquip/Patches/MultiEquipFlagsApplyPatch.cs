using HarmonyLib;

namespace MoreHeadBridge;

// Applies MultiEquipTypeFlags once MetaManager (and its cosmeticTypeAssets) exist — flips canEquipMultiple
// for the multi-equip types when AllowMultipleCosmetics is on. Runtime config toggles route through Plugin.OnAllowMultipleCosmeticsChanged.
[HarmonyPatch(typeof(MetaManager), "Awake")]
internal static class MultiEquipFlagsApplyPatch
{
    [HarmonyPostfix]
    private static void Postfix() => MultiEquipTypeFlags.Sync();
}
